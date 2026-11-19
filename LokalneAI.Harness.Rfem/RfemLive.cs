using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using LokalneAI.Harness.Rfem.Com;
using LokalneAI.Harness.Tools;

namespace LokalneAI.Harness.Rfem
{
    /// <summary>
    /// Polecenia na otwartym (żywym) RFEM 5 przez RF-COM: co jest otwarte, pręt, przekroje, przypadki i kombinacje, siły i przemieszczenia
    /// z obliczeń, reakcje podporowe oraz wskazanie obiektów kliknięciem w oknie RFEM. Każde polecenie to osobne, krótkie połączenie
    /// (<see cref="RfemComSession"/>) w wątku STA: zajęcie licencji RF-COM, odczyt, zwolnienie. Nic nie zapisuje do modelu.
    /// Jednostki dla ludzi jak w poleceniach na pliku: m, kN, kNm, cm², cm⁴, GPa, mm.
    /// </summary>
    internal sealed class RfemLive
    {
        /// <summary>Nazwy poleceń na otwartym RFEM.</summary>
        public static readonly IReadOnlyList<string> Tools = new[]
        {
            "rfem_otwarty", "rfem_otwarty_pret", "rfem_otwarty_przekroje", "rfem_otwarty_przypadki", "rfem_otwarty_sily", "rfem_otwarty_reakcje", "rfem_wskaz",
        };

        private const int AtIndex = 0;
        private const int AtNo = 1;
        private const int LoadCaseType = 1;
        private const int LoadCombinationType = 2;
        private const int ResultCombinationType = 3;
        private const int LocalMemberAxes = 2;
        private static readonly string[] Components = { "N", "Vy", "Vz", "Mt", "My", "Mz" };
        private static readonly string[] ReactionComponents = { "PX", "PY", "PZ", "MX", "MY", "MZ" };

        private readonly RfemOptions _options;

