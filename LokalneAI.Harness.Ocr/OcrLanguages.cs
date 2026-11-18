using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace LokalneAI.Harness.Ocr
{
    /// <summary>
    /// Dane języków Tesseracta (<c>*.traineddata</c>). Nie leżą w repozytorium (kilka MB na język): pobierane z tessdata_fast
    /// (Apache 2.0) do <see cref="DefaultFolder"/> przy pierwszym użyciu albo przyciskiem w Ustawieniach → OCR.
    /// </summary>
    public static class OcrLanguages
    {
        /// <summary>Skąd pobierać: tessdata_fast — mniejsze i szybsze modele LSTM, wystarczające do druku.</summary>
        public const string SourceUrl = "https://github.com/tesseract-ocr/tessdata_fast/raw/main/";

        /// <summary>Dane do wykrywania orientacji strony (<c>osd</c>), pobierane razem z językami.</summary>
        public const string OrientationData = "osd";

        /// <summary>Języki z nazwami dla ludzi (kod Tesseracta → nazwa).</summary>
        public static readonly IReadOnlyDictionary<string, string> Known = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["pol"] = "polski",
            ["eng"] = "angielski",
            ["deu"] = "niemiecki",
            ["ces"] = "czeski",
            ["slk"] = "słowacki",
            ["ukr"] = "ukraiński",
            ["rus"] = "rosyjski",
            ["fra"] = "francuski",
            ["ita"] = "włoski",
            ["spa"] = "hiszpański",
            ["nld"] = "niderlandzki",
            ["swe"] = "szwedzki",
            ["nor"] = "norweski",
            ["dan"] = "duński",
            ["lit"] = "litewski",
            ["equ"] = "wzory matematyczne",
        };

        /// <summary>Domyślny folder: <c>%LOCALAPPDATA%\LokalneAI\tessdata</c> (obok repozytoriów wiedzy i modeli).</summary>
        public static string DefaultFolder
            => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LokalneAI", "tessdata");

        /// <summary>Kody języków z napisu <c>pol+eng</c>.</summary>
        public static IList<string> Split(string languages)
            => (languages ?? "").Split(new[] { '+', ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim().ToLowerInvariant()).Distinct().ToList();

        /// <summary>Języki (i <c>osd</c>), których plików brakuje w folderze.</summary>
        public static IList<string> Missing(string folder, string languages, bool withOrientation = true)
        {
            var wanted = Split(languages);
            if (withOrientation)
            {
                wanted.Add(OrientationData);
            }

            return wanted.Where(l => !File.Exists(Path.Combine(folder ?? "", l + ".traineddata"))).ToList();
        }

        /// <summary>Języki dostępne w folderze (pliki <c>*.traineddata</c> bez <c>osd</c>).</summary>
        public static IList<string> Available(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            {
                return new List<string>();
            }

            return Directory.GetFiles(folder, "*.traineddata")
                .Select(f => Path.GetFileNameWithoutExtension(f).ToLowerInvariant())
                .Where(l => l != OrientationData)
                .OrderBy(l => l, StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>
        /// Pobiera brakujące dane języków (i <c>osd</c>) do folderu. Zwraca listę pobranych. <paramref name="progress"/> dostaje
        /// komunikaty po polsku. Błąd sieci = wyjątek <see cref="HttpRequestException"/> albo <see cref="IOException"/>.
        /// </summary>
        public static async Task<IList<string>> DownloadMissingAsync(string folder, string languages, IProgress<string> progress, CancellationToken cancellationToken)
        {
            var missing = Missing(folder, languages);
            var downloaded = new List<string>();
            if (missing.Count == 0)
            {
                return downloaded;
            }

            Directory.CreateDirectory(folder);
            using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) })
            {
                http.DefaultRequestHeaders.UserAgent.ParseAdd("Amadeusz-OCR/1.0");
                foreach (var language in missing)
                {
                    progress?.Report("Pobieram dane języka " + Describe(language) + "…");
                    var url = SourceUrl + language + ".traineddata";
                    var target = Path.Combine(folder, language + ".traineddata");
                    var temporary = target + ".part";
                    using (var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
                    {
                        if (!response.IsSuccessStatusCode)
                        {
                            throw new HttpRequestException("Nie udało się pobrać " + url + ": " + (int)response.StatusCode + " " + response.ReasonPhrase);
                        }

                        using (var source = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                        using (var file = File.Create(temporary))
                        {
                            await source.CopyToAsync(file, 81920, cancellationToken).ConfigureAwait(false);
                        }
                    }

                    if (new FileInfo(temporary).Length < 100_000)
                    {
                        File.Delete(temporary);
                        throw new IOException("Plik " + language + ".traineddata jest podejrzanie mały — pobieranie przerwane.");
                    }

                    if (File.Exists(target))
                    {
                        File.Delete(target);
                    }

                    File.Move(temporary, target);
                    downloaded.Add(language);
                    progress?.Report("Pobrano " + Describe(language) + " (" + (new FileInfo(target).Length / 1024 / 1024) + " MB).");
                }
            }

            return downloaded;
        }

        /// <summary>„pol (polski)” albo sam kod, gdy nieznany.</summary>
        public static string Describe(string code)
            => Known.TryGetValue(code ?? "", out var name) ? code + " (" + name + ")" : code == OrientationData ? "osd (orientacja strony)" : code;
    }
}
