using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LokalneAI.Harness.Tools;

namespace LokalneAI.Harness.Rfem
{
    /// <summary>Ustawienia wykonawcy RFEM: te same dozwolone foldery co dla plików.</summary>
    public sealed class RfemOptions
    {
        /// <summary>Foldery, w których wolno czytać pliki .rf5 (pusta lista = nic nie wolno).</summary>
        public IList<string> AllowedRoots { get; set; } = new List<string>();

        /// <summary>Folder bieżący (ścieżki względne, szukanie jedynego pliku .rf5).</summary>
        public string WorkingFolder { get; set; }

        /// <summary>Najwięcej znaków w wyniku polecenia.</summary>
        public int MaxOutputChars { get; set; } = 6000;

        /// <summary>Najwięcej pozycji w listach (pręty, kombinacje).</summary>
        public int MaxListed { get; set; } = 60;
    }

    /// <summary>
    /// Polecenia umiejętności „rfem”: czytanie modeli RFEM 5 (.rf5) bez programu Dlubal — węzły, linie, pręty z długościami,
    /// przekroje i materiały, przypadki i kombinacje, a po obliczeniu także siły wewnętrzne w prętach z plików solvera.
    /// Wszystko tylko do odczytu, bez pytania o zgodę; ścieżki tylko w dozwolonych folderach. Pliki są czytane do pamięci
    /// (bez uchwytu) i trzymane, dopóki na dysku się nie zmienią.
    /// </summary>
    public sealed class RfemExecutor : IToolExecutor, IConfirmingExecutor, IDisposable
    {
        private readonly RfemOptions _options;
        private readonly FileSystemExecutor _files;
        private readonly Dictionary<string, Rf5Document> _cache = new Dictionary<string, Rf5Document>(StringComparer.OrdinalIgnoreCase);
        private readonly object _lock = new object();

        /// <summary>Nazwy poleceń RFEM (bez <c>szukaj_plikow</c> i <c>pokaz_zawartosc</c>).</summary>
        public static readonly IReadOnlyList<string> Tools = new[] { "rfem_info", "rfem_pret", "rfem_wezel", "rfem_przekroje", "rfem_przypadki", "rfem_sily", "rfem_szukaj_pretow" };

        /// <summary>Tworzy wykonawcę.</summary>
        public RfemExecutor(RfemOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _files = new FileSystemExecutor(new FileSystemOptions
            {
                AllowedRoots = _options.AllowedRoots,
                WorkingFolder = _options.WorkingFolder,
                MaxOutputChars = _options.MaxOutputChars,
            });
        }

        /// <inheritdoc/>
        public async Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken cancellationToken)
        {
            switch (call.Name)
            {
                case "szukaj_plikow":
                case "pokaz_zawartosc":
                    return await _files.ExecuteAsync(call, cancellationToken).ConfigureAwait(false);
            }

            try
            {
                var text = await Task.Run(() => Execute(call), cancellationToken).ConfigureAwait(false);
                return text.StartsWith("BŁĄD", StringComparison.Ordinal) ? ToolResult.Error(text) : ToolResult.Ok(text);
            }
            catch (RfemException e)
            {
                return ToolResult.Error(e.Message);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException || e is NotSupportedException
                                      || e is InvalidDataException || e is IndexOutOfRangeException || e is ArgumentOutOfRangeException)
            {
                return ToolResult.Error("BŁĄD: " + e.Message);
            }
        }

        /// <summary>Wszystko tylko czyta: bez pytania. Zła ścieżka albo nie plik .rf5 — odrzucenie z komunikatem.</summary>
        public ConfirmationDecision Decide(ToolCall call)
        {
            switch (call.Name)
            {
                case "szukaj_plikow":
                case "pokaz_zawartosc":
                    return _files.Decide(call);
            }

            if (!Tools.Contains(call.Name))
            {
                return ConfirmationDecision.NotNeeded;
            }

            ResolvePath(call, out var problem);
            return problem != null ? ConfirmationDecision.Reject(problem) : ConfirmationDecision.NotNeeded;
        }

        /// <summary>Wczytany plik (z pamięci podręcznej, odświeżany po zmianie na dysku).</summary>
        public Rf5Document Document(string path)
        {
            lock (_lock)
            {
                if (_cache.TryGetValue(path, out var cached))
                {
                    if (!cached.IsStale())
                    {
                        return cached;
                    }

                    cached.Dispose();
                    _cache.Remove(path);
                }

                var document = Rf5Document.Open(path);
                _cache[path] = document;
                return document;
            }
        }

