using System.Diagnostics;
using System.Text;
using LokalneAI.Harness.Profiles;
using LokalneAI.Harness.Session;
using LokalneAI.Harness.Tools;

namespace LokalneAI.Harness.Tests;

public sealed class FileSystemExecutorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "lokalneai_pliki_" + Guid.NewGuid().ToString("N"));

    public FileSystemExecutorTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, true);

    private FileSystemExecutor Executor() => new FileSystemExecutor(new FileSystemOptions { AllowedRoots = new List<string> { _root }, WorkingFolder = _root });

    private static ToolCall Call(string tool, string args)
        => new ToolCall(HarnessProfile.Load(Paths.Profile("pliki")).FindTool(tool), Paths.Json(args), 1, 1);

    [Fact]
    public async Task CdIMkdir_WzgledneIBezwzgledne()
    {
        var executor = Executor();
        Directory.CreateDirectory(Path.Combine(_root, "1095_WSP"));

        Assert.True((await executor.ExecuteAsync(Call("przejdz_do_folderu", "{\"sciezka\":\"1095_WSP\"}"), CancellationToken.None)).Success);
        var created = await executor.ExecuteAsync(Call("utworz_folder", "{\"sciezka\":\"R02\\\\Wydruki\"}"), CancellationToken.None);

        Assert.True(created.Success);
        Assert.True(Directory.Exists(Path.Combine(_root, "1095_WSP", "R02", "Wydruki")));
        var list = await executor.ExecuteAsync(Call("pokaz_zawartosc", "{}"), CancellationToken.None);
        Assert.Contains("[folder] R02", list.Text);
    }

    [Theory]
    [InlineData("C:\\\\Windows\\\\Nowy")]
    [InlineData("..\\\\..\\\\poza")]
    public async Task PozaDozwolonymFolderem_Blad(string path)
    {
        var result = await Executor().ExecuteAsync(Call("utworz_folder", "{\"sciezka\":\"" + path + "\"}"), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("poza dozwolonymi", result.Text);
    }

    [Fact]
    public async Task KopiujIPrzenies_NieNadpisuja()
    {
        File.WriteAllText(Path.Combine(_root, "notka.docx"), "x");
        Directory.CreateDirectory(Path.Combine(_root, "R01"));
        var executor = Executor();

        Assert.True((await executor.ExecuteAsync(Call("kopiuj", "{\"zrodlo\":\"notka.docx\",\"cel\":\"R01\"}"), CancellationToken.None)).Success);
        var again = await executor.ExecuteAsync(Call("kopiuj", "{\"zrodlo\":\"notka.docx\",\"cel\":\"R01\"}"), CancellationToken.None);
        var renamed = await executor.ExecuteAsync(Call("przenies", "{\"zrodlo\":\"R01\",\"cel\":\"R01_stare\"}"), CancellationToken.None);

        Assert.False(again.Success);
        Assert.Contains("już istnieje", again.Text);
        Assert.True(renamed.Success);
        Assert.True(File.Exists(Path.Combine(_root, "R01_stare", "notka.docx")));
    }

    [Fact]
    public async Task Sesja_PolecenieWStyluPowershella_TrafiaDoWykonawcy()
    {
        var llm = new ScriptedLlm(
            "{\"krok\":\"wywolania\",\"wywolania\":[{\"narzedzie\":\"przejdz_do_folderu\",\"argumenty\":{\"sciezka\":\"" + _root.Replace("\\", "\\\\") + "\"}},{\"narzedzie\":\"utworz_folder\",\"argumenty\":{\"sciezka\":\"Raporty\"}}]}");
        using var session = new HarnessSession(llm, HarnessProfile.Load(Paths.Profile("pliki")), Executor());

        var turn = await session.SendAsync("cd " + _root + " i mkdir Raporty");

        Assert.Equal(TurnKind.Answer, turn.Kind);
        Assert.True(Directory.Exists(Path.Combine(_root, "Raporty")));
    }
}

public sealed class KnowledgeSyncTests : IDisposable
{
    private readonly string _work = Path.Combine(Path.GetTempPath(), "lokalneai_sync_" + Guid.NewGuid().ToString("N"));

    public KnowledgeSyncTests() => Directory.CreateDirectory(_work);

    public void Dispose()
    {
        // Pliki .git bywają tylko do odczytu.
        foreach (var file in Directory.GetFiles(_work, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(_work, true);
    }

    private static void Git(string folder, string args)
    {
        var start = new ProcessStartInfo("git", args) { WorkingDirectory = folder, UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        using var process = Process.Start(start);
        process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, "git " + args + ": " + error);
    }

    [Fact]
    public async Task Repozytorium_PobieraTylkoZmienioneINicNieKasuje()
    {
        var repo = Path.Combine(_work, "dokumentacja");
        Directory.CreateDirectory(Path.Combine(repo, "docs"));
        File.WriteAllText(Path.Combine(repo, "docs", "kotwy.md"), "# Kotwy\nM24, 400 mm", Encoding.UTF8);
        File.WriteAllText(Path.Combine(repo, "inne.txt"), "pomijane", Encoding.UTF8);
        Git(repo, "init -b main");
        Git(repo, "-c user.email=t@t -c user.name=t add .");
        Git(repo, "-c user.email=t@t -c user.name=t commit -m start");

        var profile = Path.Combine(_work, "profil");
        Directory.CreateDirectory(Path.Combine(profile, "wiedza"));
        File.WriteAllText(Path.Combine(profile, "wiedza", "moja_notatka.md"), "# Moje\nnie ruszać", Encoding.UTF8);
        File.WriteAllText(Path.Combine(profile, KnowledgeSync.SourcesFile),
            "{\"zrodla\":[{\"typ\":\"git\",\"repo\":" + System.Text.Json.JsonSerializer.Serialize(repo) + ",\"galaz\":\"main\",\"pliki\":[\"docs/*.md\"]}]}");
        var cache = Path.Combine(_work, "cache");

        var first = await KnowledgeSync.SyncAsync(profile, cache);
        var second = await KnowledgeSync.SyncAsync(profile, cache);
        File.WriteAllText(Path.Combine(repo, "docs", "kotwy.md"), "# Kotwy\nM24, 500 mm", Encoding.UTF8);
        Git(repo, "-c user.email=t@t -c user.name=t commit -am zmiana");
        var third = await KnowledgeSync.SyncAsync(profile, cache);

        Assert.Empty(first.Errors);
        Assert.Equal(1, first.Updated);
        Assert.Equal(0, second.Updated);
        Assert.Equal(1, second.Unchanged);
        Assert.Equal(1, third.Updated);
        var synced = Path.Combine(profile, "wiedza", "zrodla", "dokumentacja", "docs", "kotwy.md");
        Assert.Contains("500 mm", File.ReadAllText(synced));
        Assert.True(File.Exists(Path.Combine(profile, "wiedza", "moja_notatka.md")));
        Assert.False(File.Exists(Path.Combine(profile, "wiedza", "zrodla", "dokumentacja", "inne.txt")));
        Assert.NotNull(KnowledgeSync.LastSync(profile));

        // Pobrany plik trafia do wyszukiwarki profilu.
        var index = KnowledgeIndex.FromFolder(Path.Combine(profile, "wiedza"));
        Assert.Contains("500 mm", index.Search("kotwy")[0]);
    }
}
