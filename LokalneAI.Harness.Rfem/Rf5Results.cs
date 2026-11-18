using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace LokalneAI.Harness.Rfem
{
    /// <summary>Jeden zestaw wyników (jedno obliczenie solvera): kombinacja obciążeń albo przypadek.</summary>
    public sealed class Rf5ResultSet
    {
        /// <summary>„N” = obliczenie nieliniowe (<c>.HXN</c>, <c>.PRON</c>), „L” = liniowe (<c>.HXL</c>, <c>.PROL</c>).</summary>
        public string Kind { get; set; } = "N";

        /// <summary>Numer pliku wyników (1…).</summary>
        public int Index { get; set; }

        /// <summary>Numer kombinacji obciążeń (NCOMB z protokołu); 0 = obliczony pojedynczy przypadek.</summary>
        public int CombinationNo { get; set; }

        /// <summary>Numery przypadków wchodzących w obliczenie (NCASE).</summary>
        public int[] CaseNos { get; set; } = new int[0];

        /// <summary>Współczynniki przypadków (NCOEF).</summary>
        public double[] Coefficients { get; set; } = new double[0];

        /// <summary>Opis z protokołu (TEXT), zwykle pusty.</summary>
        public string Text { get; set; } = "";

        /// <summary>Strumień z siłami w prętach.</summary>
        public string StreamName { get; set; } = "";

        /// <summary>Przypadek, gdy obliczenie dotyczy jednego przypadku bez kombinacji.</summary>
        public int? SingleCaseNo => CombinationNo == 0 && CaseNos.Length == 1 ? CaseNos[0] : (int?)null;

        /// <summary>Krótka etykieta: „KO5” albo „PO3”, albo „analiza 7”.</summary>
        public string ShortLabel => CombinationNo > 0 ? "KO" + CombinationNo : SingleCaseNo.HasValue ? "PO" + SingleCaseNo.Value : "analiza " + Index;

        /// <summary>Etykieta z opisem z modelu, np. „KO5 (1.35*PO1 + 1.35*PO3 + PO21)”.</summary>
        public string Label(Rf5Model model)
        {
            if (CombinationNo > 0)
            {
                var combination = model?.Combination(CombinationNo);
                var expression = combination != null && combination.Expression.Length > 0 ? combination.ExpressionPl : ExpressionFromProtocol();
                var description = combination != null && combination.Description.Length > 0 ? " " + combination.Description : "";
                return "KO" + CombinationNo + description + (expression.Length > 0 ? " (" + expression + ")" : "");
            }

            if (SingleCaseNo.HasValue)
            {
                var loadCase = model?.LoadCase(SingleCaseNo.Value);
                return "PO" + SingleCaseNo.Value + (loadCase != null && loadCase.Description.Length > 0 ? " " + loadCase.Description : "");
            }

            var fromProtocol = ExpressionFromProtocol();
            return "analiza " + Index + (fromProtocol.Length > 0 ? " (" + fromProtocol + ")" : "") + (Text.Length > 0 ? " " + Text : "");
        }

        /// <summary>Wyrażenie z NCASE/NCOEF, np. „1.35*PO1 + 1.35*PO3 + PO21”.</summary>
        public string ExpressionFromProtocol()
        {
            var parts = new List<string>();
            for (var i = 0; i < CaseNos.Length; i++)
            {
                var factor = i < Coefficients.Length ? Coefficients[i] : 1.0;
                parts.Add((Math.Abs(factor - 1.0) < 1e-9 ? "" : factor.ToString("0.###", CultureInfo.InvariantCulture) + "*") + "PO" + CaseNos[i]);
            }

            return string.Join(" + ", parts);
        }
    }

    /// <summary>Siły wewnętrzne i przemieszczenia w jednym punkcie pręta (jednostki SI pliku: N, Nm, m, rad).</summary>
    public sealed class Rf5ForcePoint
    {
        /// <summary>Odległość od początku pręta [m].</summary>
        public double X { get; set; }

        /// <summary>Siła osiowa N [N] (ujemna = ściskanie, jak w RFEM).</summary>
        public double N { get; set; }

        /// <summary>Siła poprzeczna Vy [N].</summary>
        public double Vy { get; set; }

        /// <summary>Siła poprzeczna Vz [N].</summary>
        public double Vz { get; set; }

        /// <summary>Moment skręcający Mt [Nm].</summary>
        public double Mt { get; set; }

        /// <summary>Moment zginający My [Nm].</summary>
        public double My { get; set; }

        /// <summary>Moment zginający Mz [Nm].</summary>
        public double Mz { get; set; }

        /// <summary>Przemieszczenia ux, uy, uz [m] (null, gdy plik nie ma bloku przemieszczeń).</summary>
        public double[] Displacements { get; set; }

        /// <summary>Składowa po nazwie (N, Vy, Vz, Mt, My, Mz).</summary>
        public double Component(string name)
        {
            switch (name)
            {
                case "N": return N;
                case "Vy": return Vy;
                case "Vz": return Vz;
                case "Mt": return Mt;
                case "My": return My;
                case "Mz": return Mz;
                default: throw new ArgumentOutOfRangeException(nameof(name));
            }
        }
    }

    /// <summary>
    /// Wyniki z folderu <c>Results_Main_V_2</c>: siły w prętach z plików <c>.HXN&lt;nr&gt;</c> (obliczenia nieliniowe) i
    /// <c>.HXL&lt;nr&gt;</c> (liniowe). Który to przypadek albo kombinacja, mówi protokół solvera <c>.PRON&lt;nr&gt;</c> /
    /// <c>.PROL&lt;nr&gt;</c> (wiersze <c>NCOMB=</c>, <c>NCASE=</c>, <c>NCOEF=</c>). Kolejność prętów w plikach jest z <c>Mesh/.MTO</c>.
    /// Układ pliku sił: nagłówek z liczb int64 — [29] liczba prętów, [31] blok prętów (i32 liczba punktów, i32 pierwszy punkt),
    /// [34] liczba punktów, [36] blok punktów (f8 x wzdłuż pręta, i32), [40]/[41] siły (6×f4: N, Vy, Vz, Mt, My, Mz) i ich rozmiar,
    /// [46]/[47] przemieszczenia (6×f4). Kolejność i znaki składowych przyjęte jak w RFEM 6 (sprawdzone na ciężarze własnym słupa:
    /// N ujemne przy ściskaniu); Mz nie było sprawdzane z programem.
    /// </summary>
    public sealed class Rf5Results
    {
        /// <summary>Składowe sił w kolejności z pliku.</summary>
        public static readonly string[] Components = { "N", "Vy", "Vz", "Mt", "My", "Mz" };

        private static readonly Regex ForcesName = new Regex(@"^\.HX([NL])(\d{6})$", RegexOptions.Compiled);
        private readonly Rf5Archive _archive;
        private readonly Rf5Mesh _mesh;
        private readonly Dictionary<string, byte[]> _cache = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        private readonly List<Rf5ResultSet> _sets = new List<Rf5ResultSet>();

        private Rf5Results(Rf5Archive archive, Rf5Mesh mesh)
        {
            _archive = archive;
            _mesh = mesh;
        }

        /// <summary>Zestawy wyników w kolejności numerów plików (nieliniowe, potem liniowe).</summary>
        public IReadOnlyList<Rf5ResultSet> Sets => _sets;

        /// <summary>Pręty, które mają wyniki (kolejność solvera).</summary>
        public IReadOnlyList<int> MembersWithResults => _mesh.MemberOrder;

        /// <summary>Czyta spis wyników; null, gdy plik nie ma wyników sił w prętach.</summary>
        public static Rf5Results Read(Rf5Archive archive, Rf5Mesh mesh)
        {
            var names = archive.List("Results_Main_V_2");
            if (names.Count == 0)
            {
                return null;
            }

            var results = new Rf5Results(archive, mesh);
            foreach (var name in names)
            {
                var match = ForcesName.Match(name);
                if (!match.Success)
                {
                    continue;
                }

                var set = new Rf5ResultSet
                {
                    Kind = match.Groups[1].Value,
                    Index = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture),
                    StreamName = "Results_Main_V_2/" + name,
                };
                var protocol = "Results_Main_V_2/.PRO" + set.Kind + match.Groups[2].Value;
                if (archive.Exists(protocol))
                {
                    ReadProtocol(archive.ReadText(protocol), set);
                }

                results._sets.Add(set);
            }

            // Obliczenie bez protokołu (np. liniowy przebieg pomocniczy .HXL bez NCOMB/NCASE) nie wiadomo, czego dotyczy — pomijamy, ale liczymy.
            results.IgnoredSets = results._sets.RemoveAll(s => s.CombinationNo == 0 && s.CaseNos.Length == 0);
            results._sets.Sort((a, b) => a.Kind != b.Kind ? string.CompareOrdinal(b.Kind, a.Kind) : a.Index.CompareTo(b.Index));
            return results._sets.Count == 0 ? null : results;
        }

        /// <summary>Zestawy pominięte, bo protokół solvera nie mówi, którego przypadku albo kombinacji dotyczą.</summary>
        public int IgnoredSets { get; private set; }

        /// <summary>Czyta <c>KLUCZ=wartość !</c> z protokołu solvera.</summary>
        public static void ReadProtocol(string text, Rf5ResultSet set)
        {
            foreach (var raw in (text ?? "").Split('\n'))
            {
                var line = raw.Trim().TrimEnd('!').Trim();
                var eq = line.IndexOf('=');
                if (eq <= 0)
                {
                    continue;
                }

                var key = line.Substring(0, eq).Trim();
                var value = line.Substring(eq + 1).Trim();
                switch (key)
                {
                    case "NCOMB":
                        set.CombinationNo = int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var combination) ? combination : 0;
                        break;
                    case "NCASE":
                        set.CaseNos = value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(v => int.TryParse(v.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0).Where(n => n > 0).ToArray();
                        break;
                    case "NCOEF":
                        set.Coefficients = value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(v => double.TryParse(v.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : 1.0).ToArray();
                        break;
                    case "TEXT":
                        set.Text = value;
                        break;
                }
            }
        }

        /// <summary>Zestaw dla „KO5”, „CO5”, „5” (kombinacja) albo „PO3”, „LC3” (przypadek); null, gdy nie ma.</summary>
        public Rf5ResultSet Find(string loading)
        {
            if (string.IsNullOrWhiteSpace(loading))
            {
                return null;
            }

            var text = loading.Trim().ToUpperInvariant().Replace(" ", "");
            var digits = new string(text.Where(char.IsDigit).ToArray());
            if (digits.Length == 0 || !int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
            {
                return null;
            }

            var isCase = text.StartsWith("PO") || text.StartsWith("LC") || text.StartsWith("LF") || text.StartsWith("PRZYP");
            if (isCase)
            {
                return _sets.FirstOrDefault(s => s.SingleCaseNo == number);
            }

            return _sets.FirstOrDefault(s => s.CombinationNo == number) ?? _sets.FirstOrDefault(s => s.SingleCaseNo == number);
        }

        /// <summary>Czy pręt ma wyniki (jest w siatce solvera).</summary>
        public bool HasMember(int memberNo) => _mesh.MemberOrder.Contains(memberNo);

        /// <summary>Siły i przemieszczenia wzdłuż pręta dla zestawu wyników.</summary>
        public IList<Rf5ForcePoint> MemberForces(Rf5ResultSet set, int memberNo)
        {
            var position = _mesh.MemberOrder.IndexOf(memberNo);
            if (position < 0)
            {
                throw new RfemException("BŁĄD: pręt " + memberNo + " nie ma wyników (nie ma go w siatce solvera — pręt sztywny, wyłączony albo numer nie istnieje).");
            }

            if (!_cache.TryGetValue(set.StreamName, out var raw))
            {
                raw = _archive.Read(set.StreamName);
                _cache[set.StreamName] = raw;
            }

            return Decode(raw, position, memberNo);
        }

        /// <summary>Rozkodowuje siły pręta (pozycja z <c>.MTO</c>) z pliku <c>.HXN</c>/<c>.HXL</c>.</summary>
        public static IList<Rf5ForcePoint> Decode(byte[] raw, int position, int memberNo)
        {
            if (raw == null || raw.Length < 60 * 8)
            {
                throw new RfemException("BŁĄD: plik wyników jest za krótki.");
            }

            var header = new long[54];
            for (var i = 0; i < header.Length; i++)
            {
                header[i] = BitConverter.ToInt64(raw, 8 * i);
            }

            var memberCount = header[29];
            var membersOffset = header[31];
            var pointsOffset = header[36];
            var forcesOffset = header[40];
            var forcesSize = header[41];
            var displacementsOffset = header[46];
            if (memberCount <= 0 || membersOffset <= 0 || pointsOffset <= 0 || forcesOffset <= 0 || forcesOffset + forcesSize > raw.Length || header[39] != 6)
            {
                throw new RfemException("BŁĄD: nieznany układ pliku wyników (inna wersja solvera RFEM?).");
            }

            if (position >= memberCount)
            {
                throw new RfemException("BŁĄD: pręt " + memberNo + " jest poza zakresem pliku wyników (" + memberCount + " prętów).");
            }

            var count = BitConverter.ToInt32(raw, (int)membersOffset + 8 * position);
            var start = BitConverter.ToInt32(raw, (int)membersOffset + 8 * position + 4);
            var points = new List<Rf5ForcePoint>(Math.Max(0, count));
            var hasDisplacements = displacementsOffset > 0 && displacementsOffset + 24L * (start + count) <= raw.Length;
            for (var k = start; k < start + count; k++)
            {
                var p = (int)pointsOffset + 12 * k;
                var f = (int)forcesOffset + 24 * k;
                if (p + 12 > raw.Length || f + 24 > raw.Length)
                {
                    break;
                }

                var point = new Rf5ForcePoint
                {
                    X = BitConverter.ToDouble(raw, p),
                    N = BitConverter.ToSingle(raw, f),
                    Vy = BitConverter.ToSingle(raw, f + 4),
                    Vz = BitConverter.ToSingle(raw, f + 8),
                    Mt = BitConverter.ToSingle(raw, f + 12),
                    My = BitConverter.ToSingle(raw, f + 16),
                    Mz = BitConverter.ToSingle(raw, f + 20),
                };
                if (hasDisplacements)
                {
                    var d = (int)displacementsOffset + 24 * k;
                    point.Displacements = new double[] { BitConverter.ToSingle(raw, d), BitConverter.ToSingle(raw, d + 4), BitConverter.ToSingle(raw, d + 8) };
                }

                points.Add(point);
            }

            return points;
        }
    }
}
