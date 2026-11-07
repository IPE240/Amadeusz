using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace LokalneAI.Harness.Tools
{
    /// <summary>Jedno źródło dokumentacji z <c>zrodla.json</c> profilu.</summary>
    public sealed class KnowledgeSource
    {
        /// <summary><c>git</c> (repozytorium) albo <c>url</c> (pojedynczy plik).</summary>
        public string Type { get; set; }

        /// <summary>Adres repozytorium, np. <c>https://github.com/Optimal-Design-of-Structures/CDE.git</c>.</summary>
        public string Repo { get; set; }

        /// <summary>Gałąź (<c>galaz</c>); null = domyślna gałąź repozytorium.</summary>
        public string Branch { get; set; }

        /// <summary>Wzorce plików w repozytorium, np. <c>docs/*.md</c>, <c>docs/**/*.md</c>, <c>README.md</c>.</summary>
        public IList<string> Files { get; set; } = new List<string>();

        /// <summary>Adres pliku dla typu <c>url</c>.</summary>
        public string Url { get; set; }

        /// <summary>Nazwa zmiennej środowiskowej z tokenem (prywatne pliki przez https), np. <c>GITHUB_TOKEN</c>.</summary>
        public string TokenEnv { get; set; }

        /// <summary>Podfolder w <c>wiedza\zrodla\</c>; domyślnie nazwa repozytorium albo pliku.</summary>
        public string Target { get; set; }
    }

    /// <summary>Wynik aktualizacji wiedzy.</summary>
    public sealed class KnowledgeSyncResult
    {
        /// <summary>Kiedy aktualizowano.</summary>
        public DateTime Time { get; set; } = DateTime.Now;

        /// <summary>Ile plików dodano albo zmieniono.</summary>
        public int Updated { get; set; }

        /// <summary>Ile plików było aktualnych.</summary>
        public int Unchanged { get; set; }

        /// <summary>Błędy źródeł; błąd jednego źródła nie zatrzymuje pozostałych.</summary>
        public List<string> Errors { get; } = new List<string>();

        /// <summary>Podsumowanie po polsku, np. „zaktualizowano 2, bez zmian 5”.</summary>
        public override string ToString()
            => "zaktualizowano " + Updated + ", bez zmian " + Unchanged + (Errors.Count > 0 ? ", błędy: " + string.Join("; ", Errors) : "");
    }

    /// <summary>
    /// Automatyczna aktualizacja dokumentacji profilu z repozytoriów (git) i adresów (https).
    /// Pliki trafiają do <c>&lt;profil&gt;\wiedza\zrodla\</c> — korzysta z nich narzędzie <c>szukaj_wiedzy</c>.
    /// Nic nie jest kasowane; pliki zmieniają się tylko, gdy w źródle jest nowa treść.
    /// </summary>
    public static class KnowledgeSync
    {
        /// <summary>Nazwa pliku źródeł w folderze profilu.</summary>
        public const string SourcesFile = "zrodla.json";
        private const string StateFile = ".stan.json";

        /// <summary>Czyta <c>zrodla.json</c> profilu; brak pliku = pusta lista.</summary>
        public static IList<KnowledgeSource> ReadSources(string profileFolder)
        {
            var list = new List<KnowledgeSource>();
            var path = Path.Combine(profileFolder, SourcesFile);
            if (!File.Exists(path))
            {
                return list;
            }

            using (var document = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8)))
            {
                if (!document.RootElement.TryGetProperty("zrodla", out var array) || array.ValueKind != JsonValueKind.Array)
                {
                    return list;
                }

                foreach (var item in array.EnumerateArray())
                {
                    var source = new KnowledgeSource
                    {
                        Type = Get(item, "typ") ?? (Get(item, "repo") != null ? "git" : "url"),
                        Repo = Get(item, "repo"),
                        Branch = Get(item, "galaz"),
                        Url = Get(item, "url"),
                        TokenEnv = Get(item, "token_env"),
                        Target = Get(item, "cel"),
                    };
                    if (item.TryGetProperty("pliki", out var files) && files.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var file in files.EnumerateArray())
                        {
                            source.Files.Add(file.GetString());
                        }
                    }

                    list.Add(source);
                }
            }

            return list;
        }

        /// <summary>Zapisuje listę źródeł do <c>zrodla.json</c> profilu (np. z okna „Źródła wiedzy”).</summary>
        public static void WriteSources(string profileFolder, IEnumerable<KnowledgeSource> sources)
        {
            var path = Path.Combine(profileFolder, SourcesFile);
            using (var stream = new MemoryStream())
            {
                using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
                {
                    writer.WriteStartObject();
                    writer.WriteStartArray("zrodla");
                    foreach (var source in sources ?? Enumerable.Empty<KnowledgeSource>())
                    {
                        writer.WriteStartObject();
                        writer.WriteString("typ", string.IsNullOrWhiteSpace(source.Type) ? (string.IsNullOrWhiteSpace(source.Url) ? "git" : "url") : source.Type);
                        WriteIfAny(writer, "repo", source.Repo);
                        WriteIfAny(writer, "galaz", source.Branch);
                        WriteIfAny(writer, "url", source.Url);
                        WriteIfAny(writer, "token_env", source.TokenEnv);
                        WriteIfAny(writer, "cel", source.Target);
                        if (source.Files.Count > 0)
                        {
                            writer.WriteStartArray("pliki");
                            foreach (var file in source.Files.Where(f => !string.IsNullOrWhiteSpace(f)))
                            {
                                writer.WriteStringValue(file.Trim());
                            }

                            writer.WriteEndArray();
                        }

                        writer.WriteEndObject();
                    }

                    writer.WriteEndArray();
                    writer.WriteEndObject();
                }

                File.WriteAllText(path, Encoding.UTF8.GetString(stream.ToArray()) + "\n", new UTF8Encoding(false));
            }
        }

        private static void WriteIfAny(Utf8JsonWriter writer, string name, string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                writer.WriteString(name, value.Trim());
            }
        }

        /// <summary>Czas ostatniej aktualizacji; null = jeszcze nie było.</summary>
        public static DateTime? LastSync(string profileFolder)
        {
            var path = Path.Combine(profileFolder, "wiedza", "zrodla", StateFile);
            return File.Exists(path) ? File.GetLastWriteTime(path) : (DateTime?)null;
        }

        /// <summary>Pobiera nowe wersje plików ze wszystkich źródeł profilu.</summary>
        /// <param name="profileFolder">Folder profilu.</param>
        /// <param name="cacheFolder">Folder na kopie repozytoriów, np. <c>%LOCALAPPDATA%\LokalneAI\repo</c>.</param>
        /// <param name="cancellationToken">Przerwanie.</param>
        public static async Task<KnowledgeSyncResult> SyncAsync(string profileFolder, string cacheFolder, CancellationToken cancellationToken = default(CancellationToken))
        {
            var result = new KnowledgeSyncResult();
            var targetRoot = Path.Combine(profileFolder, "wiedza", "zrodla");
            foreach (var source in ReadSources(profileFolder))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    if (source.Type == "git")
                    {
                        await SyncGitAsync(source, cacheFolder, targetRoot, result, cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        await SyncUrlAsync(source, targetRoot, result, cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (Exception e) when (e is IOException || e is HttpRequestException || e is InvalidOperationException
                    || e is UnauthorizedAccessException || e is System.ComponentModel.Win32Exception || e is TaskCanceledException)
                {
                    result.Errors.Add((source.Repo ?? source.Url) + ": " + e.Message);
                }
            }

            Directory.CreateDirectory(targetRoot);
            File.WriteAllText(Path.Combine(targetRoot, StateFile), JsonSerializer.Serialize(new
            {
                czas = result.Time.ToString("s"),
                zaktualizowano = result.Updated,
                bez_zmian = result.Unchanged,
                bledy = result.Errors,
            }, Text.Relaxed), new UTF8Encoding(false));
            return result;
        }

        private static async Task SyncGitAsync(KnowledgeSource source, string cacheFolder, string targetRoot, KnowledgeSyncResult result, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(source.Repo))
            {
                throw new InvalidOperationException("brak adresu repozytorium");
            }

            var name = RepoName(source.Repo);
            var clone = Path.Combine(cacheFolder, name);
            var branch = string.IsNullOrWhiteSpace(source.Branch) ? null : source.Branch;
            if (!Directory.Exists(Path.Combine(clone, ".git")))
            {
                Directory.CreateDirectory(cacheFolder);
                await GitAsync(cacheFolder, "clone --depth 1 " + (branch != null ? "--branch \"" + branch + "\" " : "") + "\"" + source.Repo + "\" \"" + name + "\"", ct).ConfigureAwait(false);
            }
            else
            {
                // Kopia w folderze podręcznym jest nasza — można ją bez obaw ustawić na stan zdalny.
                var remoteBranch = branch ?? (await GitAsync(clone, "rev-parse --abbrev-ref HEAD", ct).ConfigureAwait(false)).Trim();
                await GitAsync(clone, "fetch --depth 1 origin \"" + remoteBranch + "\"", ct).ConfigureAwait(false);
                await GitAsync(clone, "reset --hard FETCH_HEAD", ct).ConfigureAwait(false);
            }

            var target = Path.Combine(targetRoot, string.IsNullOrWhiteSpace(source.Target) ? name : source.Target);
            var patterns = source.Files.Count > 0 ? source.Files : new List<string> { "**/*.md" };
            foreach (var file in patterns.SelectMany(p => Match(clone, p)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var relative = file.Substring(clone.Length).TrimStart('\\', '/');
                CopyIfChanged(file, Path.Combine(target, relative), result);
            }
        }

        private static async Task SyncUrlAsync(KnowledgeSource source, string targetRoot, KnowledgeSyncResult result, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(source.Url))
            {
                throw new InvalidOperationException("brak adresu pliku");
            }

            using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) })
            using (var request = new HttpRequestMessage(HttpMethod.Get, source.Url))
            {
                var token = string.IsNullOrEmpty(source.TokenEnv) ? null : Environment.GetEnvironmentVariable(source.TokenEnv);
                if (!string.IsNullOrEmpty(token))
                {
                    request.Headers.TryAddWithoutValidation("Authorization", "token " + token);
                }

                using (var response = await http.SendAsync(request, ct).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        throw new HttpRequestException("HTTP " + (int)response.StatusCode);
                    }

                    var bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                    var fileName = string.IsNullOrWhiteSpace(source.Target) ? Path.GetFileName(new Uri(source.Url).AbsolutePath) : source.Target;
                    WriteIfChanged(bytes, Path.Combine(targetRoot, fileName), result);
                }
            }
        }

        /// <summary>Pliki pasujące do wzorca: <c>docs/*.md</c>, <c>docs/**/*.md</c> (z podfolderami), <c>README.md</c>.</summary>
        internal static IEnumerable<string> Match(string root, string pattern)
        {
            var normalized = pattern.Replace('/', Path.DirectorySeparatorChar);
            var recursive = normalized.Contains("**");
            normalized = normalized.Replace("**" + Path.DirectorySeparatorChar, "").Replace("**", "");
            var folder = Path.GetDirectoryName(normalized) ?? "";
            var filePattern = Path.GetFileName(normalized);
            var directory = Path.Combine(root, folder);
            if (!Directory.Exists(directory))
            {
                return Enumerable.Empty<string>();
            }

            return Directory.GetFiles(directory, string.IsNullOrEmpty(filePattern) ? "*" : filePattern, recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
                .Where(f => f.IndexOf(Path.DirectorySeparatorChar + ".git" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) < 0);
        }

        private static void CopyIfChanged(string source, string target, KnowledgeSyncResult result)
            => WriteIfChanged(File.ReadAllBytes(source), target, result);

        private static void WriteIfChanged(byte[] content, string target, KnowledgeSyncResult result)
        {
            if (File.Exists(target) && File.ReadAllBytes(target).SequenceEqual(content))
            {
                result.Unchanged++;
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target));
            File.WriteAllBytes(target, content);
            result.Updated++;
        }

        private static async Task<string> GitAsync(string workingDirectory, string arguments, CancellationToken ct)
        {
            var start = new ProcessStartInfo("git", arguments)
            {
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            start.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0";
            using (var process = Process.Start(start))
            {
                var output = process.StandardOutput.ReadToEndAsync();
                var error = process.StandardError.ReadToEndAsync();
                var finished = await Task.Run(() => process.WaitForExit(120000), ct).ConfigureAwait(false);
                if (!finished)
                {
                    process.Kill();
                    throw new IOException("git " + arguments.Split(' ')[0] + ": przekroczono czas");
                }

                if (process.ExitCode != 0)
                {
                    throw new IOException("git " + arguments.Split(' ')[0] + ": " + Text.Shorten((await error.ConfigureAwait(false)).Trim(), 300));
                }

                return await output.ConfigureAwait(false);
            }
        }

        private static string RepoName(string repo)
        {
            var name = repo.TrimEnd('/');
            name = name.Substring(name.LastIndexOfAny(new[] { '/', '\\', ':' }) + 1);
            if (name.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            {
                name = name.Substring(0, name.Length - 4);
            }

            return string.Concat(name.Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.' ? c : '_'));
        }

        private static string Get(JsonElement item, string name)
            => item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }
}