        private string Execute(ToolCall call)
        {
            var path = ResolvePath(call, out var problem);
            if (problem != null)
            {
                return problem;
            }

            var document = Document(path);
            switch (call.Name)
            {
                case "rfem_info": return Info(document);
                case "rfem_pret": return Member(document, call);
                case "rfem_wezel": return Node(document, call);
                case "rfem_przekroje": return Sections(document);
                case "rfem_przypadki": return Loadings(document);
                case "rfem_sily": return Forces(document, call);
                case "rfem_szukaj_pretow": return SearchMembers(document, call);
                default: return "BŁĄD: nieobsługiwane polecenie: " + call.Name;
            }
        }

        private string Info(Rf5Document document)
        {
            var model = document.Model;
            var text = new StringBuilder("OK ").Append(Path.GetFileName(document.Path)).Append(" (").Append(document.Container)
                .Append(model.Version.Length > 0 ? ", RFEM " + model.Version : "").Append(", ").Append(Num(document.SizeBytes / 1024.0 / 1024.0, 1)).Append(" MB)\n");

            var byType = model.Members.Values.GroupBy(m => m.TypeName).OrderByDescending(g => g.Count()).Select(g => g.Key + " " + g.Count()).ToList();
            text.Append("Model: ").Append(model.Nodes.Count).Append(" węzłów, ").Append(model.Lines.Count).Append(" linii, ").Append(model.Members.Count).Append(" prętów")
                .Append(byType.Count > 0 ? " (" + string.Join(", ", byType) + ")" : "").Append(", ").Append(model.CrossSections.Count).Append(" przekrojów, ")
                .Append(model.Materials.Count).Append(" materiałów.");
            if (model.Members.Count > 0)
            {
                var total = model.Members.Values.Select(m => document.MemberLength(m.No) ?? 0).Sum();
                text.Append(" Łączna długość prętów ").Append(Num(total, 1)).Append(" m; numery prętów ").Append(model.Members.Keys.Min()).Append('–').Append(model.Members.Keys.Max()).Append('.');
            }

            if (model.CrossSections.Count > 0)
            {
                text.Append("\nPrzekroje: ").Append(string.Join("; ", model.CrossSections.Values.Take(_options.MaxListed).Select(s =>
                {
                    var members = model.MembersWithSection(s.No);
                    var material = model.MaterialOf(s);
                    return s.No + " " + s.Name + (material != null ? " (" + material.Description + ")" : "") + " – " + Count(members.Count, "pręt", "pręty", "prętów")
                        + (members.Count > 0 ? ", " + Num(members.Sum(m => document.MemberLength(m.No) ?? 0), 1) + " m" : "");
                })));
                if (model.CrossSections.Count > _options.MaxListed)
                {
                    text.Append("; … (rfem_przekroje pokaże wszystkie)");
                }
            }

            if (model.Materials.Count > 0)
            {
                text.Append("\nMateriały: ").Append(string.Join("; ", model.Materials.Values.Select(m => m.No + " " + m.Description + (m.E > 0 ? " (E = " + Num(m.E / 1e9, 0) + " GPa)" : ""))));
            }

            if (model.LoadCases.Count > 0)
            {
                text.Append("\nPrzypadki (").Append(model.LoadCases.Count).Append("): ").Append(string.Join("; ", model.LoadCases.Take(30).Select(lc => "PO" + lc.No + " " + lc.Description)));
                if (model.LoadCases.Count > 30)
                {
                    text.Append("; …");
                }
            }

            if (model.Combinations.Count > 0)
            {
                text.Append("\nKombinacje obciążeń: ").Append(model.Combinations.Count).Append(" (KO").Append(model.Combinations.Min(c => c.No)).Append("–KO").Append(model.Combinations.Max(c => c.No)).Append(')');
                if (model.ResultCombinations.Count > 0)
                {
                    text.Append(", kombinacje wyników: ").Append(model.ResultCombinations.Count);
                }

                text.Append(" — lista: rfem_przypadki.");
            }

            if (document.HasResults)
            {
                var sets = document.Results.Sets;
                text.Append("\nWyniki: ").Append(sets.Count).Append(" zestawów (").Append(string.Join(", ", sets.Take(6).Select(s => s.ShortLabel)))
                    .Append(sets.Count > 6 ? " … " + sets[sets.Count - 1].ShortLabel : "").Append("), pręty z wynikami: ").Append(document.Results.MembersWithResults.Count)
                    .Append(". Siły w pręcie: rfem_sily (numer, obciazenie=KO5).")
                    .Append(document.Results.IgnoredSets > 0 ? " Pominięto " + document.Results.IgnoredSets + " zestawów bez protokołu solvera." : "");
            }
            else
            {
                text.Append("\nWyniki: brak (model nie był liczony albo zapisany bez wyników).");
            }

            AppendWarnings(text, model);
            return Truncate(text.ToString());
        }

