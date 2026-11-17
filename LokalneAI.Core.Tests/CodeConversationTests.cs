using LokalneAI.Core.Storage;

namespace LokalneAI.Core.Tests;

/// <summary>Rozmowy zakładki Code: tylko w &lt;folder grupy&gt;\.amadeusz\, z .gitignore, kosz zamiast kasowania.</summary>
public sealed class CodeConversationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "LokalneAI_rozmowy_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static CodeConversation Sample(string title, DateTime updated) => new()
    {
        Title = title,
        Created = updated,
        Updated = updated,
        Profiles = ["tekla", "komputer"],
        Entries = [new CodeConversationEntry { Kind = "User", Text = "wstaw słup" }, new CodeConversationEntry { Kind = "Call", Text = "→ wstaw_slup", Detail = "profil=HEB300", Success = true }],
        History = [new CodeConversationMessage { Role = "user", Content = "wstaw słup" }],
    };

    [Fact]
    public void Zapis_WFolderzeGrupy_ZGitignore_IOdczyt()
    {
        var group = Path.Combine(_root, "Projekt A");
        Directory.CreateDirectory(group);

        var path = CodeConversationStore.Save(group, Sample("Słupy hali", new DateTime(2026, 11, 17, 10, 0, 0)));

        Assert.Equal(Path.Combine(group, ".amadeusz"), Path.GetDirectoryName(path));
        Assert.Contains("*", File.ReadAllLines(Path.Combine(group, ".amadeusz", ".gitignore")));
        Assert.Equal(new[] { ".amadeusz" }, Directory.GetFileSystemEntries(group).Select(Path.GetFileName));
        var loaded = CodeConversationStore.Load(path)!;
        Assert.Equal("Słupy hali", loaded.Title);
        Assert.Equal(new[] { "tekla", "komputer" }, loaded.Profiles);
        Assert.True(loaded.Entries[1].Success);
        Assert.Equal("wstaw słup", loaded.History[0].Content);
        Assert.Contains("Słupy hali", File.ReadAllText(path)); // polskie znaki czytelne w pliku
    }

    [Fact]
    public void Lista_NajnowszePierwsze_BezUszkodzonych()
    {
        var group = Path.Combine(_root, "B");
        CodeConversationStore.Save(group, Sample("stara", new DateTime(2026, 11, 1)));
        CodeConversationStore.Save(group, Sample("nowa", new DateTime(2026, 11, 17)));
        File.WriteAllText(Path.Combine(group, ".amadeusz", "zepsuta.json"), "{ nie json");

        var list = CodeConversationStore.List(group);

        Assert.Equal(new[] { "nowa", "stara" }, list.Select(c => c.Title));
        Assert.Empty(CodeConversationStore.List(Path.Combine(_root, "nie-ma")));
    }

    [Fact]
    public void Kosz_IPrzeniesienie_NicNieKasuja()
    {
        var a = Path.Combine(_root, "A");
        var b = Path.Combine(_root, "B");
        var first = Sample("do kosza", DateTime.Now);
        var second = Sample("do B", DateTime.Now);
        CodeConversationStore.Save(a, first);
        CodeConversationStore.Save(a, second);

        var trashed = CodeConversationStore.MoveToTrash(a, first.Id);
        var moved = CodeConversationStore.Move(a, b, second.Id);

        Assert.True(File.Exists(trashed));
        Assert.Equal(Path.Combine(a, ".amadeusz", "kosz"), Path.GetDirectoryName(trashed));
        Assert.Empty(CodeConversationStore.List(a)); // kosz nie jest na liście
        Assert.Equal("do B", Assert.Single(CodeConversationStore.List(b)).Title);
        Assert.Equal(moved, CodeConversationStore.PathFor(b, second.Id));
        Assert.True(File.Exists(Path.Combine(b, ".amadeusz", ".gitignore")));
    }

    [Fact]
    public void Ustawienia_GrupyBezDuplikatow_IStaryProfil()
    {
        var settings = new AppSettings
        {
            HarnessProfile = "cde",
            CodeGroups =
            [
                new CodeGroupSetting { Folder = @"C:\ODS\Projekty\WSP\" },
                new CodeGroupSetting { Folder = @"c:\ods\projekty\wsp", Name = "drugi" },
                new CodeGroupSetting { Folder = " " },
            ],
        }.Normalize();

        var group = Assert.Single(settings.CodeGroups);
        Assert.Equal(@"C:\ODS\Projekty\WSP", group.Folder);
        Assert.Equal("WSP", group.Name);
        Assert.Null(settings.HarnessProfiles); // okno weźmie wtedy HarnessProfile
    }

    [Fact]
    public void NoweId_SortujeSieDatami()
    {
        var id = CodeConversationStore.NewId(new DateTime(2026, 11, 17, 14, 30, 12));

        Assert.Matches(@"^2026-11-17_143012_[0-9a-f]{4}$", id);
    }
}
