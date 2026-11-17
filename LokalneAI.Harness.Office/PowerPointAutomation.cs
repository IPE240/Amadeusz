using System;
using System.Text;

namespace LokalneAI.Harness.Office
{
    /// <summary>Operacje na prezentacjach PowerPointa (wątek STA).</summary>
    internal static class PowerPointAutomation
    {
        /// <summary>Tekst wszystkich slajdów: numer slajdu, potem teksty kształtów (tytuł pierwszy).</summary>
        public static string Read(OfficeHost host, string path, int maxChars)
        {
            host.Open(path, readOnly: true, visible: false);
            var slides = host.Track(Com.Get(host.Document, "Slides"));
            var count = Com.Int(slides, "Count");
            var text = new StringBuilder("OK ").Append(path).Append(" (slajdów: ").Append(count).Append(')');
            var body = new StringBuilder();
            for (var s = 1; s <= count; s++)
            {
                var slide = Com.Get(slides, "Item", s);
                body.Append("\nSlajd ").Append(s).Append(':');
                var shapes = Com.Get(slide, "Shapes");
                var shapeCount = Com.Int(shapes, "Count");
                for (var i = 1; i <= shapeCount; i++)
                {
                    var shape = Com.Get(shapes, "Item", i);
                    try
                    {
                        if (Com.Int(shape, "HasTextFrame") != 0)
                        {
                            var frame = Com.Get(shape, "TextFrame");
                            if (Com.Int(frame, "HasText") != 0)
                            {
                                var range = Com.Get(frame, "TextRange");
                                var shapeText = Com.Text(range, "Text").Replace("\r", " / ").Replace("\v", " / ").Trim();
                                if (shapeText.Length > 0)
                                {
                                    body.Append("\n  - ").Append(shapeText);
                                }

                                Com.Release(range);
                            }

                            Com.Release(frame);
                        }
                    }
                    finally
                    {
                        Com.Release(shape);
                    }
                }

                var notes = SlideNotes(slide);
                if (notes.Length > 0)
                {
                    body.Append("\n  (notatki: ").Append(notes).Append(')');
                }

                Com.Release(shapes);
                Com.Release(slide);
                if (body.Length > maxChars * 2)
                {
                    body.Append("\n… (dalsze slajdy pominięto)");
                    break;
                }
            }

            host.Finish(keepOpen: false);
            return text + Tools.FileSystemExecutor.Truncate(body.ToString(), maxChars);
        }

        private static string SlideNotes(object slide)
        {
            try
            {
                var notesPage = Com.Get(slide, "NotesPage");
                var shapes = Com.Get(notesPage, "Shapes");
                var count = Com.Int(shapes, "Count");
                var text = "";
                for (var i = 1; i <= count && text.Length == 0; i++)
                {
                    var shape = Com.Get(shapes, "Item", i);
                    // ppPlaceholderBody = 2 na stronie notatek to pole notatek
                    if (Com.Int(shape, "Type") == 14 && Com.Int(Com.Get(shape, "PlaceholderFormat"), "Type") == 2 && Com.Int(shape, "HasTextFrame") != 0)
                    {
                        text = Com.Text(Com.Get(Com.Get(shape, "TextFrame"), "TextRange"), "Text").Replace("\r", " / ").Trim();
                    }

                    Com.Release(shape);
                }

                Com.Release(shapes);
                Com.Release(notesPage);
                return text;
            }
            catch (OfficeException)
            {
                return "";
            }
        }

        /// <summary>
        /// Dodaje slajd z tytułem i tekstem (nowe linie = punkty) na końcu albo w podanym miejscu. Układ „Tytuł i zawartość”
        /// (drugi układ wzorca); gdy go nie ma — pierwszy układ. Nieistniejąca prezentacja powstaje.
        /// </summary>
        public static string AddSlide(OfficeHost host, string path, string title, string text, int position, bool keepOpen)
        {
            var created = !System.IO.File.Exists(path);
            if (created)
            {
                host.CreateNew(path, visible: keepOpen);
            }
            else
            {
                host.Open(path, readOnly: false, visible: keepOpen);
            }

            var slides = host.Track(Com.Get(host.Document, "Slides"));
            var count = Com.Int(slides, "Count");
            var index = position >= 1 && position <= count + 1 ? position : count + 1;
            var master = host.Track(Com.Get(host.Document, "SlideMaster"));
            var layouts = host.Track(Com.Get(master, "CustomLayouts"));
            var layout = host.Track(Com.Get(layouts, "Item", Com.Int(layouts, "Count") >= 2 ? 2 : 1));
            var slide = host.Track(Com.Call(slides, "AddSlide", index, layout));
            var shapes = host.Track(Com.Get(slide, "Shapes"));
            var shapeCount = Com.Int(shapes, "Count");
            var filledTitle = false;
            var filledBody = false;
            for (var i = 1; i <= shapeCount; i++)
            {
                var shape = Com.Get(shapes, "Item", i);
                try
                {
                    if (Com.Int(shape, "HasTextFrame") == 0)
                    {
                        continue;
                    }

                    var range = Com.Get(Com.Get(shape, "TextFrame"), "TextRange");
                    if (!filledTitle)
                    {
                        Com.Set(range, "Text", title ?? "");
                        filledTitle = true;
                    }
                    else if (!filledBody)
                    {
                        Com.Set(range, "Text", (text ?? "").Replace("\r\n", "\n").Replace('\n', '\r').TrimEnd('\r'));
                        filledBody = true;
                    }

                    Com.Release(range);
                }
                finally
                {
                    Com.Release(shape);
                }
            }

            if (!filledBody && !string.IsNullOrWhiteSpace(text))
            {
                // Układ bez pola treści — osobne pole tekstowe pod tytułem.
                var box = Com.Call(shapes, "AddTextbox", 1, 40f, 140f, 880f, 360f); // msoTextOrientationHorizontal
                Com.Set(Com.Get(Com.Get(box, "TextFrame"), "TextRange"), "Text", text.Replace("\r\n", "\n").Replace('\n', '\r'));
                Com.Release(box);
            }

            host.Save();
            host.Finish(keepOpen);
            return "OK " + (created ? "utworzono prezentację " + path + ", " : "") + "dodano slajd " + index + " „" + title + "” (slajdów: " + (count + 1) + ")"
                + (created ? "" : ", zapisano " + path);
        }

        /// <summary>Zapis kopii: PDF (<c>SaveAs</c> z ppSaveAsPDF) albo inny format z rozszerzenia.</summary>
        public static string SaveAs(OfficeHost host, string path, string target)
        {
            host.Open(path, readOnly: true, visible: false);
            Com.Call(host.Document, "SaveCopyAs", target, OfficeHost.PowerPointFormat(target));
            host.Finish(keepOpen: false);
            return "OK zapisano " + target;
        }
    }
}
