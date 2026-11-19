using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace LokalneAI.Harness.Rfem
{
    /// <summary>
    /// Czyta tabele modelu ze strumienia InputData pliku .rf5 (RFEM 5.2x). Format jest rozpoznany po porównaniu modeli biura,
    /// nie z dokumentacji: każdy obiekt (węzeł, linia, pręt) to rekord zakończony znacznikiem <c>OwnerParamGroup</c>, a tabele
    /// przekrojów, materiałów, przypadków i kombinacji mają własne stałe układy. Rozpoznawanie jest zachowawcze — rekord,
    /// który nie pasuje do żadnego układu, jest pomijany, a liczby rekordów trafiają do <see cref="Rf5Model.Warnings"/>.
    /// </summary>
    public static class Rf5Parser
    {
        private static readonly byte[] Key = Encoding.ASCII.GetBytes("\x0fOwnerParamGroup");
        private static readonly byte[] CurveKey = Encoding.ASCII.GetBytes("\nOwnerCurve");
        private static readonly byte[] Tail14 = { 0, 0, 0, 0, 4, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
        private static readonly byte[] Tail9 = { 0, 0, 0, 0, 4, 0, 0, 0, 0 };
        private static readonly byte[] Numbers = Encoding.ASCII.GetBytes("Numbers");
        private static readonly byte[] SectionKey = { 0x0c, 0, 0, 0, 1, 1, 7, (byte)'N', (byte)'u', (byte)'m', (byte)'b', (byte)'e', (byte)'r', (byte)'s', 0, 0, 1, 0, 0, 0 };
        private static readonly byte[] NameId = Encoding.ASCII.GetBytes("NameID|");
        private static readonly byte[] Flag1 = { 1, 0, 0, 0 };
        private static readonly Regex ExpressionPattern = new Regex(@"^[0-9A-Za-z .*+^()\-/]+$", RegexOptions.Compiled);

        private static readonly HashSet<string> MemberTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            "Beam", "BeamRigid", "Truss", "TrussN", "Tension", "Compression", "Cable", "Rib", "Buckling", "Definable",
            "Coupling", "Spring", "Dummy", "Null", "Damper", "Pipe", "CouplingRigidRigid", "CouplingRigidHinge", "CouplingHingeRigid", "CouplingHingeHinge",
        };

        /// <summary>Znaki 0x80–0xFF strony kodowej Windows-1250 (RFEM zapisuje nazwy w stronie kodowej systemu; polskie modele = 1250).</summary>
        private const string Cp1250High =
            "€�‚�„…†‡�‰Š‹ŚŤŽŹ�‘’“”•–—�™š›śťžź" +
            " ˇ˘Ł¤Ą¦§¨©Ş«¬­®Ż°±˛ł´µ¶·¸ąş»Ľ˝ľż" +
            "ŔÁÂĂÄĹĆÇČÉĘËĚÍÎĎĐŃŇÓÔŐÖ×ŘŮÚŰÜÝŢß" +
            "ŕáâăäĺćçčéęëěíîďđńňóôőö÷řůúűüýţ˙";

        /// <summary>Czyta model ze strumienia InputData.</summary>
        public static Rf5Model Parse(byte[] data)
        {
            var model = new Rf5Model();
            if (data == null || data.Length < 64)
            {
                model.Warnings.Add("strumień modelu jest pusty");
                return model;
            }

            var unknownRecords = ScanObjects(data, model);
            ScanCrossSections(data, model);
            ScanMaterials(data, model);
            ScanLoadCases(data, model);
            ScanCombinations(data, model);

            if (unknownRecords > 0)
            {
                model.Warnings.Add(unknownRecords + " rekordów obiektów o nierozpoznanym układzie (np. powierzchnie, podpory, przeguby — ich nie czytamy)");
            }

            if (model.Members.Count > 0 && model.CrossSections.Count == 0)
            {
                model.Warnings.Add("nie rozpoznano tabeli przekrojów");
            }

            foreach (var member in model.Members.Values)
            {
                if (member.CrossSectionStart > 0 && !model.CrossSections.ContainsKey(member.CrossSectionStart))
                {
                    model.Warnings.Add("pręt " + member.No + " wskazuje przekrój " + member.CrossSectionStart + ", którego nie ma w tabeli");
                    break;
                }
            }

            return model;
        }

        /// <summary>Wersja RFEM ze strumienia History.bin (np. „5.27.01.160034”) albo pusta.</summary>
        public static string ReadVersion(byte[] history)
        {
            if (history == null)
            {
                return "";
            }

            var marker = Encoding.ASCII.GetBytes("RFEM\x0e");
            var at = IndexOf(history, marker, 0);
            if (at < 0)
            {
                return "";
            }

            var start = at + marker.Length;
            var end = start;
            while (end < history.Length && (history[end] == (byte)'.' || (history[end] >= (byte)'0' && history[end] <= (byte)'9')))
            {
                end++;
            }

            return Encoding.ASCII.GetString(history, start, end - start);
        }

        /// <summary>Tekst w stronie kodowej 1250 (nazwy w pliku RFEM).</summary>
        public static string DecodeText(byte[] data, int offset, int length)
        {
            if (offset < 0 || length <= 0 || offset + length > data.Length)
            {
                return "";
            }

            var chars = new char[length];
            for (var i = 0; i < length; i++)
            {
                var b = data[offset + i];
                chars[i] = b < 0x80 ? (char)b : Cp1250High[b - 0x80];
            }

            return new string(chars).Trim();
        }

        private static int ScanObjects(byte[] data, Rf5Model model)
        {
            // Zwykłe obiekty kończy znacznik OwnerParamGroup; węzły leżące na liniach (krzywych) — OwnerCurve. Ciało zaczyna się tuż za znacznikiem.
            return ScanObjects(data, model, Key) + ScanObjects(data, model, CurveKey);
        }

        private static int ScanObjects(byte[] data, Rf5Model model, byte[] key)
        {
            var unknown = 0;
            var at = 0;
            while ((at = IndexOf(data, key, at)) >= 0)
            {
                var p = at;
                at += key.Length;
                var q = p - 14;
                if (q < 20 || !Match(data, q, Tail14))
                {
                    continue;
                }

                var r = q;
                while (r > 0 && data[r - 1] == 1)
                {
                    r--;
                }

                // Nagłówek obiektu: [i32 numer][tekst 1][tekst 2][00×6][01×n] — bez tekstów to numer i 8 zer (patrz ReadHeader).
                var header = ReadHeader(data, r);
                if (header == null)
                {
                    continue;
                }

                var a = p + key.Length;
                if (a + 96 > data.Length)
                {
                    continue;
                }

                if (TryMember(data, a, header, model) || TryLine(data, a, header, model) || TryNode(data, a, header, model))
                {
                    continue;
                }

                unknown++;
            }

            return unknown;
        }

        private static bool TryMember(byte[] data, int a, ObjectHeader header, Rf5Model model)
        {
            var no = header.No;
            var comment = header.Comment;
            // [00][f4 obrót][00×5][i32 linia][len][typ: Beam, BeamRigid, TrussN…][bloki Numbers: przekroje, drugi zestaw]
            if (data[a] != 0 || !IsZero(data, a + 5, 5))
            {
                return false;
            }

            var typeLength = data[a + 14];
            if (typeLength < 2 || typeLength > 24 || a + 15 + typeLength > data.Length)
            {
                return false;
            }

            var type = Encoding.ASCII.GetString(data, a + 15, typeLength);
            if (!type.All(c => c >= 'A' && c <= 'Z' || c >= 'a' && c <= 'z'))
            {
                return false;
            }

            if (!MemberTypes.Contains(type) && !type.StartsWith("Beam", StringComparison.Ordinal) && !type.StartsWith("Truss", StringComparison.Ordinal))
            {
                return false;
            }

            var line = BitConverter.ToInt32(data, a + 10);
            if (line <= 0 || line > 10_000_000)
            {
                return false;
            }

            var member = new Rf5Member
            {
                No = no,
                LineNo = line,
                Type = type,
                Rotation = BitConverter.ToSingle(data, a + 1),
                Comment = comment,
                TeklaGuid = header.TeklaGuid,
            };

            var q = a + 15 + typeLength;
            var lists = new List<int[]>();
            for (var k = 0; k < 3; k++)
            {
                var list = ReadNumbers(data, q, out var consumed);
                if (list == null)
                {
                    break;
                }

                lists.Add(list);
                q += consumed;
            }

            if (lists.Count > 0 && lists[0].Length > 0)
            {
                member.CrossSectionStart = lists[0][0];
                member.CrossSectionEnd = lists[0].Length > 1 && lists[0][1] != lists[0][0] ? lists[0][1] : 0;
            }

            if (lists.Count > 1)
            {
                member.SecondList = lists[1].ToList();
            }

            if (!model.Members.ContainsKey(no))
            {
                model.Members[no] = member;
            }

            return true;
        }

        private static bool TryLine(byte[] data, int a, ObjectHeader header, Rf5Model model)
        {
            // zera (zwykle 10), potem blok Numbers z węzłami; odmiana z kątem: [00][f4 kąt][00×5][Numbers] (np. π/2 przy liniach z obróconym układem)
            var z = 0;
            while (z < 16 && data[a + z] == 0)
            {
                z++;
            }

            var nodes = ReadNumbers(data, a + z, out _);
            float rotation = 0;
            if (nodes == null && data[a] == 0 && IsZero(data, a + 5, 5))
            {
                nodes = ReadNumbers(data, a + 10, out _);
                rotation = BitConverter.ToSingle(data, a + 1);
            }

            if (nodes == null || nodes.Length < 2 || nodes.Any(n => n <= 0) || !Finite(rotation))
            {
                return false;
            }

            if (!model.Lines.ContainsKey(header.No))
            {
                model.Lines[header.No] = new Rf5Line { No = header.No, Nodes = nodes.ToList(), Comment = header.Comment, Rotation = rotation };
            }

            return true;
        }

        private static bool TryNode(byte[] data, int a, ObjectHeader header, Rf5Model model)
        {
            var no = header.No;
            var comment = header.Comment;
            // [00×8][f8 x][f8 y][f8 z][len][nazwa układu, np. Standard]
            if (!IsZero(data, a, 8))
            {
                return false;
            }

            var x = BitConverter.ToDouble(data, a + 8);
            var y = BitConverter.ToDouble(data, a + 16);
            var z = BitConverter.ToDouble(data, a + 24);
            var nameLength = data[a + 32];
            if (nameLength < 1 || nameLength > 30 || !Finite(x) || !Finite(y) || !Finite(z))
            {
                return false;
            }

            if (!model.Nodes.ContainsKey(no))
            {
                model.Nodes[no] = new Rf5Node { No = no, X = x, Y = y, Z = z, CoordinateSystem = DecodeText(data, a + 33, nameLength), Comment = comment };
            }

            return true;
        }

        /// <summary>Blok listy liczb: <c>[u32 długość][01 01 07 "Numbers" 00][u8 bajtów][i32…]</c>; null, gdy to nie blok.</summary>
        private static int[] ReadNumbers(byte[] data, int q, out int consumed)
        {
            consumed = 0;
            if (q + 16 > data.Length)
            {
                return null;
            }

            var length = BitConverter.ToUInt32(data, q);
            if (length < 12 || length > 4096 || data[q + 4] != 1 || data[q + 5] != 1 || !Match(data, q + 7, Numbers))
            {
                return null;
            }

            var count = data[q + 15] / 4;
            if (12 + 4 * count != length || q + 16 + 4 * count > data.Length)
            {
                return null;
            }

            var values = new int[count];
            for (var i = 0; i < count; i++)
            {
                values[i] = BitConverter.ToInt32(data, q + 16 + 4 * i);
            }

            consumed = 4 + (int)length;
            return values;
        }

        private static void ScanCrossSections(byte[] data, Rf5Model model)
        {
            // …[i32 nr][00×16+][01×14][00 00 00 00 04 00 00 00 00][f8 It][f4][f8 Iy][f8 Iz][f8 A][f8 Ay][f8 Az][f4×5][00×8][i32 materiał] (93 B od ogona do bajtu długości)
            // [len][opis, np. „HE A 160 | DIN 1025-3:1994”][f4 wymiar 1][f4 wymiar 2][ff ff ff ff][blok Numbers 0c…][01 00 00 00][len][TextID, np. „HE-A 160”]
            var at = 0;
            var sequential = 0;
            while ((at = IndexOf(data, SectionKey, at)) >= 0)
            {
                var m = at;
                at += SectionKey.Length;
                if (m < 200)
                {
                    continue;
                }

                var found = -1;
                for (var length = 1; length <= 100 && m - 13 - length - 94 >= 0; length++)
                {
                    var lenPos = m - 13 - length;
                    if (data[lenPos] == length && Match(data, lenPos - 93, Tail9) && data[lenPos - 94] == 1)
                    {
                        found = lenPos;
                        break;
                    }
                }

                if (found < 0)
                {
                    continue;
                }

                var lenPos2 = found;
                var descriptionLength = data[lenPos2];
                var t = lenPos2 - 84;
                var section = new Rf5CrossSection
                {
                    Description = DecodeText(data, lenPos2 + 1, descriptionLength),
                    It = BitConverter.ToDouble(data, t),
                    Iy = BitConverter.ToDouble(data, t + 12),
                    Iz = BitConverter.ToDouble(data, t + 20),
                    Area = BitConverter.ToDouble(data, t + 28),
                    MaterialNo = BitConverter.ToInt32(data, lenPos2 - 4),
                    Dimension1 = BitConverter.ToSingle(data, m - 12),
                    Dimension2 = BitConverter.ToSingle(data, m - 8),
                };

                var textLength = m + SectionKey.Length < data.Length ? data[m + SectionKey.Length] : 0;
                if (textLength > 0 && textLength <= 80)
                {
                    section.TextId = DecodeText(data, m + SectionKey.Length + 1, textLength);
                }

                var ones = lenPos2 - 94;
                while (ones > 0 && data[ones - 1] == 1)
                {
                    ones--;
                }

                // Przed ciągiem jedynek: [i32 numer][zera][len][komentarz][00×7]. Numer czytamy od pierwszego niezerowego bajtu
                // idąc wstecz od komentarza (starsze bajty numeru też są zerami) — zawodzi tylko dla numerów podzielnych przez 256.
                var no = 0;
                if (ones >= 20 && IsZero(data, ones - 7, 7))
                {
                    var commentLength = CommentLength(data, ones - 8);
                    var back = ones - 8 - commentLength;   // bajt długości komentarza
                    if (commentLength >= 0 && back > 4)
                    {
                        section.Comment = commentLength > 0 ? DecodeText(data, back + 1, commentLength) : "";
                        var low = back - 1;
                        while (low > 0 && data[low] == 0 && back - low < 24)
                        {
                            low--;
                        }

                        no = low > 0 && IsZero(data, low + 1, 3) ? BitConverter.ToInt32(data, low) : 0;
                    }
                }
                sequential++;
                if (no <= 0 || no > 100_000)
                {
                    no = sequential;
                    model.Warnings.Add("przekrój „" + section.Description + "” bez czytelnego numeru — przyjęto kolejny (" + no + ")");
                }

                section.No = no;
                if (!Finite(section.Area) || section.Area < 0 || section.Area > 100)
                {
                    section.Area = 0;
                    section.Iy = section.Iz = section.It = 0;
                    model.Warnings.Add("przekrój " + no + " „" + section.Description + "”: nieczytelne charakterystyki");
                }

                if (!model.CrossSections.ContainsKey(no))
                {
                    model.CrossSections[no] = section;
                }
            }
        }

        private static readonly byte[] MaterialSignature = { 1, 1, 1, 1, 1, 1, 1, 1, 0, 1, 0, 0, 0, 0, 4, 0, 0, 0, 0 };

        private static void ScanMaterials(byte[] data, Rf5Model model)
        {
            // [i32 nr][len][komentarz][00×7][01×8][00 01 00 00 00 00 04 00 00 00 00][len][opis] … potem (materiały z biblioteki)
            // [len][NameID|…@TypeID|STEEL@NormID|…][len][norma] i dalej [f8 E][f8 G][f4][f4 γ][f4 αT]. Materiały użytkownika
            // nie mają NameID, więc E i G szukamy jako pary f8 z E/G ≈ 2–3,2 w następnych 320 bajtach.
            var at = 0;
            var sequential = 0;
            while ((at = IndexOf(data, MaterialSignature, at)) >= 0)
            {
                var signatureAt = at;
                at += MaterialSignature.Length;
                var lenPos = signatureAt + MaterialSignature.Length;
                if (signatureAt < 12 || lenPos + 1 >= data.Length || !IsZero(data, signatureAt - 7, 7))
                {
                    continue;
                }

                var commentLength = CommentLength(data, signatureAt - 8);
                if (commentLength < 0 || signatureAt - 12 - commentLength < 0)
                {
                    continue;
                }

                int nameLength = data[lenPos];
                if (nameLength < 1 || nameLength > 60 || lenPos + 1 + nameLength > data.Length || !Printable(data, lenPos + 1, nameLength))
                {
                    continue;
                }

                var material = new Rf5Material
                {
                    Description = DecodeText(data, lenPos + 1, nameLength),
                    Comment = commentLength > 0 ? DecodeText(data, signatureAt - 7 - commentLength, commentLength) : "",
                };
                var afterName = lenPos + 1 + nameLength;
                var limit = Math.Min(data.Length, afterName + 320);

                var nameId = IndexOf(data, NameId, afterName);
                if (nameId >= 0 && nameId < limit)
                {
                    int idLength = data[nameId - 1];
                    var id = DecodeText(data, nameId, Math.Min(idLength, data.Length - nameId));
                    var typeAt = id.IndexOf("@TypeID|", StringComparison.Ordinal);
                    if (typeAt >= 0)
                    {
                        var rest = id.Substring(typeAt + 8);
                        var end = rest.IndexOf('@');
                        material.Kind = end >= 0 ? rest.Substring(0, end) : rest;
                    }

                    var normLenPos = nameId + idLength;
                    if (normLenPos < data.Length)
                    {
                        int normLength = data[normLenPos];
                        if (normLength > 0 && normLength <= 60 && normLenPos + 1 + normLength <= data.Length && Printable(data, normLenPos + 1, normLength))
                        {
                            material.Norm = DecodeText(data, normLenPos + 1, normLength);
                        }
                    }
                }

                for (var e = afterName; e + 28 <= limit; e++)
                {
                    var youngs = BitConverter.ToDouble(data, e);
                    var shear = BitConverter.ToDouble(data, e + 8);
                    if (Finite(youngs) && Finite(shear) && youngs > 1e9 && youngs < 1e12 && shear > 1e8 && shear < 1e12 && youngs / shear > 2.0 && youngs / shear < 3.2)
                    {
                        material.E = youngs;
                        material.G = shear;
                        var gamma = BitConverter.ToSingle(data, e + 20);
                        var alpha = BitConverter.ToSingle(data, e + 24);
                        if (Finite(gamma) && gamma > 0 && gamma < 1e6)
                        {
                            material.SpecificWeight = gamma;
                        }

                        if (Finite(alpha) && alpha > 0 && alpha < 1e-3)
                        {
                            material.ThermalExpansion = alpha;
                        }

                        break;
                    }
                }

                var no = BitConverter.ToInt32(data, signatureAt - 12 - commentLength);
                sequential++;
                if (no <= 0 || no > 100_000)
                {
                    no = sequential;
                }

                material.No = no;
                if (!model.Materials.ContainsKey(no))
                {
                    model.Materials[no] = material;
                }
            }
        }

        private static void ScanLoadCases(byte[] data, Rf5Model model)
        {
            // [i32 nr][01 00 00 00][00 00 00 00][len][opis][f4 współczynnik ciężaru własnego]…
            var seen = new HashSet<int>();
            for (var i = 0; i + 18 <= data.Length; i++)
            {
                if (data[i + 4] != 1 || data[i + 5] != 0 || data[i + 6] != 0 || data[i + 7] != 0 || !IsZero(data, i + 8, 4))
                {
                    continue;
                }

                var length = data[i + 12];
                if (length < 1 || length > 64 || i + 13 + length + 4 > data.Length)
                {
                    continue;
                }

                var no = BitConverter.ToInt32(data, i);
                if (no <= 0 || no > 9999 || !Printable(data, i + 13, length))
                {
                    continue;
                }

                var factor = BitConverter.ToSingle(data, i + 13 + length);
                if (!Finite(factor) || factor < 0 || factor > 10)
                {
                    continue;
                }

                if (seen.Add(no))
                {
                    model.LoadCases.Add(new Rf5LoadCase { No = no, Description = DecodeText(data, i + 13, length), SelfWeightFactor = factor });
                }
            }
        }

        private static void ScanCombinations(byte[] data, Rf5Model model)
        {
            // kombinacja obciążeń: [i32 nr][01 00 00 00][len][opis][00][len][wyrażenie, np. 1.35*LF1 + 1.35*LF3 + LF21]
            // kombinacja wyników / akcja: [i32 nr][len][opis][len][wyrażenie, np. LK1 o LK2]
            var seen = new HashSet<int>();
            var at = 0;
            while ((at = IndexOf(data, Flag1, at)) >= 0)
            {
                var p = at;
                at += 1;
                if (p < 4 || p + 8 > data.Length)
                {
                    continue;
                }

                var descriptionLength = data[p + 4];
                if (descriptionLength > 60)
                {
                    continue;
                }

                var q = p + 5 + descriptionLength;
                if (q + 2 > data.Length || data[q] != 0)
                {
                    continue;
                }

                var expressionLength = data[q + 1];
                if (expressionLength < 3 || expressionLength > 200 || q + 2 + expressionLength > data.Length)
                {
                    continue;
                }

                var expression = Encoding.ASCII.GetString(data, q + 2, expressionLength);
                if (!IsExpression(expression) || (descriptionLength > 0 && !Printable(data, p + 5, descriptionLength)))
                {
                    continue;
                }

                var no = BitConverter.ToInt32(data, p - 4);
                if (no <= 0 || no > 9999)
                {
                    continue;
                }

                if (seen.Add(no))
                {
                    model.Combinations.Add(new Rf5Combination { No = no, Description = DecodeText(data, p + 5, descriptionLength), Expression = expression.Trim() });
                }
            }

            // kombinacje wyników (LK…): układ bez flagi
            var seenResult = new HashSet<int>();
            var lk = Encoding.ASCII.GetBytes("LK");
            at = 0;
            while ((at = IndexOf(data, lk, at)) >= 0)
            {
                var s = at;
                at += 2;
                for (var back = 0; back <= 8; back++)
                {
                    var start = s - back;
                    if (start < 6)
                    {
                        break;
                    }

                    var expressionLength = data[start - 1];
                    if (expressionLength < 3 || expressionLength > 200 || start + expressionLength > data.Length)
                    {
                        continue;
                    }

                    var expression = Encoding.ASCII.GetString(data, start, expressionLength);
                    if (!IsExpression(expression) || !expression.Contains("LK"))
                    {
                        continue;
                    }

                    var found = false;
                    for (var descriptionLength = 0; descriptionLength <= 60 && start - 2 - descriptionLength - 4 >= 0; descriptionLength++)
                    {
                        var descLenPos = start - 2 - descriptionLength;
                        if (data[descLenPos] != descriptionLength || (descriptionLength > 0 && !Printable(data, descLenPos + 1, descriptionLength)))
                        {
                            continue;
                        }

                        var no = BitConverter.ToInt32(data, descLenPos - 4);
                        if (no <= 0 || no > 9999)
                        {
                            continue;
                        }

                        if (seenResult.Add(no))
                        {
                            model.ResultCombinations.Add(new Rf5Combination { No = no, Description = DecodeText(data, descLenPos + 1, descriptionLength), Expression = expression.Trim() });
                        }

                        found = true;
                        break;
                    }

                    if (found)
                    {
                        at = start + expressionLength;
                        break;
                    }
                }
            }
        }

        /// <summary>Nagłówek obiektu przed ciągiem jedynek: numer, komentarz i GUID z metadanych Tekli.</summary>
        private sealed class ObjectHeader
        {
            public int No;
            public string Comment = "";
            public string TeklaGuid = "";
        }

        private static readonly Regex TeklaGuidPattern = new Regex("<PhysicalID>([0-9A-Fa-f-]{36})</PhysicalID>", RegexOptions.Compiled);

        /// <summary>Największa długość tekstu w nagłówku (metadane Tekli mają 266 znaków).</summary>
        private const int MaxHeaderText = 4096;

        /// <summary>
        /// Nagłówek obiektu czytany od końca: <c>[i32 numer][tekst 1][tekst 2][00×6]</c> przed ciągiem jedynek zaczynającym się
        /// na <paramref name="onesStart"/>. Tekst to <c>[u8 len][znaki]</c> albo dla dłuższych <c>[FF][u16 len][znaki]</c>; pusty = sam bajt 0
        /// (bez tekstów: numer i 8 zer). Tekst 1 to komentarz z tabeli. Eksport z Tekli wpisuje w tekst 1 albo w tekst 2 266-znakowy
        /// <c>&lt;TeklaItemMetadata&gt;</c> z GUID-em elementu (PhysicalID) — gdy parser znał tylko krótki komentarz z jednym bajtem
        /// długości, gubił prawie wszystkie pręty takiego modelu albo brał numer z wnętrza tekstu. Null, gdy układ nie pasuje.
        /// </summary>
        private static ObjectHeader ReadHeader(byte[] data, int onesStart)
        {
            if (onesStart < 14 || !IsZero(data, onesStart - 6, 6))
            {
                return null;
            }

            if (!TryTextEndingAt(data, onesStart - 7, out var start2, out var text2) || !TryTextEndingAt(data, start2 - 1, out var start1, out var text1) || start1 < 4)
            {
                return null;
            }

            var header = new ObjectHeader { No = BitConverter.ToInt32(data, start1 - 4) };
            foreach (var text in new[] { text1, text2 })
            {
                if (text.StartsWith("<TeklaItemMetadata", StringComparison.Ordinal))
                {
                    var guid = TeklaGuidPattern.Match(text);
                    header.TeklaGuid = guid.Success ? guid.Groups[1].Value.ToLowerInvariant() : "";
                }
                else if (text.Length > 0 && header.Comment.Length == 0)
                {
                    header.Comment = text;
                }
            }

            return header;
        }

        /// <summary>
        /// Tekst, którego ostatni bajt leży na <paramref name="last"/>: <c>[u8 len]</c> do 254 znaków albo <c>[FF][u16 len]</c>; pusty = sam bajt 0.
        /// Najpierw forma długa, bo jej warunek jest mocny (FF i dokładna długość), a w długim tekście bajt równy odległości trafia się przypadkiem.
        /// </summary>
        private static bool TryTextEndingAt(byte[] data, int last, out int start, out string text)
        {
            start = last;
            text = "";
            if (last < 0)
            {
                return false;
            }

            if (data[last] == 0)
            {
                return true;
            }

            for (var length = 1; length <= MaxHeaderText && last - length - 2 >= 4; length++)
            {
                var p = last - length - 2;
                if (data[p] == 0xFF && BitConverter.ToUInt16(data, p + 1) == length && Printable(data, p + 3, length))
                {
                    start = p;
                    text = DecodeText(data, p + 3, length);
                    return true;
                }
            }

            for (var length = 1; length <= 254 && last - length >= 4; length++)
            {
                if (data[last - length] == length && Printable(data, last - length + 1, length))
                {
                    start = last - length;
                    text = DecodeText(data, last - length + 1, length);
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Długość komentarza w nagłówku tabeli przekrojów i materiałów (tylko krótka forma <c>[u8 len]</c>): <paramref name="last"/> to
        /// ostatni bajt przed siedmioma zerami. Bez komentarza jest tam 0 (bajt długości); z komentarzem — ostatni znak, a bajt długości L
        /// leży L bajtów wcześniej. −1 = nie pasuje. Obiekty (węzły, linie, pręty) czyta <see cref="ReadHeader"/>.
        /// </summary>
        private static int CommentLength(byte[] data, int last)
        {
            if (last < 0)
            {
                return -1;
            }

            if (data[last] == 0)
            {
                return 0;
            }

            for (var length = 1; length <= 120 && last - length >= 4; length++)
            {
                if (data[last - length] == length && Printable(data, last - length + 1, length))
                {
                    return length;
                }
            }

            return -1;
        }

        private static bool IsExpression(string text)
            => ExpressionPattern.IsMatch(text) && (text.Contains("LF") || text.Contains("LK")) && text.Any(char.IsDigit);

        private static bool Printable(byte[] data, int offset, int length)
        {
            for (var i = 0; i < length; i++)
            {
                if (data[offset + i] < 0x20 || data[offset + i] == 0x7f)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value) && Math.Abs(value) < 1e30;

        private static bool IsZero(byte[] data, int offset, int count)
        {
            if (offset < 0 || offset + count > data.Length)
            {
                return false;
            }

            for (var i = 0; i < count; i++)
            {
                if (data[offset + i] != 0)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool AllOnes(byte[] data, int offset, int count)
        {
            if (offset < 0 || offset + count > data.Length)
            {
                return false;
            }

            for (var i = 0; i < count; i++)
            {
                if (data[offset + i] != 1)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool Match(byte[] data, int offset, byte[] pattern)
        {
            if (offset < 0 || offset + pattern.Length > data.Length)
            {
                return false;
            }

            for (var i = 0; i < pattern.Length; i++)
            {
                if (data[offset + i] != pattern[i])
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Pierwsze wystąpienie wzorca od pozycji; −1 = brak.</summary>
        internal static int IndexOf(byte[] data, byte[] pattern, int from)
        {
            if (pattern.Length == 0 || data.Length < pattern.Length)
            {
                return -1;
            }

            var first = pattern[0];
            var last = data.Length - pattern.Length;
            for (var i = Math.Max(0, from); i <= last; i++)
            {
                if (data[i] != first)
                {
                    continue;
                }

                var ok = true;
                for (var j = 1; j < pattern.Length; j++)
                {
                    if (data[i + j] != pattern[j])
                    {
                        ok = false;
                        break;
                    }
                }

                if (ok)
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
