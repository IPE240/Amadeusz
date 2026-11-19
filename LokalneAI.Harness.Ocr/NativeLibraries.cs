using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Tesseract;

namespace LokalneAI.Harness.Ocr
{
    /// <summary>
    /// Gdzie Tesseract ma szukać swoich bibliotek natywnych (<c>x64\tesseract50.dll</c>, <c>x64\leptonica-1.82.0.dll</c>).
    /// Tesseract szuka ich obok pliku Tesseract.dll (<c>Assembly.Location</c>), a w programie jednoplikowym
    /// (<c>PublishSingleFile</c>, np. Amadeusz.exe) ta ścieżka jest pusta i ładowanie kończyło się
    /// <c>ArgumentNullException</c> w <c>TargetInvocationException</c>. Biblioteki rozpakowane z pliku programu leżą
    /// w <c>%TEMP%\.net\&lt;program&gt;\&lt;skrót&gt;\</c> — host podaje ten folder w <c>NATIVE_DLL_SEARCH_DIRECTORIES</c>.
    /// </summary>
    internal static class NativeLibraries
    {
        private static readonly object Gate = new object();
        private static bool _prepared;

        /// <summary>Folder ustawiony w <c>TesseractEnviornment.CustomSearchPath</c>; null = Tesseract szuka sam.</summary>
        public static string SearchPath { get; private set; }

        /// <summary>Ustawia folder bibliotek Tesseracta, jeśli go znajdzie (raz na proces).</summary>
        public static void Prepare()
        {
            lock (Gate)
            {
                if (_prepared)
                {
                    return;
                }

                _prepared = true;
                if (!string.IsNullOrEmpty(TesseractEnviornment.CustomSearchPath))
                {
                    SearchPath = TesseractEnviornment.CustomSearchPath; // ustawił gospodarz
                    return;
                }

                var platform = Environment.Is64BitProcess ? "x64" : "x86";
                SearchPath = Candidates().FirstOrDefault(folder => File.Exists(Path.Combine(folder, platform, "tesseract50.dll")));
                if (SearchPath != null)
                {
                    TesseractEnviornment.CustomSearchPath = SearchPath;
                }
            }
        }

        /// <summary>Foldery do sprawdzenia: rozpakowane biblioteki programu jednoplikowego, folder programu, folder Tesseract.dll.</summary>
        internal static IEnumerable<string> Candidates()
        {
            if (AppContext.GetData("NATIVE_DLL_SEARCH_DIRECTORIES") is string native)
            {
                foreach (var folder in native.Split(new[] { Path.PathSeparator, ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    yield return folder.Trim();
                }
            }

            yield return AppDomain.CurrentDomain.BaseDirectory;
            string location = null;
            try
            {
                location = typeof(Pix).Assembly.Location;
            }
            catch (NotSupportedException)
            {
                // Zestaw w pamięci — brak ścieżki.
            }

            if (!string.IsNullOrEmpty(location))
            {
                yield return Path.GetDirectoryName(location);
            }
        }

        /// <summary>Czy wyjątek to nieudane ładowanie bibliotek natywnych (a nie zły obraz albo brak danych języka).</summary>
        public static bool IsLoadFailure(Exception e)
            => e is TargetInvocationException || e is TypeInitializationException || e is DllNotFoundException
               || e is BadImageFormatException || e is EntryPointNotFoundException;

        /// <summary>Komunikat dla człowieka i modelu, gdy biblioteki Tesseracta się nie załadowały.</summary>
        public static OcrException LoadError(Exception e)
        {
            var inner = e;
            while ((inner is TargetInvocationException || inner is TypeInitializationException) && inner.InnerException != null)
            {
                inner = inner.InnerException;
            }

            return new OcrException("BŁĄD: nie udało się uruchomić Tesseracta (biblioteki x64\\tesseract50.dll i leptonica"
                + (SearchPath == null ? ", nie znalazłem ich folderu" : " w " + SearchPath) + "): " + inner.Message, e);
        }
    }
}