        /// <summary>Tworzy polecenia z ustawieniami wykonawcy (limity, czasy).</summary>
        public RfemLive(RfemOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        /// <summary>Zdanie o stanie RFEM 5 na tym komputerze dla modelu (do notatek sesji) — bez łączenia się przez COM.</summary>
        public static string Status()
        {
            if (!RfemComSession.IsInstalled())
            {
                return "RFEM 5 nie jest zainstalowany na tym komputerze: polecenia rfem_otwarty* i rfem_wskaz nie zadziałają, zostają pliki .rf5.";
            }

            var title = RfemComSession.RunningWindowTitle();
            return title == null
                ? "RFEM 5 nie jest teraz uruchomiony: polecenia rfem_otwarty* i rfem_wskaz zadziałają dopiero po otwarciu modelu w RFEM 5; pliki .rf5 czytają rfem_info, rfem_pret, rfem_sily."
                : "RFEM 5 jest uruchomiony (okno „" + title + "”): polecenia rfem_otwarty* czytają ten otwarty model, rfem_wskaz prosi o kliknięcie obiektu w oknie RFEM.";
        }

        /// <summary>Wykonuje polecenie; wynik zaczyna się od „OK” albo „BŁĄD”.</summary>
        public string Execute(ToolCall call, CancellationToken cancellationToken)
        {
            switch (call.Name)
            {
                case "rfem_otwarty": return Run(Summary, cancellationToken);
                case "rfem_otwarty_pret": return Run(s => MemberTool(s, call), cancellationToken);
                case "rfem_otwarty_przekroje": return Run(Sections, cancellationToken);
                case "rfem_otwarty_przypadki": return Run(Loadings, cancellationToken);
                case "rfem_otwarty_sily": return Run(s => Forces(s, call), cancellationToken);
                case "rfem_otwarty_reakcje": return Run(s => Reactions(s, call), cancellationToken);
                case "rfem_wskaz":
                    return Run(s => Pick(s, call), cancellationToken, _options.PickTimeout,
                        "okno wyboru w RFEM wciąż czeka na kliknięcie i OK (albo Anuluj); dopóki jest otwarte, licencja RF-COM jest zajęta.");
                default: return "BŁĄD: nieobsługiwane polecenie: " + call.Name;
            }
        }

        private string Run(Func<RfemComSession, string> work, CancellationToken cancellationToken, TimeSpan? timeout = null, string timeoutHint = null)
        {
            return RfemCom.RunSta(() =>
            {
                var problem = RfemComLayout.Problem();
                if (problem != null)
                {
                    return problem;
                }

                using (var session = RfemComSession.Connect())
                {
                    session.Lock();
                    return Truncate(work(session));
                }
            }, timeout ?? _options.LiveTimeout, cancellationToken, timeoutHint ?? "RFEM może czekać na okno dialogowe, sprawdź program; licencja RF-COM zostaje zajęta do końca wywołania.");
        }

        // ---------------------------------------------------------------- indeks modelu

        /// <summary>Tabele modelu przeczytane jednym przebiegiem (pręt po pręcie; 500 prętów ≈ 0,1 s).</summary>
        private sealed class Index
        {
            public readonly SortedDictionary<int, Member> Members = new SortedDictionary<int, Member>();
            public readonly SortedDictionary<int, CrossSection> Sections = new SortedDictionary<int, CrossSection>();
            public readonly SortedDictionary<int, Material> Materials = new SortedDictionary<int, Material>();
            public readonly Dictionary<int, Line> Lines = new Dictionary<int, Line>();
            public readonly Dictionary<int, Node> Nodes = new Dictionary<int, Node>();

            public CrossSection? SectionOf(int no) => no > 0 && Sections.TryGetValue(no, out var s) ? s : (CrossSection?)null;

            public Material? MaterialOf(int no) => no > 0 && Materials.TryGetValue(no, out var m) ? m : (Material?)null;

            public List<Member> MembersWithSection(int no) => Members.Values.Where(m => m.StartCrossSectionNo == no || m.EndCrossSectionNo == no).ToList();
        }

        private static Index Load(RfemComSession session, bool members = true)
        {
            var data = session.Data;
            var index = new Index();
            if (members)
            {
                var count = data.GetMemberCount();
                for (var i = 0; i < count; i++)
                {
                    var member = data.GetMember(i, AtIndex);
                    try
                    {
                        member.GetData(out var record);
                        index.Members[record.No] = record;
                    }
                    finally
                    {
                        RfemComSession.Release(member);
                    }
                }
            }

            var sections = data.GetCrossSectionCount();
            for (var i = 0; i < sections; i++)
            {
                var section = data.GetCrossSection(i, AtIndex);
                try
                {
                    section.GetData(out var record);
                    index.Sections[record.No] = record;
                }
                finally
                {
                    RfemComSession.Release(section);
                }
            }

            var materials = data.GetMaterialCount();
            for (var i = 0; i < materials; i++)
            {
                var material = data.GetMaterial(i, AtIndex);
                try
                {
                    material.GetData(out var record);
                    index.Materials[record.No] = record;
                }
                finally
                {
                    RfemComSession.Release(material);
                }
            }

            return index;
        }

        private static Line? LineOf(RfemComSession session, Index index, int no)
        {
            if (no <= 0)
            {
                return null;
            }

            if (index.Lines.TryGetValue(no, out var cached))
            {
                return cached;
            }

            ILine line;
            try
            {
                line = session.Data.GetLine(no, AtNo);
            }
            catch (Exception e) when (RfemCom.IsComFailure(e))
            {
                return null;
            }

            try
            {
                line.GetData(out var record);
                if (record.ControlPoints != IntPtr.Zero)
                {
                    Native.SafeArrayDestroy(record.ControlPoints);
                    record.ControlPoints = IntPtr.Zero;
                }

                index.Lines[no] = record;
                return record;
            }
            finally
            {
                RfemComSession.Release(line);
            }
        }

        private static Node? NodeOf(RfemComSession session, Index index, int no)
        {
            if (index.Nodes.TryGetValue(no, out var cached))
            {
                return cached;
            }

            INode node;
            try
            {
                node = session.Data.GetNode(no, AtNo);
            }
            catch (Exception e) when (RfemCom.IsComFailure(e))
            {
                return null;
            }

            try
            {
                node.GetData(out var record);
                index.Nodes[no] = record;
                return record;
            }
            finally
            {
                RfemComSession.Release(node);
            }
        }

        /// <summary>Numery węzłów linii pręta (lista „64,38” z RF-COM).</summary>
        private static List<int> MemberNodes(RfemComSession session, Index index, Member member)
        {
            var line = LineOf(session, index, member.LineNo);
            return line.HasValue ? ParseList(line.Value.NodeList) : new List<int>();
        }

        /// <summary>Pręt o numerze użytkownika albo komunikat BŁĄD z zakresem numerów.</summary>
        private static Member? FindMember(RfemComSession session, int no, out string problem)
        {
            problem = null;
            if (no <= 0)
            {
                problem = "BŁĄD: podaj numer pręta (numer).";
                return null;
            }

            IMember member;
            try
            {
                member = session.Data.GetMember(no, AtNo);
            }
            catch (Exception e) when (RfemCom.IsComFailure(e))
            {
                var index = Load(session);
                if (index.Members.Count == 0)
                {
                    problem = "BŁĄD: otwarty model nie ma prętów.";
                    return null;
                }

                var nearest = index.Members.Keys.OrderBy(k => Math.Abs(k - no)).Take(5).OrderBy(k => k).ToList();
                problem = "BŁĄD: w otwartym modelu nie ma pręta " + no + ". Pręty mają numery " + index.Members.Keys.First() + "–" + index.Members.Keys.Last() + " (jest ich " + index.Members.Count + "); najbliższe: "
                    + string.Join(", ", nearest) + ". Wskazanie kliknięciem: rfem_wskaz.";
                return null;
            }

            try
            {
                member.GetData(out var record);
                return record;
            }
            finally
            {
                RfemComSession.Release(member);
            }
        }

        // ---------------------------------------------------------------- polecenia

        private string Summary(RfemComSession session)
        {
            var model = session.Model;
            var data = session.Data;
            var text = new StringBuilder("OK RFEM ").Append(Safe(() => session.App.GetVersion(), "5")).Append(", otwarty model „").Append(Safe(() => model.GetName(), "?")).Append('”');
            var path = Safe(() => model.GetPath(), "");
            var project = Safe(() => model.GetProjectName(), "");
            text.Append(path.Length > 0 ? " (" + path + ")" : " (jeszcze nie zapisany)").Append(project.Length > 0 ? ", projekt „" + project + "”" : "").Append('.');

            var index = Load(session);
            var byType = index.Members.Values.GroupBy(m => m.Type).OrderByDescending(g => g.Count()).Select(g => TypeName(g.Key) + " " + g.Count()).ToList();
            text.Append("\nModel: ").Append(data.GetNodeCount()).Append(" węzłów, ").Append(data.GetLineCount()).Append(" linii, ").Append(index.Members.Count).Append(" prętów")
                .Append(byType.Count > 0 ? " (" + string.Join(", ", byType) + ")" : "").Append(", ").Append(index.Sections.Count).Append(" przekrojów, ").Append(index.Materials.Count).Append(" materiałów, ")
                .Append(data.GetSurfaceCount()).Append(" powierzchni, ").Append(data.GetSolidCount()).Append(" brył, ").Append(data.GetNodalSupportCount()).Append(" podpór węzłowych, ")
                .Append(data.GetSetOfMembersCount()).Append(" zbiorów prętów.");
            if (index.Members.Count > 0)
            {
                text.Append(" Łączna długość prętów ").Append(Num(index.Members.Values.Sum(m => m.Length), 1)).Append(" m; numery prętów ").Append(index.Members.Keys.First()).Append('–').Append(index.Members.Keys.Last()).Append('.');
            }

            if (index.Sections.Count > 0)
            {
                text.Append("\nPrzekroje: ").Append(string.Join("; ", index.Sections.Values.Take(_options.MaxListed).Select(s =>
                {
                    var members = index.MembersWithSection(s.No);
                    var material = index.MaterialOf(s.MaterialNo);
                    return s.No + " " + s.Description + (material.HasValue ? " (" + material.Value.Description + ")" : "") + " – " + RfemExecutor.Count(members.Count, "pręt", "pręty", "prętów")
                        + (members.Count > 0 ? ", " + Num(members.Sum(m => m.Length), 1) + " m" : "");
                })));
                if (index.Sections.Count > _options.MaxListed)
                {
                    text.Append("; … (rfem_otwarty_przekroje pokaże wszystkie)");
                }
            }

            if (index.Materials.Count > 0)
            {
                text.Append("\nMateriały: ").Append(string.Join("; ", index.Materials.Values.Select(m => m.No + " " + m.Description + (m.ElasticityModulus > 0 ? " (E = " + Num(m.ElasticityModulus / 1e9, 0) + " GPa)" : ""))));
            }

            var loads = session.Loads;
            var cases = loads.GetLoadCaseCount();
            var combinations = loads.GetLoadCombinationCount();
            var resultCombinations = loads.GetResultCombinationCount();
            text.Append("\nObciążenia: ").Append(cases).Append(" PO, ").Append(combinations).Append(" KO, ").Append(resultCombinations).Append(" KW — lista: rfem_otwarty_przypadki.");
            if (loads.HasAnyResults())
            {
                var solved = CombinationsWithResults(session);
                text.Append("\nWyniki: są (kombinacje z wynikami: ").Append(solved.Count).Append(" z ").Append(combinations).Append(solved.Count > 0 ? ", np. KO" + solved[0] : "").Append("). Siły w pręcie: rfem_otwarty_sily (numer, obciazenie=KO")
                    .Append(solved.Count > 0 ? solved[0].ToString(CultureInfo.InvariantCulture) : "1").Append("); reakcje: rfem_otwarty_reakcje (wezel).");
            }
            else
            {
                text.Append("\nWyniki: brak (model nie jest policzony).");
            }

            text.Append("\nPręt wskazany kliknięciem w RFEM: rfem_wskaz.");
            return text.ToString();
        }

        private string MemberTool(RfemComSession session, ToolCall call)
        {
            var no = (int)(call.GetNumber("numer") ?? 0);
            var member = FindMember(session, no, out var problem);
            if (problem != null)
            {
                return problem;
            }

            var index = Load(session, members: false);
            var text = new StringBuilder("OK ").Append(Describe(session, index, member.Value, true));
            text.Append('\n').Append(session.Loads.HasAnyResults()
                ? "Wyniki: model policzony — siły: rfem_otwarty_sily (numer=" + no + "), od jednej kombinacji z obciazenie=KO5, ugięcia z co=przemieszczenia."
                : "Wyniki: brak (model nie jest policzony).");
            return text.ToString();
        }

        /// <summary>Opis pręta: typ, linia, węzły, długość, obrót, przeguby, przekrój z materiałem (w jednym albo kilku wierszach).</summary>
        private string Describe(RfemComSession session, Index index, Member member, bool full)
        {
            var nodes = MemberNodes(session, index, member);
            var text = new StringBuilder("pręt ").Append(member.No).Append(full ? " (otwarty RFEM, model „" + Safe(() => session.Model.GetName(), "?") + "”): " : ": ").Append(TypeName(member.Type));
            if (!full)
            {
                var section = index.SectionOf(member.StartCrossSectionNo);
                return text.Append(", ").Append(section.HasValue ? section.Value.Description : member.Type == 2 ? "bez przekroju" : "przekrój " + member.StartCrossSectionNo)
                    .Append(", L = ").Append(Num(member.Length, 3)).Append(" m, węzły ").Append(string.Join(" → ", nodes)).Append(member.Comment?.Length > 0 ? " („" + member.Comment + "”)" : "").ToString();
            }

            text.Append(", linia ").Append(member.LineNo).Append(", węzły ").Append(nodes.Count > 0 ? string.Join(" → ", nodes) : "?").Append(", długość ").Append(Num(member.Length, 3)).Append(" m");
            if (member.Rotation.Type == 3)
            {
                text.Append(", obrót przez węzeł pomocniczy ").Append(member.Rotation.HelpNodeNo);
            }
            else
            {
                text.Append(", obrót ").Append(Num(member.Rotation.Angle * 180 / Math.PI, 1)).Append('°');
            }

            if (member.StartHingeNo > 0 || member.EndHingeNo > 0)
            {
                text.Append(", przegub ").Append(member.StartHingeNo > 0 ? "nr " + member.StartHingeNo + " na początku" : "").Append(member.StartHingeNo > 0 && member.EndHingeNo > 0 ? " i " : "").Append(member.EndHingeNo > 0 ? "nr " + member.EndHingeNo + " na końcu" : "");
            }

            if (member.EccentricityNo > 0)
            {
                text.Append(", mimośród nr ").Append(member.EccentricityNo);
            }

            text.Append(member.Comment?.Length > 0 ? ", komentarz „" + member.Comment + "”" : "").Append('.');
            text.Append("\nWęzły [m]: ").Append(string.Join("; ", nodes.Select(n =>
            {
                var node = NodeOf(session, index, n);
                return node.HasValue ? n + " (" + Num(node.Value.X, 3) + "; " + Num(node.Value.Y, 3) + "; " + Num(node.Value.Z, 3) + ")" : n + " (?)";
            })));

            var start = index.SectionOf(member.StartCrossSectionNo);
            if (start.HasValue)
            {
                text.Append('\n').Append(SectionText(index, start.Value));
                if (member.EndCrossSectionNo > 0 && member.EndCrossSectionNo != member.StartCrossSectionNo)
                {
                    var end = index.SectionOf(member.EndCrossSectionNo);
                    text.Append("\nPrzekrój na końcu (zbieżny): ").Append(end.HasValue ? end.Value.No + " " + end.Value.Description : member.EndCrossSectionNo.ToString(CultureInfo.InvariantCulture));
                }
            }
            else
            {
                text.Append(member.StartCrossSectionNo > 0 ? "\nPrzekrój " + member.StartCrossSectionNo + " (brak w tabeli)." : member.Type == 2 ? "\nBez przekroju (pręt sztywny)." : "\nBez przekroju.");
            }

            return text.ToString();
        }

        private static string SectionText(Index index, CrossSection section)
        {
            var material = index.MaterialOf(section.MaterialNo);
            var text = new StringBuilder("Przekrój ").Append(section.No).Append(": ").Append(section.Description)
                .Append(section.TextID?.Length > 0 && section.TextID != section.Description ? " (" + section.TextID + ")" : "")
                .Append(material.HasValue ? ", materiał " + material.Value.No + " " + material.Value.Description + (material.Value.ElasticityModulus > 0 ? " (E = " + Num(material.Value.ElasticityModulus / 1e9, 0) + " GPa)" : "") : ", materiał " + section.MaterialNo);
            if (section.AxialArea > 0)
            {
                text.Append("; A = ").Append(Num(section.AxialArea * 1e4, 2)).Append(" cm², Iy = ").Append(Num(section.BendingMomentY * 1e8, 1)).Append(" cm⁴, Iz = ").Append(Num(section.BendingMomentZ * 1e8, 1))
                    .Append(" cm⁴, It = ").Append(Num(section.TorsionMoment * 1e8, 2)).Append(" cm⁴");
            }

            return text.ToString();
        }

        private string Sections(RfemComSession session)
        {
            var index = Load(session);
            if (index.Sections.Count == 0)
            {
                return "BŁĄD: otwarty model nie ma przekrojów" + (index.Members.Count > 0 ? " (pręty są: " + index.Members.Count + ")." : ".");
            }

            var text = new StringBuilder("OK przekroje (").Append(index.Sections.Count).Append(") w otwartym RFEM („").Append(Safe(() => session.Model.GetName(), "?")).Append("”); A [cm²], Iy, Iz, It [cm⁴]:");
            foreach (var section in index.Sections.Values)
            {
                var members = index.MembersWithSection(section.No);
                var material = index.MaterialOf(section.MaterialNo);
                text.Append('\n').Append(section.No).Append(" | ").Append(section.Description).Append(section.TextID?.Length > 0 && section.TextID != section.Description ? " (" + section.TextID + ")" : "")
                    .Append(" | ").Append(material.HasValue ? material.Value.Description : "materiał " + section.MaterialNo)
                    .Append(" | A = ").Append(Num(section.AxialArea * 1e4, 2)).Append(" | Iy = ").Append(Num(section.BendingMomentY * 1e8, 1)).Append(" | Iz = ").Append(Num(section.BendingMomentZ * 1e8, 1)).Append(" | It = ").Append(Num(section.TorsionMoment * 1e8, 2))
                    .Append(" | ").Append(RfemExecutor.Count(members.Count, "pręt", "pręty", "prętów"))
                    .Append(members.Count > 0 ? " (" + string.Join(", ", members.Select(m => m.No).Take(15)) + (members.Count > 15 ? ", …" : "") + "), razem " + Num(members.Sum(m => m.Length), 2) + " m" : "");
            }

            if (index.Materials.Count > 0)
            {
                text.Append("\nMateriały: ").Append(string.Join("; ", index.Materials.Values.Select(MaterialText)));
            }

            return text.ToString();
        }

        private static string MaterialText(Material material)
        {
            var details = new List<string>();
            if (material.ElasticityModulus > 0)
            {
                details.Add("E = " + Num(material.ElasticityModulus / 1e9, 1) + " GPa");
            }

            if (material.ShearModulus > 0)
            {
                details.Add("G = " + Num(material.ShearModulus / 1e9, 1) + " GPa");
            }

            if (material.SpecificWeight > 0)
            {
                details.Add("γ = " + Num(material.SpecificWeight / 1e3, 1) + " kN/m³");
            }

            if (material.UserDefined)
            {
                details.Add("użytkownika");
            }

            return material.No + " " + material.Description + (details.Count > 0 ? " (" + string.Join(", ", details) + ")" : "");
        }

        private string Loadings(RfemComSession session)
        {
            var loads = session.Loads;
            var cases = loads.GetLoadCaseCount();
            var combinations = loads.GetLoadCombinationCount();
            var resultCombinations = loads.GetResultCombinationCount();
            if (cases == 0 && combinations == 0)
            {
                return "BŁĄD: otwarty model nie ma przypadków ani kombinacji obciążeń.";
            }

            var text = new StringBuilder("OK przypadki obciążeń (").Append(cases).Append(") w otwartym RFEM:");
            for (var i = 0; i < cases; i++)
            {
                var loadCase = loads.GetLoadCase(i, AtIndex);
                try
                {
                    loadCase.GetData(out var record);
                    text.Append("\nPO").Append(record.Loading.No).Append(' ').Append(record.Description).Append(record.ToSolve ? "" : " (nie do liczenia)").Append(Safe(loadCase.HasResults) ? " [wyniki]" : "");
                }
                finally
                {
                    RfemComSession.Release(loadCase);
                }
            }

            if (combinations > 0)
            {
                text.Append("\nKombinacje obciążeń (").Append(combinations).Append("), PO = przypadek:");
                for (var i = 0; i < Math.Min(combinations, _options.MaxListed); i++)
                {
                    var combination = loads.GetLoadCombination(i, AtIndex);
                    try
                    {
                        combination.GetData(out var record);
                        text.Append("\nKO").Append(record.Loading.No).Append(record.Description?.Length > 0 ? " " + record.Description : "").Append(": ").Append(Translate(record.Definition)).Append(Safe(combination.HasResults) ? " [wyniki]" : "");
                    }
                    finally
                    {
                        RfemComSession.Release(combination);
                    }
                }

                if (combinations > _options.MaxListed)
                {
                    text.Append("\n… i ").Append(combinations - _options.MaxListed).Append(" dalszych");
                }
            }

            if (resultCombinations > 0)
            {
                text.Append("\nKombinacje wyników (").Append(resultCombinations).Append("), KO = kombinacja, „lub” = obwiednia:");
                for (var i = 0; i < Math.Min(resultCombinations, 20); i++)
                {
                    var combination = loads.GetResultCombination(i, AtIndex);
                    try
                    {
                        combination.GetData(out var record);
                        text.Append("\nKW").Append(record.Loading.No).Append(record.Description?.Length > 0 ? " " + record.Description : "").Append(": ").Append(Translate(record.Definition)).Append(Safe(combination.HasResults) ? " [wyniki]" : "");
                    }
                    finally
                    {
                        RfemComSession.Release(combination);
                    }
                }
            }

            text.Append(loads.HasAnyResults() ? "\nSiły w pręcie: rfem_otwarty_sily (numer, obciazenie=KO5)." : "\nWyników nie ma (model nie jest policzony).");
            return text.ToString();
        }

        private string Forces(RfemComSession session, ToolCall call)
        {
            var no = (int)(call.GetNumber("numer") ?? 0);
            var member = FindMember(session, no, out var problem);
            if (problem != null)
            {
                return problem;
            }

            if (!session.Loads.HasAnyResults())
            {
                return "BŁĄD: otwarty model nie ma wyników — policz go w RFEM i spytaj ponownie. Geometrię i przekrój pręta pokaże rfem_otwarty_pret.";
            }

            var what = (call.GetString("co") ?? "").Trim().ToLowerInvariant();
            var displacements = what.StartsWith("przem") || what.StartsWith("ugi") || what.StartsWith("deform");
            var loadingText = call.GetString("obciazenie");
            if (string.IsNullOrWhiteSpace(loadingText))
            {
                return displacements ? DisplacementExtremes(session, no) : Extremes(session, no);
            }

            if (!TryParseLoading(loadingText, out var loading, out problem))
            {
                return problem;
            }

            var results = ResultsFor(session, loading, out var label, out problem);
            if (problem != null)
            {
                return problem;
            }

            var text = new StringBuilder("OK pręt ").Append(no).Append(", ").Append(label).Append(" (otwarty RFEM)");
            if (displacements)
            {
                var points = Native.ReadRecords<MemberDeformations>(Call(() => { results.GetMemberDeformations(no, AtNo, LocalMemberAxes, out var psa); return psa; }));
                if (points.Length == 0)
                {
                    return "BŁĄD: pręt " + no + " nie ma przemieszczeń dla " + label + " (pręt sztywny albo wyłączony z obliczeń).";
                }

                text.Append(", przemieszczenia w osiach pręta [mm], x od początku pręta [m]:\nx | ux | uy | uz | wypadkowe");
                foreach (var point in points)
                {
                    text.Append('\n').Append(Num(point.Location, 3)).Append(" | ").Append(Num(point.Displacements.X * 1e3, 2)).Append(" | ").Append(Num(point.Displacements.Y * 1e3, 2)).Append(" | ").Append(Num(point.Displacements.Z * 1e3, 2))
                        .Append(" | ").Append(Num(point.TotalDisplacement * 1e3, 2));
                }

                var max = points.OrderByDescending(p => p.TotalDisplacement).First();
                text.Append("\nNajwiększe przemieszczenie wypadkowe: ").Append(Num(max.TotalDisplacement * 1e3, 2)).Append(" mm (x = ").Append(Num(max.Location, 3)).Append(" m).");
                return text.ToString();
            }

            var forces = Dedupe(Native.ReadRecords<MemberForces>(Call(() => { results.GetMemberInternalForces(no, AtNo, true, out var psa); return psa; })));
            if (forces.Count == 0)
            {
                return "BŁĄD: pręt " + no + " nie ma sił dla " + label + " (pręt sztywny albo wyłączony z obliczeń).";
            }

            var envelope = loading.Type == ResultCombinationType;
            text.Append(", siły [kN], momenty [kNm], x od początku pręta [m]:\nx | N | Vy | Vz | Mt | My | Mz").Append(envelope ? " | od" : "");
            foreach (var point in forces)
            {
                text.Append('\n').Append(Num(point.Location, 3));
                for (var c = 0; c < Components.Length; c++)
                {
                    text.Append(" | ").Append(Num(Component(point, c) / 1e3, 2));
                }

                if (envelope)
                {
                    text.Append(" | ").Append(Translate(point.CorrespondingLoading));
                }
            }

            text.Append("\nEkstrema: ").Append(string.Join("; ", Components.Select((name, c) =>
            {
                var min = forces.OrderBy(p => Component(p, c)).First();
                var max = forces.OrderByDescending(p => Component(p, c)).First();
                return name + " od " + Num(Component(min, c) / 1e3, 2) + " (x = " + Num(min.Location, 3) + ") do " + Num(Component(max, c) / 1e3, 2) + " (x = " + Num(max.Location, 3) + ")";
            }))).Append(". N ujemne = ściskanie.");
            return text.ToString();
        }

        /// <summary>Ekstrema sił pręta ze wszystkich kombinacji z wynikami (gdy żadna nie ma wyników — z przypadków).</summary>
        private string Extremes(RfemComSession session, int no)
        {
            var best = new Dictionary<int, Tuple<double, string, double>>();
            var worst = new Dictionary<int, Tuple<double, string, double>>();
            var scanned = 0;
            var failures = 0;
            var skipped = 0;
            foreach (var loading in LoadingsWithResults(session))
            {
                if (scanned >= _options.MaxLoadingsScanned)
                {
                    skipped++;
                    continue;
                }

                var results = Safe(() => session.Calculation.GetResultsInFeNodes(loading.Item1.Type, loading.Item1.No), null);
                if (results == null)
                {
                    failures++;
                    continue;
                }

                try
                {
                    MemberForces[] records;
                    try
                    {
                        records = Native.ReadRecords<MemberForces>(Call(() => { results.GetMemberExtremeInternalForces(no, AtNo, true, out var psa); return psa; }));
                    }
                    catch (Exception e) when (RfemCom.IsComFailure(e))
                    {
                        failures++;
                        continue;
                    }

                    scanned++;
                    foreach (var record in records)
                    {
                        for (var c = 0; c < Components.Length; c++)
                        {
                            var value = Component(record, c);
                            if (!best.TryGetValue(c, out var max) || value > max.Item1)
                            {
                                best[c] = Tuple.Create(value, loading.Item2, record.Location);
                            }

                            if (!worst.TryGetValue(c, out var min) || value < min.Item1)
                            {
                                worst[c] = Tuple.Create(value, loading.Item2, record.Location);
                            }
                        }
                    }
                }
                finally
                {
                    RfemComSession.Release(results);
                }
            }

            if (best.Count == 0)
            {
                return "BŁĄD: nie udało się odczytać sił pręta " + no + " z żadnej kombinacji ani przypadku (pręt sztywny albo wyłączony z obliczeń?).";
            }

            var text = new StringBuilder("OK pręt ").Append(no).Append(": ekstrema z ").Append(scanned).Append(" obciążeń z wynikami w otwartym RFEM [kN, kNm], N ujemne = ściskanie:");
            for (var c = 0; c < Components.Length; c++)
            {
                var min = worst[c];
                var max = best[c];
                text.Append('\n').Append(Components[c]).Append(": min ").Append(Num(min.Item1 / 1e3, 2)).Append(" (").Append(min.Item2).Append(", x = ").Append(Num(min.Item3, 3)).Append(")")
                    .Append(", max ").Append(Num(max.Item1 / 1e3, 2)).Append(" (").Append(max.Item2).Append(", x = ").Append(Num(max.Item3, 3)).Append(')');
            }

            text.Append("\nPrzebieg dla jednego obciążenia: rfem_otwarty_sily z obciazenie=").Append(worst[0].Item2).Append('.');
            if (skipped > 0)
            {
                text.Append("\nPominięto ").Append(skipped).Append(" obciążeń ponad limit ").Append(_options.MaxLoadingsScanned).Append(" — zawęź przez obciazenie (np. kombinacja wyników KW z obwiednią).");
            }

            if (failures > 0)
            {
                text.Append("\nNie odczytano ").Append(failures).Append(" obciążeń.");
            }

            return text.ToString();
        }

        private string DisplacementExtremes(RfemComSession session, int no)
        {
            Tuple<double, string, double> best = null;
            var scanned = 0;
            foreach (var loading in LoadingsWithResults(session))
            {
                if (scanned >= _options.MaxLoadingsScanned)
                {
                    break;
                }

                var results = Safe(() => session.Calculation.GetResultsInFeNodes(loading.Item1.Type, loading.Item1.No), null);
                if (results == null)
                {
                    continue;
                }

                try
                {
                    MemberDeformations[] records;
                    try
                    {
                        records = Native.ReadRecords<MemberDeformations>(Call(() => { results.GetMemberExtremeDeformations(no, AtNo, LocalMemberAxes, out var psa); return psa; }));
                    }
                    catch (Exception e) when (RfemCom.IsComFailure(e))
                    {
                        continue;
                    }

                    scanned++;
                    foreach (var record in records)
                    {
                        if (best == null || record.TotalDisplacement > best.Item1)
                        {
                            best = Tuple.Create(record.TotalDisplacement, loading.Item2, record.Location);
                        }
                    }
                }
                finally
                {
                    RfemComSession.Release(results);
                }
            }

            return best == null
                ? "BŁĄD: nie udało się odczytać przemieszczeń pręta " + no + " z żadnego obciążenia."
                : "OK pręt " + no + ": największe przemieszczenie wypadkowe " + Num(best.Item1 * 1e3, 2) + " mm (" + best.Item2 + ", x = " + Num(best.Item3, 3) + " m) z " + scanned + " obciążeń z wynikami w otwartym RFEM. Przebieg: rfem_otwarty_sily z co=przemieszczenia i obciazenie=" + best.Item2 + ".";
        }

        private string Reactions(RfemComSession session, ToolCall call)
        {
            var node = (int)(call.GetNumber("wezel") ?? 0);
            if (node <= 0)
            {
                return "BŁĄD: podaj numer węzła podporowego (wezel).";
            }

            if (!session.Loads.HasAnyResults())
            {
                return "BŁĄD: otwarty model nie ma wyników — policz go w RFEM i spytaj ponownie.";
            }

            var loadingText = call.GetString("obciazenie");
            if (!string.IsNullOrWhiteSpace(loadingText))
            {
                if (!TryParseLoading(loadingText, out var loading, out var problem))
                {
                    return problem;
                }

                var results = ResultsFor(session, loading, out var label, out problem);
                if (problem != null)
                {
                    return problem;
                }

                NodalSupportForces[] records;
                try
                {
                    records = Native.ReadRecords<NodalSupportForces>(Call(() => { results.GetNodalSupportForces(node, AtNo, false, out var psa); return psa; }));
                }
                catch (Exception e) when (RfemCom.IsComFailure(e))
                {
                    return "BŁĄD: RFEM nie oddał reakcji węzła " + node + " dla " + label + " (" + RfemCom.Message(e) + ") — węzeł nie jest podparty albo nie ma takiego węzła.";
                }

                if (records.Length == 0)
                {
                    return "BŁĄD: węzeł " + node + " nie ma reakcji dla " + label + " (nie jest podparty?).";
                }

                var text = new StringBuilder("OK węzeł ").Append(node).Append(", ").Append(label).Append(" (otwarty RFEM), reakcje w osiach globalnych [kN, kNm]:");
                foreach (var record in records)
                {
                    text.Append('\n').Append(string.Join(", ", ReactionComponents.Select((name, c) => name + " = " + Num(Reaction(record, c) / 1e3, 2))))
                        .Append(record.CorrespondingLoading?.Length > 0 ? " (od " + Translate(record.CorrespondingLoading) + ")" : "");
                }

                return text.ToString();
            }

            var best = new Dictionary<int, Tuple<double, string>>();
            var worst = new Dictionary<int, Tuple<double, string>>();
            var scanned = 0;
            foreach (var loading in LoadingsWithResults(session))
            {
                if (scanned >= _options.MaxLoadingsScanned)
                {
                    break;
                }

                var results = Safe(() => session.Calculation.GetResultsInFeNodes(loading.Item1.Type, loading.Item1.No), null);
                if (results == null)
                {
                    continue;
                }

                try
                {
                    NodalSupportForces[] records;
                    try
                    {
                        records = Native.ReadRecords<NodalSupportForces>(Call(() => { results.GetNodalExtremeSupportForces(node, AtNo, false, out var psa); return psa; }));
                    }
                    catch (Exception e) when (RfemCom.IsComFailure(e))
                    {
                        break;  // węzeł nie jest podparty — dla innych obciążeń będzie tak samo
                    }

                    scanned++;
                    foreach (var record in records)
                    {
                        for (var c = 0; c < ReactionComponents.Length; c++)
                        {
                            var value = Reaction(record, c);
                            if (!best.TryGetValue(c, out var max) || value > max.Item1)
                            {
                                best[c] = Tuple.Create(value, loading.Item2);
                            }

                            if (!worst.TryGetValue(c, out var min) || value < min.Item1)
                            {
                                worst[c] = Tuple.Create(value, loading.Item2);
                            }
                        }
                    }
                }
                finally
                {
                    RfemComSession.Release(results);
                }
            }

            if (best.Count == 0)
            {
                return "BŁĄD: węzeł " + node + " nie ma reakcji w żadnym obciążeniu (nie jest podparty albo nie ma takiego węzła). Podpory węzłowe pokaże RFEM.";
            }

            var summary = new StringBuilder("OK węzeł ").Append(node).Append(": ekstrema reakcji z ").Append(scanned).Append(" obciążeń z wynikami (otwarty RFEM, osie globalne) [kN, kNm]:");
            for (var c = 0; c < ReactionComponents.Length; c++)
            {
                summary.Append('\n').Append(ReactionComponents[c]).Append(": min ").Append(Num(worst[c].Item1 / 1e3, 2)).Append(" (").Append(worst[c].Item2).Append("), max ").Append(Num(best[c].Item1 / 1e3, 2)).Append(" (").Append(best[c].Item2).Append(')');
            }

            return summary.ToString();
        }

        private string Pick(RfemComSession session, ToolCall call)
        {
            var what = (call.GetString("co") ?? "").Trim().ToLowerInvariant();
            var kind = what.StartsWith("węz") || what.StartsWith("wez") || what.StartsWith("node") ? 0 : what.StartsWith("lini") || what.StartsWith("line") ? 1 : 5;
            var view = session.Track(session.Model.GetActiveView());
            var picked = view.PickObjects(kind);
            var numbers = ParseList(picked);
            var noun = kind == 0 ? "węzeł" : kind == 1 ? "linię" : "pręt";
            if (numbers.Count == 0)
            {
                return "BŁĄD: nic nie wskazano — okno wyboru w RFEM zamknięto bez OK albo bez zaznaczenia. Powtórz rfem_wskaz, kliknij " + noun + " (można kilka) w oknie RFEM i potwierdź OK.";
            }

            var index = Load(session, members: false);
            var text = new StringBuilder("OK wskazane ").Append(kind == 0 ? "węzły" : kind == 1 ? "linie" : "pręty").Append(" (").Append(numbers.Count).Append("): ").Append(string.Join(", ", numbers.Take(40)));
            if (kind == 0)
            {
                foreach (var no in numbers.Take(_options.MaxListed))
                {
                    var node = NodeOf(session, index, no);
                    text.Append('\n').Append(node.HasValue ? "węzeł " + no + ": X = " + Num(node.Value.X, 3) + " m, Y = " + Num(node.Value.Y, 3) + " m, Z = " + Num(node.Value.Z, 3) + " m" : "węzeł " + no + ": nie odczytano");
                }

                return text.ToString();
            }

            if (kind == 1)
            {
                foreach (var no in numbers.Take(_options.MaxListed))
                {
                    var line = LineOf(session, index, no);
                    text.Append('\n').Append(line.HasValue ? "linia " + no + ": węzły " + line.Value.NodeList + ", długość " + Num(line.Value.Length, 3) + " m" : "linia " + no + ": nie odczytano");
                }

                return text.ToString();
            }

            if (numbers.Count == 1)
            {
                var member = FindMember(session, numbers[0], out var problem);
                return problem ?? text.Append('\n').Append(Describe(session, index, member.Value, true)).ToString();
            }

            foreach (var no in numbers.Take(_options.MaxListed))
            {
                var member = FindMember(session, no, out var problem);
                text.Append('\n').Append(problem != null ? "pręt " + no + ": nie odczytano" : Describe(session, index, member.Value, false));
            }

            if (numbers.Count > _options.MaxListed)
            {
                text.Append("\n… i ").Append(numbers.Count - _options.MaxListed).Append(" dalszych.");
            }

            return text.ToString();
        }

        // ---------------------------------------------------------------- obciążenia i wyniki

        /// <summary>Numery kombinacji obciążeń, które mają wyniki.</summary>
        private static List<int> CombinationsWithResults(RfemComSession session)
        {
            var loads = session.Loads;
            var count = loads.GetLoadCombinationCount();
            var solved = new List<int>();
            for (var i = 0; i < count; i++)
            {
                var combination = loads.GetLoadCombination(i, AtIndex);
                try
                {
                    combination.GetData(out var record);
                    if (Safe(combination.HasResults))
                    {
                        solved.Add(record.Loading.No);
                    }
                }
                finally
                {
                    RfemComSession.Release(combination);
                }
            }

            return solved;
        }

        /// <summary>Obciążenia z wynikami do przeglądu ekstremów: kombinacje (KO), a gdy żadna nie ma wyników — przypadki (PO).</summary>
        private static List<Tuple<Loading, string>> LoadingsWithResults(RfemComSession session)
        {
            var list = CombinationsWithResults(session).Select(no => Tuple.Create(new Loading { Type = LoadCombinationType, No = no }, "KO" + no)).ToList();
            if (list.Count > 0)
            {
                return list;
            }

            var loads = session.Loads;
            var count = loads.GetLoadCaseCount();
            for (var i = 0; i < count; i++)
            {
                var loadCase = loads.GetLoadCase(i, AtIndex);
                try
                {
                    loadCase.GetData(out var record);
                    if (Safe(loadCase.HasResults))
                    {
                        list.Add(Tuple.Create(record.Loading, "PO" + record.Loading.No));
                    }
                }
                finally
                {
                    RfemComSession.Release(loadCase);
                }
            }

            return list;
        }

        /// <summary>Wyniki dla obciążenia (sprawdza, czy istnieje i ma wyniki) oraz jego etykieta, np. „KO5 (1.35*PO1 + 1.5*PO11)”.</summary>
        private static IResults ResultsFor(RfemComSession session, Loading loading, out string label, out string problem)
        {
            problem = null;
            label = Label(loading);
            var loads = session.Loads;
            bool hasResults;
            try
            {
                switch (loading.Type)
                {
                    case LoadCaseType:
                        {
                            var loadCase = session.Track(loads.GetLoadCase(loading.No, AtNo));
                            loadCase.GetData(out var record);
                            label += record.Description?.Length > 0 ? " (" + record.Description + ")" : "";
                            hasResults = loadCase.HasResults();
                            break;
                        }

                    case LoadCombinationType:
                        {
                            var combination = session.Track(loads.GetLoadCombination(loading.No, AtNo));
                            combination.GetData(out var record);
                            label += record.Definition?.Length > 0 ? " (" + Translate(record.Definition) + ")" : "";
                            hasResults = combination.HasResults();
                            break;
                        }

                    default:
                        {
                            var combination = session.Track(loads.GetResultCombination(loading.No, AtNo));
                            combination.GetData(out var record);
                            label += record.Description?.Length > 0 ? " (" + record.Description + ")" : "";
                            hasResults = combination.HasResults();
                            break;
                        }
                }
            }
            catch (Exception e) when (RfemCom.IsComFailure(e))
            {
                var available = loading.Type == LoadCaseType ? loads.GetLoadCaseCount() : loading.Type == LoadCombinationType ? loads.GetLoadCombinationCount() : loads.GetResultCombinationCount();
                problem = "BŁĄD: w otwartym modelu nie ma obciążenia " + label + " (" + (loading.Type == LoadCaseType ? "przypadków" : loading.Type == LoadCombinationType ? "kombinacji obciążeń" : "kombinacji wyników") + " jest " + available + "; lista: rfem_otwarty_przypadki).";
                return null;
            }

            if (!hasResults)
            {
                var solved = CombinationsWithResults(session);
                problem = "BŁĄD: " + label + " nie ma wyników" + (solved.Count > 0 ? "; kombinacje z wynikami: " + string.Join(", ", solved.Take(12).Select(n => "KO" + n)) + (solved.Count > 12 ? ", … (" + solved.Count + ")" : "") + "." : " — model nie jest policzony dla tego obciążenia.");
                return null;
            }

            try
            {
                return session.Track(session.Calculation.GetResultsInFeNodes(loading.Type, loading.No));
            }
            catch (Exception e) when (RfemCom.IsComFailure(e))
            {
                problem = "BŁĄD: RFEM nie oddał wyników dla " + label + " (" + RfemCom.Message(e) + ").";
                return null;
            }
        }

        /// <summary>„KO5”, „CO5”, „PO1”, „LC1”, „KW2”, „RC2”, sama liczba = KO.</summary>
        internal static bool TryParseLoading(string text, out Loading loading, out string problem)
        {
            loading = default;
            problem = null;
            var match = Regex.Match((text ?? "").Trim(), @"^(PO|LC|KO|CO|KW|RC|SK|SC)?\s*-?\s*(\d+)$", RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                problem = "BŁĄD: nie rozumiem obciążenia „" + text + "”. Podaj KO5 (kombinacja), PO1 (przypadek) albo KW1 (kombinacja wyników); lista: rfem_otwarty_przypadki.";
                return false;
            }

            var prefix = match.Groups[1].Value.ToUpperInvariant();
            loading = new Loading
            {
                Type = prefix == "PO" || prefix == "LC" ? LoadCaseType : prefix == "KW" || prefix == "RC" ? ResultCombinationType : prefix == "SK" || prefix == "SC" ? 4 : LoadCombinationType,
                No = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture),
            };
            return true;
        }