        private string Member(Rf5Document document, ToolCall call)
        {
            var no = (int)(call.GetNumber("numer") ?? 0);
            if (no <= 0)
            {
                return "BŁĄD: podaj numer pręta (numer).";
            }

            var model = document.Model;
            if (!model.Members.TryGetValue(no, out var member))
            {
                return MissingMember(document, no);
            }

            var nodes = model.MemberNodes(member);
            var length = document.MemberLength(no);
            var text = new StringBuilder("OK pręt ").Append(no).Append(" (").Append(Path.GetFileName(document.Path)).Append("): ").Append(member.TypeName)
                .Append(", linia ").Append(member.LineNo).Append(", węzły ").Append(string.Join(" → ", nodes))
                .Append(length.HasValue ? ", długość " + Num(length.Value, 3) + " m" : ", długość nieznana (brak węzłów)")
                .Append(", obrót ").Append(Num(member.Rotation * 180 / Math.PI, 1)).Append("°").Append(member.Comment.Length > 0 ? ", komentarz „" + member.Comment + "”" : "").Append('.');

            text.Append("\nWęzły [m]: ").Append(string.Join("; ", nodes.Select(n => model.Nodes.TryGetValue(n, out var node) ? n + " (" + Num(node.X, 3) + "; " + Num(node.Y, 3) + "; " + Num(node.Z, 3) + ")" : n + " (?)")));

            var section = model.SectionOf(member);
            if (section != null)
            {
                AppendSection(text.Append('\n'), model, section);
                if (member.CrossSectionEnd > 0 && model.CrossSections.TryGetValue(member.CrossSectionEnd, out var end))
                {
                    text.Append("\nPrzekrój na końcu (zbieżny): ").Append(end.No).Append(' ').Append(end.Name);
                }
            }
            else
            {
                text.Append(member.CrossSectionStart > 0 ? "\nPrzekrój " + member.CrossSectionStart + " (brak w tabeli)." : member.Type == "BeamRigid" ? "\nBez przekroju (pręt sztywny)." : "\nBez przekroju.");
            }

            if (document.HasResults && document.Results.HasMember(no))
            {
                text.Append('\n').Append(Extremes(document, no));
            }
            else if (document.HasResults)
            {
                text.Append("\nWyniki: pręt nie ma sił w plikach solvera (pręt sztywny albo wyłączony z obliczeń).");
            }
            else
            {
                text.Append("\nWyniki: plik bez wyników (model nie był liczony).");
            }

            return Truncate(text.ToString());
        }

        private string MissingMember(Rf5Document document, int no)
        {
            var model = document.Model;
            if (model.Members.Count == 0)
            {
                return "BŁĄD: w tym modelu nie rozpoznano prętów" + (model.Nodes.Count > 0 ? " (są węzły i linie — model powierzchniowy albo inny zapis)." : ".");
            }

            var nearest = model.Members.Keys.OrderBy(k => Math.Abs(k - no)).Take(5).OrderBy(k => k).ToList();
            return "BŁĄD: nie ma pręta " + no + ". Pręty mają numery " + model.Members.Keys.Min() + "–" + model.Members.Keys.Max() + " (jest ich " + model.Members.Count + "); najbliższe: "
                + string.Join(", ", nearest) + ". Listę daje rfem_szukaj_pretow.";
        }

        private string Node(Rf5Document document, ToolCall call)
        {
            var no = (int)(call.GetNumber("numer") ?? 0);
            if (no <= 0)
            {
                return "BŁĄD: podaj numer węzła (numer).";
            }

            var model = document.Model;
            if (!model.Nodes.TryGetValue(no, out var node))
            {
                return "BŁĄD: nie ma węzła " + no + " (węzły " + (model.Nodes.Count > 0 ? model.Nodes.Keys.Min() + "–" + model.Nodes.Keys.Max() + ", jest ich " + model.Nodes.Count : "brak") + ").";
            }

            var members = model.MembersAtNode(no);
            var text = new StringBuilder("OK węzeł ").Append(no).Append(": X = ").Append(Num(node.X, 3)).Append(" m, Y = ").Append(Num(node.Y, 3)).Append(" m, Z = ").Append(Num(node.Z, 3)).Append(" m")
                .Append(node.CoordinateSystem.Length > 0 && node.CoordinateSystem != "Standard" ? " (układ " + node.CoordinateSystem + ")" : "").Append(node.Comment.Length > 0 ? ", komentarz „" + node.Comment + "”" : "").Append('.');
            var lines = model.Lines.Values.Where(l => l.Nodes.Contains(no)).Select(l => l.No).ToList();
            text.Append("\nLinie: ").Append(lines.Count == 0 ? "brak" : string.Join(", ", lines));
            text.Append("\nPręty w węźle (").Append(members.Count).Append("): ").Append(members.Count == 0 ? "brak" : string.Join("; ", members.Select(m => MemberLine(document, m))));
            return Truncate(text.ToString());
        }

