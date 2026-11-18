using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace LokalneAI.Harness.Rfem
{
    /// <summary>
    /// Siatka MES z folderu <c>Mesh</c> (tekstowe pliki solvera, zapisane po obliczeniu): <c>.MTO</c> daje kolejność prętów
    /// w plikach wyników, <c>.geo</c> węzły, linie i przypisanie pręt → linia (zapas, gdy tabel modelu nie da się odczytać).
    /// </summary>
    public sealed class Rf5Mesh
    {
        /// <summary>Numery prętów w kolejności, w jakiej solver zapisuje ich wyniki (<c>.MTO</c>).</summary>
        public List<int> MemberOrder { get; } = new List<int>();

        /// <summary>Pręt → linia z <c>.geo</c> (wiersze MACR).</summary>
        public Dictionary<int, int> MemberLine { get; } = new Dictionary<int, int>();

        /// <summary>Węzły z <c>.geo</c>: numer → (x, y, z) [m].</summary>
        public Dictionary<int, double[]> Nodes { get; } = new Dictionary<int, double[]>();

        /// <summary>Linie z <c>.geo</c>: numer → węzły.</summary>
        public Dictionary<int, int[]> Lines { get; } = new Dictionary<int, int[]>();

        /// <summary>Czy w pliku była siatka.</summary>
        public bool HasMesh => MemberOrder.Count > 0 || Nodes.Count > 0;

        /// <summary>Czyta siatkę z pliku (brak folderu Mesh = pusta siatka).</summary>
        public static Rf5Mesh Read(Rf5Archive archive)
        {
            var mesh = new Rf5Mesh();
            if (archive.Exists("Mesh/.MTO"))
            {
                mesh.MemberOrder.AddRange(ParseMto(archive.ReadText("Mesh/.MTO")));
            }

            if (archive.Exists("Mesh/.geo"))
            {
                ParseGeo(archive.ReadText("Mesh/.geo"), mesh);
            }

            return mesh;
        }

        /// <summary>Kolejność prętów z <c>.MTO</c> (kolumny: pręt, kolejny, linia, pierwszy element, ostatni element, typ; wiersz z prętem 0 kończy).</summary>
        public static List<int> ParseMto(string text)
        {
            var order = new List<int>();
            foreach (var line in (text ?? "").Split('\n'))
            {
                var parts = line.Split(new[] { ' ', '\t', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 3 || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var member))
                {
                    continue;
                }

                if (member <= 0)
                {
                    break;
                }

                order.Add(member);
            }

            return order;
        }

        /// <summary>Węzły, linie i pręty z <c>.geo</c> (<c>NODE nr x y z</c>, <c>LINE nr STRL|POLY węzły…</c>, <c>MACR pręt BEAM linia</c>).</summary>
        public static void ParseGeo(string text, Rf5Mesh mesh)
        {
            foreach (var raw in (text ?? "").Split('\n'))
            {
                var parts = raw.Split(new[] { ' ', '\t', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 3)
                {
                    continue;
                }

                switch (parts[0])
                {
                    case "NODE":
                        if (parts.Length >= 5 && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var node)
                            && TryDouble(parts[2], out var x) && TryDouble(parts[3], out var y) && TryDouble(parts[4], out var z))
                        {
                            mesh.Nodes[node] = new[] { x, y, z };
                        }

                        break;

                    case "LINE":
                        if (parts.Length >= 5 && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var lineNo))
                        {
                            var nodes = new List<int>();
                            for (var i = 3; i < parts.Length; i++)
                            {
                                if (int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
                                {
                                    nodes.Add(n);
                                }
                            }

                            if (nodes.Count >= 2)
                            {
                                mesh.Lines[lineNo] = nodes.ToArray();
                            }
                        }

                        break;

                    case "MACR":
                        if (parts.Length >= 4 && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var member)
                            && int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var memberLine))
                        {
                            mesh.MemberLine[member] = memberLine;
                        }

                        break;
                }
            }
        }

        /// <summary>Długość pręta z siatki [m] albo null.</summary>
        public double? MemberLength(int memberNo)
        {
            if (!MemberLine.TryGetValue(memberNo, out var lineNo) || !Lines.TryGetValue(lineNo, out var nodes))
            {
                return null;
            }

            double length = 0;
            for (var i = 0; i + 1 < nodes.Length; i++)
            {
                if (!Nodes.TryGetValue(nodes[i], out var a) || !Nodes.TryGetValue(nodes[i + 1], out var b))
                {
                    return null;
                }

                length += Math.Sqrt((a[0] - b[0]) * (a[0] - b[0]) + (a[1] - b[1]) * (a[1] - b[1]) + (a[2] - b[2]) * (a[2] - b[2]));
            }

            return length;
        }

        private static bool TryDouble(string text, out double value)
            => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