        /// <summary>Etykieta obciążenia po polsku: PO, KO, KW, SK.</summary>
        internal static string Label(Loading loading)
            => (loading.Type == LoadCaseType ? "PO" : loading.Type == LoadCombinationType ? "KO" : loading.Type == ResultCombinationType ? "KW" : loading.Type == 4 ? "SK" : "obciążenie typu " + loading.Type + " nr ") + loading.No;

        /// <summary>Wyrażenie RFEM po polsku: LC → PO, CO → KO, RC → KW, „or” → „lub”.</summary>
        internal static string Translate(string definition)
        {
            if (string.IsNullOrEmpty(definition))
            {
                return "";
            }

            var text = Regex.Replace(definition, @"\b(LC|CO|RC|SC)(\d+)", m =>
            {
                var prefix = m.Groups[1].Value;
                return (prefix == "LC" ? "PO" : prefix == "CO" ? "KO" : prefix == "RC" ? "KW" : "SK") + m.Groups[2].Value;
            });
            return Regex.Replace(Regex.Replace(text, @"\bor\b", "lub"), @"\band\b", "i");
        }

        /// <summary>Typ pręta po polsku (MemberType z RF-COM).</summary>
        internal static string TypeName(int type)
        {
            switch (type)
            {
                case 1: return "belka";
                case 2: return "pręt sztywny";
                case 3: return "żebro";
                case 4: return "kratownicowy";
                case 5: return "kratownicowy (tylko N)";
                case 6: return "cięgno";
                case 7: return "tylko ściskanie";
                case 8: return "wyboczeniowy";
                case 9: return "lina";
                case 10: return "lina na krążkach";
                case 11: return "belka wynikowa";
                case 12: return "o zadanej sztywności";
                case 13: return "sprzęgło sztywne-sztywne";
                case 14: return "sprzęgło sztywne-przegub";
                case 15: return "sprzęgło przegub-przegub";
                case 16: return "sprzęgło przegub-sztywne";
                case 17: return "sprężyna";
                case 18: return "pręt zerowy";
                default: return "typ " + type;
            }
        }