        private string Sections(Rf5Document document)
        {
            var model = document.Model;
            if (model.CrossSections.Count == 0)
            {
                return "BŁĄD: w pliku nie rozpoznano tabeli przekrojów" + (model.Members.Count > 0 ? " (pręty są: " + model.Members.Count + ")." : ".");
            }

            var text = new StringBuilder("OK przekroje (").Append(model.CrossSections.Count).Append(") w ").Append(Path.GetFileName(document.Path)).Append("; A [cm²], Iy, Iz, It [cm⁴]:");
            foreach (var section in model.CrossSections.Values)
            {
                var members = model.MembersWithSection(section.No);
                var material = model.MaterialOf(section);
                text.Append('\n').Append(section.No).Append(" | ").Append(section.Name).Append(section.TextId.Length > 0 && section.TextId != section.Description ? " (" + section.TextId + ")" : "")
                    .Append(" | ").Append(material != null ? material.Description : "materiał " + section.MaterialNo)
                    .Append(" | A = ").Append(Num(section.Area * 1e4, 2)).Append(" | Iy = ").Append(Num(section.Iy * 1e8, 1)).Append(" | Iz = ").Append(Num(section.Iz * 1e8, 1)).Append(" | It = ").Append(Num(section.It * 1e8, 2))
                    .Append(" | ").Append(Count(members.Count, "pręt", "pręty", "prętów"))
                    .Append(members.Count > 0 ? " (" + string.Join(", ", members.Select(m => m.No).Take(15)) + (members.Count > 15 ? ", …" : "") + "), razem " + Num(members.Sum(m => document.MemberLength(m.No) ?? 0), 2) + " m" : "");
            }

            if (model.Materials.Count > 0)
            {
                text.Append("\nMateriały: ").Append(string.Join("; ", model.Materials.Values.Select(MaterialLine)));
            }

            return Truncate(text.ToString());
        }

        private string Loadings(Rf5Document document)
        {
            var model = document.Model;
            if (model.LoadCases.Count == 0 && model.Combinations.Count == 0)
            {
                return "BŁĄD: w pliku nie rozpoznano przypadków ani kombinacji obciążeń.";
            }

            var withResults = new HashSet<string>(document.HasResults ? document.Results.Sets.Select(s => s.ShortLabel) : Enumerable.Empty<string>());
            var text = new StringBuilder("OK przypadki obciążeń (").Append(model.LoadCases.Count).Append("):");
            foreach (var lc in model.LoadCases)
            {
                text.Append("\nPO").Append(lc.No).Append(' ').Append(lc.Description).Append(withResults.Contains("PO" + lc.No) ? " [wyniki]" : "");
            }

            if (model.Combinations.Count > 0)
            {
                text.Append("\nKombinacje obciążeń (").Append(model.Combinations.Count).Append("), PO = przypadek:");
                foreach (var co in model.Combinations.Take(_options.MaxListed))
                {
                    text.Append("\nKO").Append(co.No).Append(co.Description.Length > 0 ? " " + co.Description : "").Append(": ").Append(co.ExpressionPl).Append(withResults.Contains("KO" + co.No) ? " [wyniki]" : "");
                }

                if (model.Combinations.Count > _options.MaxListed)
                {
                    text.Append("\n… i ").Append(model.Combinations.Count - _options.MaxListed).Append(" dalszych");
                }
            }

            if (model.ResultCombinations.Count > 0)
            {
                text.Append("\nKombinacje wyników (").Append(model.ResultCombinations.Count).Append("), KO = kombinacja, „lub” = obwiednia:");
                foreach (var rc in model.ResultCombinations.Take(20))
                {
                    text.Append("\nKW").Append(rc.No).Append(rc.Description.Length > 0 ? " " + rc.Description : "").Append(": ").Append(rc.ExpressionPl);
                }
            }

            if (document.HasResults)
            {
                var sets = document.Results.Sets;
                var unmatched = sets.Where(s => s.CombinationNo > 0 && model.Combination(s.CombinationNo) == null).Count();
                text.Append("\nZestawy wyników: ").Append(sets.Count).Append(" (").Append(string.Join(", ", sets.Take(8).Select(s => s.ShortLabel))).Append(sets.Count > 8 ? ", …" : "").Append(')');
                if (unmatched > 0)
                {
                    text.Append("; ").Append(unmatched).Append(" zestawów wskazuje kombinacje, których nie ma w tabeli (wyniki starsze niż model?)");
                }
            }
            else
            {
                text.Append("\nWyników nie ma (model nie był liczony).");
            }

            return Truncate(text.ToString());
        }

