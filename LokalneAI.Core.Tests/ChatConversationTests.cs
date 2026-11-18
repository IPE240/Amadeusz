using LokalneAI.Core.Storage;

namespace LokalneAI.Core.Tests;

public class ChatConversationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "lokalneai_czat_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    private static CodeConversation Sample(string title) => new()
    {
        Title = title,
        Created = DateTime.Now,
        Updated = DateTime.Now,
        Entries = [new() { Kind = "User", Text = "Pytanie" }, new() { Kind = "Answer", Text = "| a | b |\n|---|---|\n| 1 | 2 |", Success = true }],
        History = [new() { Role = "user", Content = "Pytanie" }, new() { Role = "assistant", Content = "Odpowiedź" }],
    };

    [Fact]
    public void Grupy_ToPodfoldery_BezKosza_ZapisIWczytanie()
    {
        var mosty = ChatConversationStore.CreateGroup(_root, "Mosty");
        Directory.CreateDirectory(Path.Combine(_root, ChatConversationStore.TrashName));
        var path = ChatConversationStore.Save(mosty, Sample("Belki"));

        Assert.Equal(["Mosty"], ChatConversationStore.Groups(_root).Select(g => g.Name));
        var listed = Assert.Single(ChatConversationStore.List(mosty));
        Assert.Equal("Belki", listed.Title);
        Assert.Equal(path, listed.Path);
        Assert.Empty(ChatConversationStore.List(_root)); // „Niezgrupowane” nie widzą rozmów z podfolderów
        Assert.Equal(2, CodeConversationStore.Load(path)!.History.Count);
        Assert.Throws<ArgumentException>(() => ChatConversationStore.CreateGroup(_root, "kosz"));
    }

    [Fact]
    public void Usuniecie_IdzieDoKosza_KoszKasujePoTerminie()
    {
        var conversation = Sample("Do kosza");
        ChatConversationStore.Save(_root, conversation);
        var trashed = ChatConversationStore.MoveToTrash(_root, _root, conversation.Id);

        Assert.NotNull(trashed);
        Assert.True(File.Exists(trashed));
        Assert.Empty(ChatConversationStore.List(_root));
        Assert.True((DateTime.Now - File.GetLastWriteTime(trashed!)).TotalMinutes < 1); // termin liczy się od usunięcia

        Assert.Equal(0, ConversationTrash.Purge(Path.GetDirectoryName(trashed)!, now: DateTime.Now.AddDays(ConversationTrash.KeepDays - 1)));
        Assert.True(File.Exists(trashed));
        Assert.Equal(1, ConversationTrash.Purge(Path.GetDirectoryName(trashed)!, now: DateTime.Now.AddDays(ConversationTrash.KeepDays + 1)));
        Assert.False(File.Exists(trashed));
    }

    [Fact]
    public void UsuniecieGrupy_RozmowyDoKosza_ZmianaNazwyIPrzeniesienie()
    {
        var a = ChatConversationStore.CreateGroup(_root, "A");
        var conversation = Sample("Rozmowa");
        ChatConversationStore.Save(a, conversation);

        var b = ChatConversationStore.RenameGroup(a, "B");
        Assert.Single(ChatConversationStore.List(b));
        ChatConversationStore.Move(b, _root, conversation.Id);
        Assert.Single(ChatConversationStore.List(_root));
        ChatConversationStore.Move(_root, b, conversation.Id);

        ChatConversationStore.DeleteGroup(_root, b);
        Assert.False(Directory.Exists(b));
        Assert.Single(Directory.GetFiles(Path.Combine(_root, ChatConversationStore.TrashName), "B_*.json"));
    }

    [Fact]
    public void KoszCode_MaTerminIKasujeSiePrzyLiscie()
    {
        var group = Path.Combine(_root, "projekt");
        var conversation = Sample("Code");
        CodeConversationStore.Save(group, conversation);
        var trashed = CodeConversationStore.MoveToTrash(group, conversation.Id)!;
        File.SetLastWriteTime(trashed, DateTime.Now.AddDays(-(ConversationTrash.KeepDays + 1)));

        CodeConversationStore.List(group); // przy wczytaniu listy kosz się czyści
        Assert.False(File.Exists(trashed));
    }
}
