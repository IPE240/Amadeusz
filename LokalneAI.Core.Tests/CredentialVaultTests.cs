using LokalneAI.Core.Storage;

namespace LokalneAI.Core.Tests;

/// <summary>
/// Menedżer poświadczeń Windows przez <see cref="CredentialVault"/>. Testy piszą do prawdziwego Menedżera, ale pod własnym
/// przedrostkiem (AmadeuszTest-…) i sprzątają po sobie — prawdziwych wpisów Amadeusza nie dotykają.
/// </summary>
public sealed class CredentialVaultTests : IDisposable
{
    private readonly CredentialVault _vault = new("AmadeuszTest-" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose()
    {
        foreach (var credential in _vault.List())
        {
            _vault.Delete(credential.Kind, credential.Name);
        }
    }

    [Fact]
    public void Postgres_ZapisOdczytLista()
    {
        _vault.Save(new StoredCredential
        {
            Kind = CredentialKind.Postgres, Name = "baza", Host = "serwer-testowy", Port = 6543, Database = "projekty",
            User = "jan", Password = "tajne hasło ąę", PersonId = "uuid-1",
        });

        var read = _vault.Find(CredentialKind.Postgres, "baza")!;

        Assert.Equal("serwer-testowy", read.Host);
        Assert.Equal(6543, read.Port);
        Assert.Equal("projekty", read.Database);
        Assert.Equal("jan", read.User);
        Assert.Equal("tajne hasło ąę", read.Password);
        Assert.Equal("uuid-1", read.PersonId);
        Assert.Equal("jan@serwer-testowy:6543/projekty", read.Describe());
        Assert.Single(_vault.List());
        Assert.Null(_vault.Find(CredentialKind.Ftp, "baza")); // rodzaj jest częścią nazwy wpisu
    }

    [Fact]
    public void ZmianaNazwy_UsuwaStaryWpis_IUsuwanie()
    {
        var credential = new StoredCredential { Kind = CredentialKind.Ftp, Name = "stary", Host = "ftp.example.com", User = "u", Password = "p" };
        _vault.Save(credential);

        credential.Name = "nowy";
        credential.Kind = CredentialKind.Smb;
        _vault.Save(credential, CredentialKind.Ftp, "stary");

        Assert.Null(_vault.Find(CredentialKind.Ftp, "stary"));
        Assert.Equal("p", _vault.Find(CredentialKind.Smb, "nowy")!.Password);
        Assert.Equal(445, _vault.Find(CredentialKind.Smb, "nowy")!.EffectivePort);

        _vault.Delete(CredentialKind.Smb, "nowy");
        _vault.Delete(CredentialKind.Smb, "nie-ma"); // brak wpisu to nie błąd
        Assert.Empty(_vault.List());
    }

    [Fact]
    public void ZlaNazwa_IZaDuzo_Odrzucone()
    {
        Assert.Throws<ArgumentException>(() => _vault.Save(new StoredCredential { Name = " " }));
        Assert.Throws<ArgumentException>(() => _vault.Save(new StoredCredential { Name = "a:b" }));
        Assert.Throws<ArgumentException>(() => _vault.Save(new StoredCredential { Name = "duzy", Password = new string('x', 3000) }));
        Assert.Empty(_vault.List());
    }
}