        private string Forces(Rf5Document document, ToolCall call)
        {
            var no = (int)(call.GetNumber("numer") ?? 0);
            if (no <= 0)
            {
                return "BŁĄD: podaj numer pręta (numer).";
            }

            if (!document.HasResults)
            {
                return "BŁĄD: plik " + Path.GetFileName(document.Path) + " nie ma wyników — model nie był liczony w RFEM albo zapisano go bez wyników. Geometrię i przekrój pręta pokaże rfem_pret.";
            }

            if (!document.Model.Members.ContainsKey(no) && !document.Results.HasMember(no))
            {
                return MissingMember(document, no);
            }

            if (!document.Results.HasMember(no))
            {
                return "BŁĄD: pręt " + no + " nie ma sił w plikach solvera (pręt sztywny albo wyłączony z obliczeń). Geometrię pokaże rfem_pret.";
            }

            var what = (call.GetString("co") ?? "").Trim().ToLowerInvariant();
            var displacements = what.StartsWith("przem") || what.StartsWith("ugi") || what.StartsWith("deform");
            var loading = call.GetString("obciazenie");
            if (string.IsNullOrWhiteSpace(loading))
            {
                return Truncate(displacements ? DisplacementExtremes(document, no) : Extremes(document, no, true));
            }

            var set = document.Results.Find(loading);
            if (set == null)
            {
                var available = document.Results.Sets.Select(s => s.ShortLabel).ToList();
                return "BŁĄD: nie ma wyników dla „" + loading + "”. Są: " + string.Join(", ", available.Take(12)) + (available.Count > 12 ? ", … (" + available.Count + " zestawów, rfem_przypadki)" : "") + ".";
            }

            var points = Dedupe(document.Results.MemberForces(set, no));
            var text = new StringBuilder("OK pręt ").Append(no).Append(", ").Append(set.Label(document.Model));
            if (displacements)
            {
                text.Append(", przemieszczenia [mm], x od początku pręta [m]:\nx | ux | uy | uz");
                foreach (var point in points.Where(p => p.Displacements != null))
                {
                    text.Append('\n').Append(Num(point.X, 3)).Append(" | ").Append(Num(point.Displacements[0] * 1e3, 2)).Append(" | ").Append(Num(point.Displacements[1] * 1e3, 2)).Append(" | ").Append(Num(point.Displacements[2] * 1e3, 2));
                }

                var max = points.Where(p => p.Displacements != null).Select(p => Math.Sqrt(p.Displacements.Sum(d => d * d))).DefaultIfEmpty(0).Max();
                text.Append("\nNajwiększe przemieszczenie wypadkowe: ").Append(Num(max * 1e3, 2)).Append(" mm.");
                return Truncate(text.ToString());
            }

            text.Append(", siły [kN], momenty [kNm], x od początku pręta [m]:\nx | N | Vy | Vz | Mt | My | Mz");
            foreach (var point in points)
            {
                text.Append('\n').Append(Num(point.X, 3));
                foreach (var component in Rf5Results.Components)
                {
                    text.Append(" | ").Append(Num(point.Component(component) / 1e3, 2));
                }
            }

            text.Append("\nEkstrema: ");
            text.Append(string.Join("; ", Rf5Results.Components.Select(c =>
            {
                var min = points.OrderBy(p => p.Component(c)).First();
                var max = points.OrderByDescending(p => p.Component(c)).First();
                return c + " od " + Num(min.Component(c) / 1e3, 2) + " (x = " + Num(min.X, 3) + ") do " + Num(max.Component(c) / 1e3, 2) + " (x = " + Num(max.X, 3) + ")";
            })));
            text.Append(". N ujemne = ściskanie.");
            return Truncate(text.ToString());
        }