        /// <summary>Lista numerów z tekstu RFEM: „1,5,7”, „1-3;8”, „28 29”.</summary>
        internal static List<int> ParseList(string text)
        {
            var result = new List<int>();
            if (string.IsNullOrWhiteSpace(text))
            {
                return result;
            }

            foreach (var part in text.Split(new[] { ',', ';', ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var range = Regex.Match(part, @"^(\d+)\s*-\s*(\d+)$");
                if (range.Success)
                {
                    var from = int.Parse(range.Groups[1].Value, CultureInfo.InvariantCulture);
                    var to = int.Parse(range.Groups[2].Value, CultureInfo.InvariantCulture);
                    for (var i = from; i <= to && result.Count < 2000; i++)
                    {
                        result.Add(i);
                    }
                }
                else if (int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var no))
                {
                    result.Add(no);
                }
            }

            return result.Distinct().ToList();
        }

        private static double Component(MemberForces point, int index)
        {
            switch (index)
            {
                case 0: return point.Forces.X;
                case 1: return point.Forces.Y;
                case 2: return point.Forces.Z;
                case 3: return point.Moments.X;
                case 4: return point.Moments.Y;
                default: return point.Moments.Z;
            }
        }

        private static double Reaction(NodalSupportForces record, int index)
        {
            switch (index)
            {
                case 0: return record.Forces.X;
                case 1: return record.Forces.Y;
                case 2: return record.Forces.Z;
                case 3: return record.Moments.X;
                case 4: return record.Moments.Y;
                default: return record.Moments.Z;
            }
        }

        /// <summary>Usuwa powtórzone punkty (lewa i prawa strona węzła z tymi samymi wartościami); zostają skoki od sił skupionych.</summary>
        private static List<MemberForces> Dedupe(IEnumerable<MemberForces> points)
        {
            var result = new List<MemberForces>();
            foreach (var point in points.OrderBy(p => p.Location))
            {
                if (result.Count > 0)
                {
                    var last = result[result.Count - 1];
                    var same = Math.Abs(last.Location - point.Location) < 1e-6;
                    for (var c = 0; same && c < Components.Length; c++)
                    {
                        same = Math.Abs(Component(last, c) - Component(point, c)) < 0.5;
                    }

                    if (same)
                    {
                        continue;
                    }
                }

                result.Add(point);
            }

            return result;
        }

        private static IntPtr Call(Func<IntPtr> call) => call();

        private static string Safe(Func<string> read, string fallback)
        {
            try
            {
                return read() ?? fallback;
            }
            catch (Exception e) when (RfemCom.IsComFailure(e))
            {
                return fallback;
            }
        }

        private static T Safe<T>(Func<T> read, T fallback) where T : class
        {
            try
            {
                return read();
            }
            catch (Exception e) when (RfemCom.IsComFailure(e))
            {
                return fallback;
            }
        }

        private static bool Safe(Func<bool> read)
        {
            try
            {
                return read();
            }
            catch (Exception e) when (RfemCom.IsComFailure(e))
            {
                return false;
            }
        }

        private static string Num(double value, int decimals) => RfemExecutor.Num(value, decimals);

        private string Truncate(string text) => FileSystemExecutor.Truncate(text, _options.MaxOutputChars);
    }
}
