using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace LokalneAI.Harness.Rfem
{
    /// <summary>Węzeł modelu.</summary>
    public sealed class Rf5Node
    {
        /// <summary>Numer węzła.</summary>
        public int No { get; set; }

        /// <summary>X [m].</summary>
        public double X { get; set; }

        /// <summary>Y [m].</summary>
        public double Y { get; set; }

        /// <summary>Z [m] (w układzie pliku; RFEM pokazuje Z w dół albo w górę według ustawień modelu).</summary>
        public double Z { get; set; }

        /// <summary>Układ współrzędnych („Standard”).</summary>
        public string CoordinateSystem { get; set; } = "";

        /// <summary>Komentarz z tabeli RFEM (np. „Podparty”).</summary>
        public string Comment { get; set; } = "";

        /// <summary>Odległość do innego węzła [m].</summary>
        public double DistanceTo(Rf5Node other) => Math.Sqrt((X - other.X) * (X - other.X) + (Y - other.Y) * (Y - other.Y) + (Z - other.Z) * (Z - other.Z));
    }

    /// <summary>Linia (oś pręta): lista węzłów.</summary>
    public sealed class Rf5Line
    {
        /// <summary>Numer linii.</summary>
        public int No { get; set; }

        /// <summary>Węzły po kolei (prosta: 2, łamana: więcej).</summary>
        public IList<int> Nodes { get; set; } = new List<int>();

        /// <summary>Komentarz z tabeli RFEM.</summary>
        public string Comment { get; set; } = "";
    }

    /// <summary>Pręt.</summary>
    public sealed class Rf5Member
    {
        /// <summary>Numer pręta.</summary>
        public int No { get; set; }

        /// <summary>Numer linii.</summary>
        public int LineNo { get; set; }

        /// <summary>Typ z pliku: <c>Beam</c>, <c>BeamRigid</c>, <c>Truss</c>, <c>TrussN</c>, <c>Tension</c>, <c>Compression</c>, <c>Cable</c>…</summary>
        public string Type { get; set; } = "";

        /// <summary>Obrót pręta [rad] (kąt β).</summary>
        public double Rotation { get; set; }

        /// <summary>Przekrój na początku (0 = brak, np. pręt sztywny).</summary>
        public int CrossSectionStart { get; set; }

        /// <summary>Przekrój na końcu (0 = taki sam jak na początku).</summary>
        public int CrossSectionEnd { get; set; }

        /// <summary>Druga lista liczb z rekordu (prawdopodobnie przeguby początek/koniec; nierozpoznane do końca).</summary>
        public IList<int> SecondList { get; set; } = new List<int>();

        /// <summary>Komentarz z tabeli RFEM (np. „słup osi A”).</summary>
        public string Comment { get; set; } = "";

        /// <summary>Typ po polsku.</summary>
        public string TypeName
        {
            get
            {
                switch (Type)
                {
                    case "Beam": return "belka";
                    case "BeamRigid": return "pręt sztywny";
                    case "Truss": return "kratownicowy";
                    case "TrussN": return "kratownicowy (tylko N)";
                    case "Tension": return "cięgno (tylko rozciąganie)";
                    case "Compression": return "tylko ściskanie";
                    case "Cable": return "lina";
                    case "Rib": return "żebro";
                    default: return Type;
                }
            }
        }
    }

    /// <summary>Przekrój.</summary>
    public sealed class Rf5CrossSection
    {
        /// <summary>Numer przekroju.</summary>
        public int No { get; set; }

        /// <summary>Opis z tabeli (w języku modelu), np. „HE A 160 | DIN 1025-3:1994”.</summary>
        public string Description { get; set; } = "";

        /// <summary>Nazwa z biblioteki (TextID, zwykle niemiecka), np. „HE-A 160”, „Rohr 810/4”.</summary>
        public string TextId { get; set; } = "";

        /// <summary>Numer materiału.</summary>
        public int MaterialNo { get; set; }

        /// <summary>Pole przekroju A [m²].</summary>
        public double Area { get; set; }

        /// <summary>Moment bezwładności Iy [m⁴].</summary>
        public double Iy { get; set; }

        /// <summary>Moment bezwładności Iz [m⁴].</summary>
        public double Iz { get; set; }

        /// <summary>Moment bezwładności przy skręcaniu It [m⁴].</summary>
        public double It { get; set; }

        /// <summary>Wymiar 1 (szerokość/średnica) [m].</summary>
        public double Dimension1 { get; set; }

        /// <summary>Wymiar 2 (wysokość) [m].</summary>
        public double Dimension2 { get; set; }

        /// <summary>Komentarz z tabeli RFEM.</summary>
        public string Comment { get; set; } = "";

        /// <summary>Krótka nazwa dla ludzi.</summary>
        public string Name => string.IsNullOrWhiteSpace(Description) ? TextId : Description;
    }

    /// <summary>Materiał.</summary>
    public sealed class Rf5Material
    {
        /// <summary>Numer materiału.</summary>
        public int No { get; set; }

        /// <summary>Opis, np. „Stal S 355”.</summary>
        public string Description { get; set; } = "";

        /// <summary>Moduł sprężystości E [Pa]; 0 = nie odczytano.</summary>
        public double E { get; set; }

        /// <summary>Moduł ścinania G [Pa]; 0 = nie odczytano.</summary>
        public double G { get; set; }

        /// <summary>Ciężar objętościowy γ [N/m³]; 0 = nie odczytano.</summary>
        public double SpecificWeight { get; set; }

        /// <summary>Współczynnik rozszerzalności cieplnej αT [1/K]; 0 = nie odczytano.</summary>
        public double ThermalExpansion { get; set; }

        /// <summary>Rodzaj z biblioteki: STEEL, CONCRETE, TIMBER… (pusty = nieznany).</summary>
        public string Kind { get; set; } = "";

        /// <summary>Norma, np. „EN 1993-1-1:2005-05”.</summary>
        public string Norm { get; set; } = "";

        /// <summary>Komentarz z tabeli RFEM.</summary>
        public string Comment { get; set; } = "";

        /// <summary>Rodzaj po polsku.</summary>
        public string KindName
        {
            get
            {
                switch ((Kind ?? "").ToUpperInvariant())
                {
                    case "STEEL": return "stal";
                    case "CONCRETE": return "beton";
                    case "TIMBER": return "drewno";
                    case "ALUMINIUM": case "ALUMINUM": return "aluminium";
                    case "MASONRY": return "mur";
                    case "": return "";
                    default: return Kind.ToLowerInvariant();
                }
            }
        }
    }

    /// <summary>Przypadek obciążeń (LC).</summary>
    public sealed class Rf5LoadCase
    {
        /// <summary>Numer przypadku (numeracja użytkownika, z lukami).</summary>
        public int No { get; set; }

        /// <summary>Opis.</summary>
        public string Description { get; set; } = "";

        /// <summary>Współczynnik ciężaru własnego z rekordu (1 = ciężar własny włączony; znaczenie nie jest pewne).</summary>
        public double SelfWeightFactor { get; set; }
    }

    /// <summary>Kombinacja obciążeń (CO) albo wyników (RC).</summary>
    public sealed class Rf5Combination
    {
        /// <summary>Numer kombinacji.</summary>
        public int No { get; set; }

        /// <summary>Opis (może być pusty przy kombinacjach generowanych).</summary>
        public string Description { get; set; } = "";

        /// <summary>Wyrażenie z pliku, np. <c>1.35*LF1 + 1.35*LF3 + LF21</c> (LF = przypadek, LK = kombinacja, „o” = lub).</summary>
        public string Expression { get; set; } = "";

        /// <summary>Wyrażenie z polskimi skrótami: LF→PO (przypadek), LK→KO (kombinacja), „o” → „lub”.</summary>
        public string ExpressionPl => Expression.Replace("LF", "PO").Replace("LK", "KO").Replace(" o ", " lub ");
    }

    /// <summary>
    /// Model z pliku .rf5: tabele odczytane ze strumienia InputData. Wszystko, czego nie udało się rozpoznać, jest
    /// w <see cref="Warnings"/>; liczby są w jednostkach SI pliku (m, m², m⁴).
    /// </summary>
    public sealed class Rf5Model
    {
        /// <summary>Węzły po numerze.</summary>
        public SortedDictionary<int, Rf5Node> Nodes { get; } = new SortedDictionary<int, Rf5Node>();

        /// <summary>Linie po numerze.</summary>
        public SortedDictionary<int, Rf5Line> Lines { get; } = new SortedDictionary<int, Rf5Line>();

        /// <summary>Pręty po numerze.</summary>
        public SortedDictionary<int, Rf5Member> Members { get; } = new SortedDictionary<int, Rf5Member>();

        /// <summary>Przekroje po numerze.</summary>
        public SortedDictionary<int, Rf5CrossSection> CrossSections { get; } = new SortedDictionary<int, Rf5CrossSection>();

        /// <summary>Materiały po numerze.</summary>
        public SortedDictionary<int, Rf5Material> Materials { get; } = new SortedDictionary<int, Rf5Material>();

        /// <summary>Przypadki obciążeń w kolejności z pliku.</summary>
        public List<Rf5LoadCase> LoadCases { get; } = new List<Rf5LoadCase>();

        /// <summary>Kombinacje obciążeń (KO) w kolejności z pliku.</summary>
        public List<Rf5Combination> Combinations { get; } = new List<Rf5Combination>();

        /// <summary>Kombinacje wyników (KW, w RFEM „RC”) w kolejności z pliku; wyrażenia odwołują się do KO („LK1 o LK2”).</summary>
        public List<Rf5Combination> ResultCombinations { get; } = new List<Rf5Combination>();

        /// <summary>Uwagi z rozpoznawania (np. brak tabeli prętów w starszym zapisie).</summary>
        public List<string> Warnings { get; } = new List<string>();

        /// <summary>Wersja RFEM, która zapisała plik (z History.bin), albo pusta.</summary>
        public string Version { get; set; } = "";

        /// <summary>Długość pręta [m] z węzłów linii; null, gdy brakuje węzłów.</summary>
        public double? MemberLength(Rf5Member member)
        {
            if (member == null || !Lines.TryGetValue(member.LineNo, out var line) || line.Nodes.Count < 2)
            {
                return null;
            }

            double length = 0;
            for (var i = 0; i + 1 < line.Nodes.Count; i++)
            {
                if (!Nodes.TryGetValue(line.Nodes[i], out var a) || !Nodes.TryGetValue(line.Nodes[i + 1], out var b))
                {
                    return null;
                }

                length += a.DistanceTo(b);
            }

            return length;
        }

        /// <summary>Węzły pręta po kolei (puste, gdy brak linii).</summary>
        public IList<int> MemberNodes(Rf5Member member)
            => member != null && Lines.TryGetValue(member.LineNo, out var line) ? line.Nodes : new List<int>();

        /// <summary>Przekrój pręta (początek) albo null.</summary>
        public Rf5CrossSection SectionOf(Rf5Member member)
            => member != null && CrossSections.TryGetValue(member.CrossSectionStart, out var section) ? section : null;

        /// <summary>Materiał przekroju albo null.</summary>
        public Rf5Material MaterialOf(Rf5CrossSection section)
            => section != null && Materials.TryGetValue(section.MaterialNo, out var material) ? material : null;

        /// <summary>Przypadek po numerze albo null.</summary>
        public Rf5LoadCase LoadCase(int no) => LoadCases.FirstOrDefault(lc => lc.No == no);

        /// <summary>Kombinacja obciążeń po numerze albo null.</summary>
        public Rf5Combination Combination(int no) => Combinations.FirstOrDefault(co => co.No == no);

        /// <summary>Pręty, które leżą na danym węźle (przez linię).</summary>
        public IList<Rf5Member> MembersAtNode(int nodeNo)
            => Members.Values.Where(m => Lines.TryGetValue(m.LineNo, out var line) && line.Nodes.Contains(nodeNo)).ToList();

        /// <summary>Pręty o danym przekroju (początek albo koniec).</summary>
        public IList<Rf5Member> MembersWithSection(int sectionNo)
            => Members.Values.Where(m => m.CrossSectionStart == sectionNo || m.CrossSectionEnd == sectionNo).ToList();
    }
}