        /// <summary>Ekstrema sił pręta ze wszystkich zestawów wyników.</summary>
        private string Extremes(Rf5Document document, int no, bool standalone = false)
        {
            var sets = document.Results.Sets;
            var best = new Dictionary<string, Tuple<double, Rf5ResultSet, double>>();
            var worst = new Dictionary<string, Tuple<double, Rf5ResultSet, double>>();
            var failures = 0;
            foreach (var set in sets)
            {
                IList<Rf5ForcePoint> points;
                try
                {
                    points = document.Results.MemberForces(set, no);
                }
                catch (RfemException)
                {
                    failures++;
                    continue;
                }

                foreach (var component in Rf5Results.Components)
                {
                    foreach (var point in points)
                    {
                        var value = point.Component(component);
                        if (!best.TryGetValue(component, out var max) || value > max.Item1)
                        {
                            best[component] = Tuple.Create(value, set, point.X);
                        }

                        if (!worst.TryGetValue(component, out var min) || value < min.Item1)
                        {
                            worst[component] = Tuple.Create(value, set, point.X);
                        }
                    }
                }
            }

            if (best.Count == 0)
            {
                return "Wyniki: nie udało się odczytać sił pręta " + no + ".";
            }

            var text = new StringBuilder(standalone ? "OK pręt " + no + ": " : "Wyniki: ").Append("ekstrema ze wszystkich ").Append(sets.Count).Append(" zestawów [kN, kNm], N ujemne = ściskanie:");
            foreach (var component in Rf5Results.Components)
            {
                var min = worst[component];
                var max = best[component];
                text.Append('\n').Append(component).Append(": min ").Append(Num(min.Item1 / 1e3, 2)).Append(" (").Append(min.Item2.ShortLabel).Append(", x = ").Append(Num(min.Item3, 3)).Append(")")
                    .Append(", max ").Append(Num(max.Item1 / 1e3, 2)).Append(" (").Append(max.Item2.ShortLabel).Append(", x = ").Append(Num(max.Item3, 3)).Append(')');
            }

            var labels = Rf5Results.Components.SelectMany(c => new[] { worst[c].Item2, best[c].Item2 }).GroupBy(s => s.ShortLabel).Select(g => g.First()).Take(6).ToList();
            text.Append("\nKombinacje: ").Append(string.Join("; ", labels.Select(s => s.Label(document.Model))));
            text.Append("\nPrzebieg dla jednej kombinacji: rfem_sily z obciazenie=").Append(labels[0].ShortLabel).Append('.');
            if (failures > 0)
            {
                text.Append("\nNie odczytano ").Append(failures).Append(" zestawów.");
            }

            return text.ToString();
        }

        private string DisplacementExtremes(Rf5Document document, int no)
        {
            var sets = document.Results.Sets;
            Tuple<double, Rf5ResultSet, double> best = null;
            foreach (var set in sets)
            {
                IList<Rf5ForcePoint> points;
                try
                {
                    points = document.Results.MemberForces(set, no);
                }
                catch (RfemException)
                {
                    continue;
                }

                foreach (var point in points.Where(p => p.Displacements != null))
                {
                    var value = Math.Sqrt(point.Displacements.Sum(d => d * d));
                    if (best == null || value > best.Item1)
                    {
                        best = Tuple.Create(value, set, point.X);
                    }
                }
            }

            return best == null
                ? "BŁĄD: pliki wyników nie mają bloku przemieszczeń pręta " + no + "."
                : "OK pręt " + no + ": największe przemieszczenie wypadkowe " + Num(best.Item1 * 1e3, 2) + " mm (" + best.Item2.Label(document.Model) + ", x = " + Num(best.Item3, 3) + " m) ze wszystkich " + sets.Count + " zestawów. Przebieg: rfem_sily z co=przemieszczenia i obciazenie=" + best.Item2.ShortLabel + ".";
        }

        private string SearchMembers(Rf5Document document, ToolCall call)
        {
            var model = document.Model;
            if (model.Members.Count == 0)
            {
                return "BŁĄD: w tym modelu nie rozpoznano prętów" + (model.Nodes.Count > 0 ? " (są węzły i linie — model powierzchniowy albo inny zapis)." : ".");
            }

            IEnumerable<Rf5Member> members = model.Members.Values;
            var criteria = new List<string>();
            var section = call.GetString("przekroj");
            if (call.GetNumber("przekroj").HasValue)
            {
                section = ((int)call.GetNumber("przekroj").Value).ToString(CultureInfo.InvariantCulture);
            }

            if (!string.IsNullOrWhiteSpace(section))
            {
                var wanted = section.Trim();
                if (int.TryParse(wanted, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sectionNo))
                {
                    members = members.Where(m => m.CrossSectionStart == sectionNo || m.CrossSectionEnd == sectionNo);
                    criteria.Add("przekrój " + sectionNo);
                }
                else
                {
                    var key = Normalize(wanted);
                    members = members.Where(m =>
                    {
                        var s = model.SectionOf(m);
                        return s != null && (Normalize(s.Description).Contains(key) || Normalize(s.TextId).Contains(key));
                    });
                    criteria.Add("przekrój „" + wanted + "”");
                }
            }

            var type = call.GetString("typ");
            if (!string.IsNullOrWhiteSpace(type))
            {
                var key = type.Trim().ToLowerInvariant();
                members = members.Where(m => TypeMatches(m, key));
                criteria.Add("typ " + type.Trim());
            }

            var node = call.GetNumber("wezel");
            if (node.HasValue && node.Value > 0)
            {
                var n = (int)node.Value;
                members = members.Where(m => model.MemberNodes(m).Contains(n));
                criteria.Add("węzeł " + n);
            }

            var minLength = call.GetNumber("min_dlugosc");
            var maxLength = call.GetNumber("max_dlugosc");
            if (minLength.HasValue || maxLength.HasValue)
            {
                members = members.Where(m =>
                {
                    var length = document.MemberLength(m.No);
                    return length.HasValue && (!minLength.HasValue || length.Value >= minLength.Value - 1e-6) && (!maxLength.HasValue || length.Value <= maxLength.Value + 1e-6);
                });
                criteria.Add("długość " + (minLength.HasValue ? "od " + Num(minLength.Value, 2) + " m " : "") + (maxLength.HasValue ? "do " + Num(maxLength.Value, 2) + " m" : ""));
            }

            var list = members.OrderBy(m => m.No).ToList();
            var text = new StringBuilder("OK ").Append(Count(list.Count, "pręt", "pręty", "prętów")).Append(criteria.Count > 0 ? " (" + string.Join(", ", criteria) + ")" : " (wszystkie)")
                .Append(" z ").Append(model.Members.Count).Append(" w ").Append(Path.GetFileName(document.Path)).Append(list.Count > 0 ? ", razem " + Num(list.Sum(m => document.MemberLength(m.No) ?? 0), 2) + " m:" : ".");
            foreach (var member in list.Take(_options.MaxListed))
            {
                text.Append('\n').Append(MemberLine(document, member));
            }

            if (list.Count > _options.MaxListed)
            {
                text.Append("\n… i ").Append(list.Count - _options.MaxListed).Append(" dalszych (zawęź kryteria).");
            }

            return Truncate(text.ToString());
        }

