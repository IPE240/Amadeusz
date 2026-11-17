using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;

namespace LokalneAI.Core.Storage;

/// <summary>Rodzaj poświadczenia w menedżerze Amadeusza.</summary>
public enum CredentialKind
{
    /// <summary>Baza PostgreSQL: host, port, baza, użytkownik, hasło i (opcjonalnie) Twój identyfikator w bazie.</summary>
    Postgres,

    /// <summary>Serwer FTP: host, port, użytkownik, hasło.</summary>
    Ftp,

    /// <summary>Udział sieciowy Windows (SMB): adres, użytkownik, hasło.</summary>
    Smb,
}

/// <summary>Jedno poświadczenie (wiersz w Ustawieniach → Poświadczenia).</summary>
public sealed class StoredCredential
{
    public CredentialKind Kind { get; set; }

    /// <summary>Nazwa wpisu, np. <c>cde</c> — profil asystenta wskazuje ją polem <c>poswiadczenie</c> w <c>profil.json</c>.</summary>
    public string Name { get; set; } = "";

    /// <summary>Adres serwera (nazwa albo IP); przy SMB np. <c>\\serwer\udzial</c>.</summary>
    public string Host { get; set; } = "";

    /// <summary>Port; null = domyślny (PostgreSQL 5432, FTP 21).</summary>
    public int? Port { get; set; }

    /// <summary>Nazwa bazy (PostgreSQL).</summary>
    public string Database { get; set; } = "";

    public string User { get; set; } = "";

    public string Password { get; set; } = "";

    /// <summary>Twój identyfikator w bazie (PostgreSQL), np. <c>person_id</c> — asystent pyta o „moje” dane przez <c>{{ja}}</c>.</summary>
    public string PersonId { get; set; } = "";

    /// <summary>Port do połączenia: wpisany albo domyślny dla rodzaju.</summary>
    public int EffectivePort => Port ?? (Kind == CredentialKind.Ftp ? 21 : Kind == CredentialKind.Postgres ? 5432 : 445);

    /// <summary>Opis bez hasła, np. „jan@serwer:5432/projekty”.</summary>
    public string Describe() => Kind switch
    {
        CredentialKind.Postgres => $"{User}@{Host}:{EffectivePort}/{Database}",
        CredentialKind.Ftp => $"{User}@{Host}:{EffectivePort}",
        _ => $"{User} @ {Host}",
    };
}

/// <summary>
/// Poświadczenia Amadeusza w <b>Menedżerze poświadczeń Windows</b> (Panel sterowania → Menedżer poświadczeń → Poświadczenia
/// systemu Windows → Poświadczenia ogólne, nazwy <c>Amadeusz:postgres:cde</c> itd.). Windows szyfruje je kontem użytkownika
/// (DPAPI) i trzyma tylko na tym komputerze (bez synchronizacji między komputerami). Program nie zapisuje ich nigdzie indziej:
/// ani w ustawieniach, ani w repozytorium. Pola poza loginem (adres, port, baza, hasło, identyfikator) są razem w zaszyfrowanej
/// części wpisu (JSON, do 2560 bajtów) — w Menedżerze widać tylko nazwę wpisu i login.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class CredentialVault(string prefix = CredentialVault.DefaultPrefix)
{
    public const string DefaultPrefix = "Amadeusz";

    private const int CredTypeGeneric = 1;
    private const int CredPersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;
    private const int MaxBlobBytes = 5 * 512;

    /// <summary>Przedrostek nazw wpisów (testy używają własnego, żeby nie ruszać prawdziwych).</summary>
    public string Prefix { get; } = prefix;

    /// <summary>Nazwa wpisu w Menedżerze poświadczeń, np. <c>Amadeusz:postgres:cde</c>.</summary>
    public string TargetName(CredentialKind kind, string name) => Prefix + ":" + kind.ToString().ToLowerInvariant() + ":" + name.Trim();

    /// <summary>Wszystkie poświadczenia Amadeusza, posortowane po rodzaju i nazwie. Wpisy nie do odczytania są pomijane.</summary>
    public IReadOnlyList<StoredCredential> List()
    {
        var list = new List<StoredCredential>();
        if (!CredEnumerate(Prefix + ":*", 0, out var count, out var array))
        {
            var error = Marshal.GetLastWin32Error();
            return error == ErrorNotFound ? list : throw new Win32Exception(error);
        }

        try
        {
            for (var i = 0; i < count; i++)
            {
                var pointer = Marshal.ReadIntPtr(array, i * IntPtr.Size);
                if (FromNative(Marshal.PtrToStructure<NativeCredential>(pointer)) is { } credential)
                {
                    list.Add(credential);
                }
            }
        }
        finally
        {
            CredFree(array);
        }

        return list.OrderBy(c => c.Kind).ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Wpis o tym rodzaju i nazwie albo null.</summary>
    public StoredCredential? Find(CredentialKind kind, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        if (!CredRead(TargetName(kind, name), CredTypeGeneric, 0, out var pointer))
        {
            var error = Marshal.GetLastWin32Error();
            return error == ErrorNotFound ? null : throw new Win32Exception(error);
        }

        try
        {
            return FromNative(Marshal.PtrToStructure<NativeCredential>(pointer));
        }
        finally
        {
            CredFree(pointer);
        }
    }

    /// <summary>
    /// Zapisuje wpis (nowy albo zmieniony). Po zmianie nazwy albo rodzaju podaj poprzednie — stary wpis zostanie usunięty.
    /// Pusta nazwa albo za długie dane: <see cref="ArgumentException"/>.
    /// </summary>
    public void Save(StoredCredential credential, CredentialKind? previousKind = null, string? previousName = null)
    {
        ArgumentNullException.ThrowIfNull(credential);
        var name = credential.Name.Trim();
        if (name.Length == 0 || name.Contains(':'))
        {
            throw new ArgumentException("Nazwa wpisu nie może być pusta ani zawierać dwukropka.", nameof(credential));
        }

        var secret = JsonSerializer.SerializeToUtf8Bytes(new Secret
        {
            Host = credential.Host.Trim(),
            Port = credential.Port,
            Database = credential.Database.Trim(),
            Password = credential.Password,
            PersonId = credential.PersonId.Trim(),
        });
        if (secret.Length > MaxBlobBytes)
        {
            throw new ArgumentException("Za dużo danych w jednym wpisie (Windows pozwala na " + MaxBlobBytes + " bajtów).", nameof(credential));
        }

        var blob = Marshal.AllocHGlobal(secret.Length);
        try
        {
            Marshal.Copy(secret, 0, blob, secret.Length);
            var native = new NativeCredential
            {
                Type = CredTypeGeneric,
                TargetName = TargetName(credential.Kind, name),
                UserName = credential.User.Trim(),
                Comment = "Amadeusz — " + credential.Kind,
                CredentialBlob = blob,
                CredentialBlobSize = secret.Length,
                Persist = CredPersistLocalMachine,
            };
            if (!CredWrite(ref native, 0))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
        }
        finally
        {
            Marshal.FreeHGlobal(blob);
        }

        if (previousName is not null && (previousKind != credential.Kind || !string.Equals(previousName.Trim(), name, StringComparison.OrdinalIgnoreCase)))
        {
            Delete(previousKind ?? credential.Kind, previousName);
        }
    }

    /// <summary>Usuwa wpis; brak wpisu to nie błąd.</summary>
    public void Delete(CredentialKind kind, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        if (!CredDelete(TargetName(kind, name), CredTypeGeneric, 0))
        {
            var error = Marshal.GetLastWin32Error();
            if (error != ErrorNotFound)
            {
                throw new Win32Exception(error);
            }
        }
    }

    private StoredCredential? FromNative(NativeCredential native)
    {
        // Amadeusz:postgres:cde
        var target = native.TargetName ?? "";
        if (!target.StartsWith(Prefix + ":", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var rest = target[(Prefix.Length + 1)..];
        var colon = rest.IndexOf(':');
        if (colon <= 0 || !Enum.TryParse<CredentialKind>(rest[..colon], ignoreCase: true, out var kind))
        {
            return null;
        }

        var secret = new Secret();
        if (native.CredentialBlobSize > 0 && native.CredentialBlob != IntPtr.Zero)
        {
            var bytes = new byte[native.CredentialBlobSize];
            Marshal.Copy(native.CredentialBlob, bytes, 0, bytes.Length);
            try
            {
                secret = JsonSerializer.Deserialize<Secret>(bytes) ?? new Secret();
            }
            catch (JsonException)
            {
                // Wpis zapisany ręcznie w Menedżerze (samo hasło jako tekst UTF-16).
                secret = new Secret { Password = Encoding.Unicode.GetString(bytes) };
            }
        }

        return new StoredCredential
        {
            Kind = kind,
            Name = rest[(colon + 1)..],
            User = native.UserName ?? "",
            Host = secret.Host ?? "",
            Port = secret.Port,
            Database = secret.Database ?? "",
            Password = secret.Password ?? "",
            PersonId = secret.PersonId ?? "",
        };
    }

    private sealed class Secret
    {
        public string? Host { get; set; }

        public int? Port { get; set; }

        public string? Database { get; set; }

        public string? Password { get; set; }

        public string? PersonId { get; set; }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public int Flags;
        public int Type;
        public string TargetName;
        public string Comment;
        public long LastWritten;
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public IntPtr Attributes;
        public string TargetAlias;
        public string UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite(ref NativeCredential credential, int flags);

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, int type, int flags, out IntPtr credential);

    [DllImport("advapi32.dll", EntryPoint = "CredEnumerateW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredEnumerate(string filter, int flags, out int count, out IntPtr credentials);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDelete(string target, int type, int flags);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern void CredFree(IntPtr buffer);
}