        private static bool TypeMatches(Rf5Member member, string key)
        {
            var name = member.TypeName.ToLowerInvariant();
            var raw = member.Type.ToLowerInvariant();
            if (name.Contains(key) || raw.Contains(key))
            {
                return true;
            }

            if (key.StartsWith("sztyw"))
            {
                return raw == "beamrigid";
            }

            if (key.StartsWith("kratow"))
            {
                return raw.StartsWith("truss");
            }

            if (key.StartsWith("cięg") || key.StartsWith("cieg") || key.StartsWith("rozciąg") || key.StartsWith("rozciag"))
            {
                return raw == "tension";
            }

            if (key.StartsWith("ścisk") || key.StartsWith("scisk"))
            {
                return raw == "compression";
            }

            return key.StartsWith("belk") && raw == "beam";
        }

        private string MemberLine(Rf5Document document, Rf5Member member)
        {
            var model = document.Model;
            var section = model.SectionOf(member);
            var length = document.MemberLength(member.No);
            return "pręt " + member.No + ": " + member.TypeName + ", " + (section != null ? section.Name : member.Type == "BeamRigid" ? "bez przekroju" : "przekrój " + member.CrossSectionStart)
                + (length.HasValue ? ", L = " + Num(length.Value, 3) + " m" : "") + ", węzły " + string.Join(" → ", model.MemberNodes(member)) + (member.Comment.Length > 0 ? " („" + member.Comment + "”)" : "");
        }

        private void AppendSection(StringBuilder text, Rf5Model model, Rf5CrossSection section)
        {
            var material = model.MaterialOf(section);
            text.Append("Przekrój ").Append(section.No).Append(": ").Append(section.Name).Append(section.TextId.Length > 0 && section.TextId != section.Description ? " (" + section.TextId + ")" : "")
                .Append(material != null ? ", materiał " + material.No + " " + material.Description + (material.E > 0 ? " (E = " + Num(material.E / 1e9, 0) + " GPa)" : "") : ", materiał " + section.MaterialNo);
            if (section.Area > 0)
            {
                text.Append("; A = ").Append(Num(section.Area * 1e4, 2)).Append(" cm², Iy = ").Append(Num(section.Iy * 1e8, 1)).Append(" cm⁴, Iz = ").Append(Num(section.Iz * 1e8, 1)).Append(" cm⁴, It = ").Append(Num(section.It * 1e8, 2)).Append(" cm⁴");
                if (section.Dimension1 > 0 && section.Dimension1 < 10)
                {
                    text.Append("; wymiary ").Append(Num(section.Dimension1 * 1e3, 0)).Append(section.Dimension2 > 0 && section.Dimension2 < 10 ? " × " + Num(section.Dimension2 * 1e3, 0) : "").Append(" mm");
                }
            }
        }

        private static string MaterialLine(Rf5Material material)
        {
            var text = new StringBuilder().Append(material.No).Append(' ').Append(material.Description);
            var details = new List<string>();
            if (material.KindName.Length > 0)
            {
                details.Add(material.KindName);
            }

            if (material.E > 0)
            {
                details.Add("E = " + Num(material.E / 1e9, 1) + " GPa");
            }

            if (material.G > 0)
            {
                details.Add("G = " + Num(material.G / 1e9, 1) + " GPa");
            }

            if (material.SpecificWeight > 0)
            {
                details.Add("γ = " + Num(material.SpecificWeight / 1e3, 1) + " kN/m³");
            }

            if (material.Norm.Length > 0)
            {
                details.Add(material.Norm);
            }

            return details.Count > 0 ? text.Append(" (").Append(string.Join(", ", details)).Append(')').ToString() : text.ToString();
        }

        private static void AppendWarnings(StringBuilder text, Rf5Model model)
        {
            if (model.Warnings.Count > 0)
            {
                text.Append("\nUwagi: ").Append(string.Join("; ", model.Warnings.Distinct().Take(5))).Append('.');
            }
        }

        /// <summary>Usuwa powtórzone punkty (solver zapisuje granice elementów dwa razy; zostają, gdy wartości różnią się — skok od siły skupionej).</summary>
        private static List<Rf5ForcePoint> Dedupe(IList<Rf5ForcePoint> points)
        {
            var result = new List<Rf5ForcePoint>();
            foreach (var point in points)
            {
                var last = result.Count > 0 ? result[result.Count - 1] : null;
                if (last != null && Math.Abs(last.X - point.X) < 1e-6 && Rf5Results.Components.All(c => Math.Abs(last.Component(c) - point.Component(c)) < 0.5))
                {
                    continue;
                }

                result.Add(point);
            }

            return result;
        }

        /// <summary>Ścieżka pliku .rf5 z argumentu <c>sciezka</c> albo jedyny plik .rf5 w folderze bieżącym.</summary>
        private string ResolvePath(ToolCall call, out string problem)
        {
            problem = null;
            var given = call.GetString("sciezka");
            if (string.IsNullOrWhiteSpace(given))
            {
                var folder = _files.Resolve(".", out problem);
                if (problem != null)
                {
                    return null;
                }

                var candidates = Directory.Exists(folder) ? Directory.GetFiles(folder, "*.rf5").OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList() : new List<string>();
                if (candidates.Count == 1)
                {
                    return candidates[0];
                }

                problem = candidates.Count == 0
                    ? "BŁĄD: podaj plik .rf5 (sciezka) — w folderze bieżącym " + folder + " nie ma plików .rf5 (szukaj_plikow z wzorcem *.rf5 znajdzie je w podfolderach)."
                    : "BŁĄD: w folderze bieżącym jest " + candidates.Count + " plików .rf5, podaj który: " + string.Join(", ", candidates.Take(10).Select(Path.GetFileName)) + ".";
                return null;
            }

            var path = _files.Resolve(given, out problem);
            if (problem != null)
            {
                return null;
            }

            if (Directory.Exists(path))
            {
                problem = "BŁĄD: " + path + " to folder, nie plik .rf5.";
                return null;
            }

            if (!File.Exists(path))
            {
                var alternative = path.EndsWith(".rf5", StringComparison.OrdinalIgnoreCase) ? null : path + ".rf5";
                if (alternative != null && File.Exists(alternative))
                {
                    return alternative;
                }

                problem = "BŁĄD: nie ma pliku " + path + " (szukaj_plikow z wzorcem *.rf5 pokaże modele w folderze).";
                return null;
            }

            if (!path.EndsWith(".rf5", StringComparison.OrdinalIgnoreCase))
            {
                problem = "BŁĄD: " + Path.GetFileName(path) + " nie jest plikiem RFEM 5 (.rf5)" + (path.EndsWith(".rf6", StringComparison.OrdinalIgnoreCase) ? " — plików RFEM 6 (.rf6) ta umiejętność nie czyta." : ".");
                return null;
            }

            return path;
        }

        private string Truncate(string text) => FileSystemExecutor.Truncate(text, _options.MaxOutputChars);

        /// <summary>Liczba po polsku (przecinek dziesiętny, bez grupowania).</summary>
        internal static string Num(double value, int decimals)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                return "?";
            }

            var text = Math.Round(value, decimals).ToString("F" + decimals, CultureInfo.InvariantCulture).Replace('.', ',');
            return text == "-0" || text.StartsWith("-0,") && text.Skip(3).All(c => c == '0') ? text.Substring(1) : text;
        }

        private static string Count(int count, string one, string few, string many)
        {
            var form = count == 1 ? one : count % 10 >= 2 && count % 10 <= 4 && (count % 100 < 10 || count % 100 >= 20) ? few : many;
            return count + " " + form;
        }

        private static string Normalize(string text) => (text ?? "").ToLowerInvariant().Replace(" ", "").Replace("-", "").Replace("_", "");

        /// <inheritdoc/>
        public void Dispose()
        {
            lock (_lock)
            {
                foreach (var document in _cache.Values)
                {
                    document.Dispose();
                }

                _cache.Clear();
            }
        }
    }
}
