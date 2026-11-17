using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json;
using System.Windows;
using LokalneAI.Core.Storage;
using Microsoft.Win32;
using Npgsql;

namespace LokalneAI.App;

/// <summary>Połączenie z bazą z poświadczenia (dla profilu z narzędziem SQL).</summary>
public sealed record PostgresAccess(string ConnectionString, string PersonId, string Description);

/// <summary>
/// Ustawienia → „Poświadczenia”: prosta lista wpisów (PostgreSQL, FTP, SMB) w Menedżerze poświadczeń Windows
/// (<see cref="CredentialVault"/>). Program nie trzyma ich nigdzie indziej. Profil asystenta wskazuje wpis nazwą
/// (<c>poswiadczenie</c> w <c>profil.json</c>) i dopiero wtedy pokazuje się w zakładce Code.
/// </summary>
public sealed class CredentialsViewModel : ObservableObject
{
    private readonly CredentialVault _vault;
    private string _status = "";
    private bool _statusIsError;

    public CredentialsViewModel(CredentialVault vault, AppSettings settings, CloudKeyStore keys, Action saveSettings)
    {
        _vault = vault;
        AddCommand = new RelayCommand(() => { Add(); return Task.CompletedTask; });
        ImportCommand = new RelayCommand(ImportAsync);
        MigrateOldSettings(settings, keys, saveSettings);
        Reload();
    }

    /// <summary>Lista wpisów (zapisane i nowe, jeszcze niezapisane).</summary>
    public ObservableCollection<CredentialRow> Rows { get; } = [];

    public RelayCommand AddCommand { get; }

    public RelayCommand ImportCommand { get; }

    /// <summary>Rodzaje do listy rozwijanej w wierszu.</summary>
    public static IReadOnlyList<KeyValuePair<CredentialKind, string>> Kinds { get; } =
    [
        new(CredentialKind.Postgres, "PostgreSQL"),
        new(CredentialKind.Ftp, "FTP"),
        new(CredentialKind.Smb, "SMB"),
    ];

    public string Status { get => _status; private set => SetProperty(ref _status, value); }

    public bool StatusIsError { get => _statusIsError; private set => SetProperty(ref _statusIsError, value); }

    /// <summary>Do nagłówka sekcji: „2 wpisy” albo „brak wpisów”.</summary>
    public string Summary
    {
        get
        {
            var count = Rows.Count(r => r.IsSaved);
            return count == 0 ? "brak wpisów" : count == 1 ? "1 wpis" : count is >= 2 and <= 4 ? count + " wpisy" : count + " wpisów";
        }
    }

    /// <summary>Zmieniły się poświadczenia (zakładka Code odświeża profile i sesję).</summary>
    public event EventHandler? Changed;

    /// <summary>Czy jest wpis PostgreSQL o tej nazwie (np. profil cde pokazuje się tylko wtedy).</summary>
    public bool HasPostgres(string name) => SafeFind(CredentialKind.Postgres, name) is not null;

    /// <summary>Połączenie z bazą z wpisu PostgreSQL o tej nazwie; null, gdy wpisu albo hasła nie ma.</summary>
    public PostgresAccess? GetPostgres(string name)
    {
        var credential = SafeFind(CredentialKind.Postgres, name);
        if (credential is null || credential.User.Length == 0 || credential.Password.Length == 0)
        {
            return null;
        }

        return new PostgresAccess(BuildConnectionString(credential), credential.PersonId, credential.Describe());
    }

    internal static string BuildConnectionString(StoredCredential credential)
        => new NpgsqlConnectionStringBuilder
        {
            Host = credential.Host,
            Port = credential.EffectivePort,
            Database = credential.Database,
            Username = credential.User,
            Password = credential.Password,
            Timeout = 15,
            CommandTimeout = 30,
            ApplicationName = "Amadeusz",
        }.ConnectionString;

    private StoredCredential? SafeFind(CredentialKind kind, string name)
    {
        try
        {
            return _vault.Find(kind, name);
        }
        catch (Win32Exception)
        {
            return null;
        }
    }

    /// <summary>Wczytuje wpisy z Menedżera poświadczeń.</summary>
    public void Reload()
    {
        Rows.Clear();
        try
        {
            foreach (var credential in _vault.List())
            {
                Rows.Add(new CredentialRow(this, credential));
            }
        }
        catch (Win32Exception e)
        {
            SetStatus("Nie odczytałem Menedżera poświadczeń Windows: " + e.Message, error: true);
        }

        OnPropertyChanged(nameof(Summary));
    }

    private void Add()
    {
        Rows.Add(new CredentialRow(this, null));
        SetStatus("", error: false);
    }

    /// <summary>Zapisuje wiersz, gdy ma wszystkie potrzebne pola (wołane, gdy kursor opuszcza wiersz).</summary>
    internal void Commit(CredentialRow row)
    {
        if (!row.IsDirty)
        {
            return;
        }

        var problem = row.Validate(Rows);
        if (problem is not null)
        {
            row.SetState(row.IsSaved || row.HasAnyInput ? problem : "", error: row.IsSaved || row.HasAnyInput);
            return;
        }

        try
        {
            var credential = row.ToCredential();
            if (credential.Password.Length == 0 && row.IsSaved)
            {
                credential.Password = _vault.Find(row.SavedKind!.Value, row.SavedName!)?.Password ?? "";
            }

            _vault.Save(credential, row.SavedKind, row.SavedName);
            row.MarkSaved(credential.Password.Length > 0);
            row.SetState("zapisane", error: false);
        }
        catch (Exception e) when (e is Win32Exception or ArgumentException)
        {
            row.SetState("nie zapisano: " + e.Message, error: true);
            return;
        }

        OnPropertyChanged(nameof(Summary));
        Changed?.Invoke(this, EventArgs.Empty);
        if (row.Kind == CredentialKind.Postgres)
        {
            _ = TestAsync(row); // od razu widać, czy dane są dobre (połączenie tylko do odczytu)
        }
    }

    /// <summary>Kasuje wiersz (×): zapisany wpis znika z Menedżera poświadczeń po potwierdzeniu.</summary>
    internal void Remove(CredentialRow row)
    {
        if (row.IsSaved)
        {
            var answer = MessageBox.Show(Application.Current?.MainWindow!,
                "Usunąć poświadczenie „" + row.SavedName + "” (" + row.SavedKind + ") z Menedżera poświadczeń Windows?",
                "Poświadczenia", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                _vault.Delete(row.SavedKind!.Value, row.SavedName!);
            }
            catch (Win32Exception e)
            {
                SetStatus("Nie usunąłem wpisu: " + e.Message, error: true);
                return;
            }
        }

        Rows.Remove(row);
        OnPropertyChanged(nameof(Summary));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Sprawdza połączenie z bazą (tylko odczyt; dla bazy CDE także, czy identyfikator ma wpisy godzin).</summary>
    internal async Task TestAsync(CredentialRow row)
    {
        Commit(row);
        var access = row.IsSaved ? GetPostgres(row.SavedName!) : null;
        if (access is null)
        {
            row.SetState("najpierw uzupełnij i zapisz wpis (z hasłem)", error: true);
            return;
        }

        row.SetState("łączę się…", error: false);
        try
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    var result = await CheckAsync(access.ConnectionString, access.PersonId);
                    row.SetState("połączono", error: false);
                    SetStatus(row.Name + ": " + result, error: false);
                    return;
                }
                catch (NpgsqlException e) when (attempt == 1 && e.InnerException is TimeoutException or System.Net.Sockets.SocketException)
                {
                    // zimny start bazy — drugie podejście zwykle przechodzi
                }
            }
        }
        catch (Exception e) when (e is NpgsqlException or TimeoutException or InvalidOperationException)
        {
            row.SetState("brak połączenia", error: true);
            SetStatus(row.Name + ": nie połączyłem się — " + e.Message, error: true);
        }
    }

    private static async Task<string> CheckAsync(string connectionString, string personId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using (var readOnly = new NpgsqlCommand("SET TRANSACTION READ ONLY", connection, transaction))
        {
            await readOnly.ExecuteNonQueryAsync();
        }

        string who;
        await using (var command = new NpgsqlCommand("SELECT current_user, current_setting('server_version')", connection, transaction))
        await using (var reader = await command.ExecuteReaderAsync())
        {
            await reader.ReadAsync();
            who = "połączono jako " + reader.GetString(0) + " (PostgreSQL " + reader.GetString(1) + ", tylko odczyt).";
        }

        if (string.IsNullOrWhiteSpace(personId))
        {
            await transaction.RollbackAsync();
            return who;
        }

        // Baza z widokiem godzin (np. CDE): czy identyfikator ma wpisy. W innych bazach widoku nie ma — wtedy samo połączenie.
        await using (var exists = new NpgsqlCommand("SELECT to_regclass('timesheet.v_my_hours') IS NOT NULL", connection, transaction))
        {
            if (!(bool)(await exists.ExecuteScalarAsync() ?? false))
            {
                await transaction.RollbackAsync();
                return who;
            }
        }

        await using (var command = new NpgsqlCommand("SELECT EXISTS (SELECT 1 FROM timesheet.v_my_hours h WHERE h.person_id::text = @p)", connection, transaction))
        {
            command.Parameters.AddWithValue("p", personId.Trim());
            var found = (bool)(await command.ExecuteScalarAsync() ?? false);
            await transaction.RollbackAsync();
            return who + (found ? " Twój identyfikator ma wpisy godzin." : " UWAGA: dla tego identyfikatora nie ma żadnych godzin — sprawdź go.");
        }
    }

    /// <summary>Plik JSON (host, port, dbname, user, password) → nowy wpis PostgreSQL. Pliku konta administratora nie wczytuje.</summary>
    private Task ImportAsync()
    {
        var dialog = new OpenFileDialog { Filter = "Plik połączenia (*.json)|*.json", Title = "Plik połączenia z bazą PostgreSQL" };
        if (dialog.ShowDialog() == true)
        {
            ImportFile(dialog.FileName);
        }

        return Task.CompletedTask;
    }

    internal bool ImportFile(string path)
    {
        if (Path.GetFileName(path).Contains("admin", StringComparison.OrdinalIgnoreCase))
        {
            SetStatus("To plik konta administratora — asystent tylko czyta bazę, więc go nie używa. Wybierz zwykłe konto.", error: true);
            return false;
        }

        try
        {
            var credential = ReadConnectionFile(path);
            if (credential is null)
            {
                SetStatus("W pliku nie ma pól user i password.", error: true);
                return false;
            }

            _vault.Save(credential);
            Reload();
            SetStatus("Wczytano „" + credential.Name + "” z " + Path.GetFileName(path) + " do Menedżera poświadczeń. Plik nie jest już potrzebny.", error: false);
            Changed?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or Win32Exception or ArgumentException)
        {
            SetStatus("Nie wczytałem pliku: " + e.Message, error: true);
            return false;
        }
    }

    internal static StoredCredential? ReadConnectionFile(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var json = document.RootElement;
        string? Get(string name) => json.TryGetProperty(name, out var v) ? (v.ValueKind == JsonValueKind.String ? v.GetString() : v.GetRawText()) : null;
        var user = Get("user");
        var password = Get("password");
        if (string.IsNullOrWhiteSpace(user) || string.IsNullOrEmpty(password))
        {
            return null;
        }

        var database = Get("dbname") ?? "";
        return new StoredCredential
        {
            Kind = CredentialKind.Postgres,
            Name = database.Length > 0 ? database : "baza",
            Host = Get("host") ?? "",
            Port = int.TryParse(Get("port"), out var port) ? port : null,
            Database = database,
            User = user,
            Password = password,
        };
    }

    /// <summary>
    /// Dane bazy z wersji do 0.4 (ustawienia + hasło w <c>klucze.json</c> pod kluczem <c>cde</c>, albo dawny plik połączenia)
    /// → wpis PostgreSQL „cde” w Menedżerze poświadczeń; potem ustawienia i klucz są czyszczone.
    /// </summary>
    private void MigrateOldSettings(AppSettings settings, CloudKeyStore keys, Action saveSettings)
    {
        try
        {
            const string name = "cde";
            if (settings.CdeUser is null && settings.CdeConnectionFile is null && settings.CdeHost is null && !keys.Has(name))
            {
                return;
            }

            if (_vault.Find(CredentialKind.Postgres, name) is null)
            {
                StoredCredential? credential = null;
                if (settings.CdeUser is not null && keys.Get(name) is { Length: > 0 } password)
                {
                    credential = new StoredCredential
                    {
                        Kind = CredentialKind.Postgres, Name = settings.CdeDatabase ?? name, Host = settings.CdeHost ?? "", Port = settings.CdePort,
                        Database = settings.CdeDatabase ?? name, User = settings.CdeUser, Password = password, PersonId = settings.CdePersonId ?? "",
                    };
                }
                else if (settings.CdeConnectionFile is { Length: > 0 } file && File.Exists(file) && !Path.GetFileName(file).Contains("admin", StringComparison.OrdinalIgnoreCase))
                {
                    credential = ReadConnectionFile(file);
                    if (credential is not null)
                    {
                        credential.PersonId = settings.CdePersonId ?? "";
                    }
                }

                var onlyDefaults = settings.CdeUser is null && settings.CdeConnectionFile is null && !keys.Has(name);
                if (credential is null && !onlyDefaults)
                {
                    return; // niepełne dawne dane (np. login bez hasła) — zostają, aż użytkownik wpisze poświadczenie
                }

                // Same dawne wartości domyślne (adres serwera bez loginu i hasła) nie są poświadczeniem — tylko je czyścimy.
                if (credential is not null)
                {
                    _vault.Save(credential);
                    SetStatus("Dane bazy z ustawień przeniesiono do Menedżera poświadczeń Windows (wpis „" + credential.Name + "”).", error: false);
                }
            }

            settings.CdeHost = null;
            settings.CdePort = null;
            settings.CdeDatabase = null;
            settings.CdeUser = null;
            settings.CdePersonId = null;
            settings.CdeConnectionFile = null;
            if (keys.Has(name))
            {
                keys.Set(name, null);
            }

            saveSettings();
        }
        catch (Exception e) when (e is Win32Exception or IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            SetStatus("Nie przeniosłem dawnych danych bazy: " + e.Message, error: true);
        }
    }

    private void SetStatus(string text, bool error)
    {
        Status = text;
        StatusIsError = error;
    }
}

/// <summary>
/// Wiersz listy poświadczeń, wszystko w jednej linii: typ, IP (z portem), nazwa bazy, user, hasło, person_id, ×.
/// Wpis rozpoznaje się po nazwie bazy (PostgreSQL) albo po adresie (FTP, SMB). Hasło tylko do wpisania — nie wraca do okna.
/// </summary>
public sealed class CredentialRow : ObservableObject
{
    private readonly CredentialsViewModel _owner;
    private CredentialKind _kind;
    private string _address = "";
    private string _database = "";
    private string _user = "";
    private string _personId = "";
    private string _newPassword = "";
    private bool _hasStoredPassword;
    private bool _isDirty;
    private string _state = "";
    private bool _stateIsError;

    public CredentialRow(CredentialsViewModel owner, StoredCredential? saved)
    {
        _owner = owner;
        if (saved is not null)
        {
            _kind = saved.Kind;
            _address = saved.Host + (saved.Port is { } port ? ":" + port : "");
            _database = saved.Database;
            _user = saved.User;
            _personId = saved.PersonId;
            _hasStoredPassword = saved.Password.Length > 0;
            SavedKind = saved.Kind;
            SavedName = saved.Name;
        }

        RemoveCommand = new RelayCommand(() => { _owner.Remove(this); return Task.CompletedTask; });
    }

    public RelayCommand RemoveCommand { get; }

    /// <summary>Rodzaj i nazwa w Menedżerze (null = jeszcze nie zapisano).</summary>
    public CredentialKind? SavedKind { get; private set; }

    public string? SavedName { get; private set; }

    public bool IsSaved => SavedName is not null;

    public bool IsDirty => _isDirty;

    public CredentialKind Kind
    {
        get => _kind;
        set
        {
            if (Set(ref _kind, value))
            {
                OnPropertyChanged(nameof(PostgresOnly));
                OnPropertyChanged(nameof(AddressHint));
            }
        }
    }

    /// <summary>„IP”: adres serwera z portem (<c>serwer:5432</c>); przy SMB <c>\serwer\udział</c>.</summary>
    public string Address { get => _address; set => Set(ref _address, value); }

    public string Database { get => _database; set => Set(ref _database, value); }

    public string User { get => _user; set => Set(ref _user, value); }

    public string PersonId { get => _personId; set => Set(ref _personId, value); }

    /// <summary>Nazwa wpisu: nazwa bazy (PostgreSQL) albo adres bez portu (FTP, SMB). Profil wskazuje ją polem <c>poswiadczenie</c>.</summary>
    public string Name => Kind == CredentialKind.Postgres ? _database.Trim() : SplitAddress(_address).Host.TrimStart('\\').Replace(":", "");

    /// <summary>Hasło wpisane w pole (z PasswordBox); puste = zostaje zapisane.</summary>
    public string NewPassword
    {
        get => _newPassword;
        set
        {
            if (Set(ref _newPassword, value))
            {
                OnPropertyChanged(nameof(PasswordHint));
            }
        }
    }

    /// <summary>Podpowiedź w pustym polu hasła.</summary>
    public string PasswordHint => _newPassword.Length > 0 ? "" : _hasStoredPassword ? "•••• zapisane" : "";

    /// <summary>Pola tylko dla PostgreSQL (nazwa bazy, person_id) — w innych wierszach ukryte, ale miejsce zostaje (kolumny równo).</summary>
    public Visibility PostgresOnly => Kind == CredentialKind.Postgres ? Visibility.Visible : Visibility.Hidden;

    public string AddressHint => Kind == CredentialKind.Smb ? @"\serwer\udział" : Kind == CredentialKind.Ftp ? "serwer:21" : "serwer:5432";

    /// <summary>Krótki stan na końcu wiersza: „zapisane”, czego brakuje, błąd.</summary>
    public string State { get => _state; private set => SetProperty(ref _state, value); }

    public bool StateIsError { get => _stateIsError; private set => SetProperty(ref _stateIsError, value); }

    internal bool HasAnyInput => new[] { _address, _database, _user, _personId, _newPassword }.Any(v => !string.IsNullOrWhiteSpace(v));

    internal void SetState(string text, bool error)
    {
        State = text;
        StateIsError = error;
    }

    /// <summary>„serwer:5432” → („serwer”, 5432); bez portu albo przy <c>\serwer\udział</c> port = null.</summary>
    internal static (string Host, int? Port) SplitAddress(string address)
    {
        var text = (address ?? "").Trim();
        var colon = text.LastIndexOf(':');
        if (colon > 0 && !text.StartsWith(@"\\", StringComparison.Ordinal) && text.IndexOf(':') == colon
            && int.TryParse(text[(colon + 1)..], out var port))
        {
            return (text[..colon], port);
        }

        return (text, null);
    }

    /// <summary>Czego brakuje do zapisu; null = można zapisać.</summary>
    internal string? Validate(IEnumerable<CredentialRow> all)
    {
        var missing = new List<string>();
        var (host, port) = SplitAddress(_address);
        if (host.Length == 0)
        {
            missing.Add("IP");
        }

        if (Kind == CredentialKind.Postgres && string.IsNullOrWhiteSpace(Database))
        {
            missing.Add("nazwa bazy");
        }

        if (string.IsNullOrWhiteSpace(User))
        {
            missing.Add("user");
        }

        if (!_hasStoredPassword && _newPassword.Length == 0)
        {
            missing.Add("hasło");
        }

        if (missing.Count > 0)
        {
            return "brak: " + string.Join(", ", missing);
        }

        if (port is < 1 or > 65535)
        {
            return "port 1–65535";
        }

        if (Name.Contains(':'))
        {
            return "nazwa bazy bez dwukropka";
        }

        return all.Any(r => !ReferenceEquals(r, this) && r.Kind == Kind && string.Equals(r.Name, Name, StringComparison.OrdinalIgnoreCase))
            ? "taki wpis już jest"
            : null;
    }

    internal StoredCredential ToCredential()
    {
        var (host, port) = SplitAddress(_address);
        return new StoredCredential
        {
            Kind = Kind,
            Name = Name,
            Host = host,
            Port = port,
            Database = Kind == CredentialKind.Postgres ? Database.Trim() : "",
            User = User.Trim(),
            Password = _newPassword,
            PersonId = Kind == CredentialKind.Postgres ? PersonId.Trim() : "",
        };
    }

    internal void MarkSaved(bool hasPassword)
    {
        SavedKind = Kind;
        SavedName = Name;
        _hasStoredPassword = hasPassword;
        _newPassword = "";
        NewPasswordCleared?.Invoke(this, EventArgs.Empty); // pole hasła czyści się (to samo puste hasło — wiersz nie jest zmieniony)
        _isDirty = false;
        OnPropertyChanged(nameof(PasswordHint));
    }

    /// <summary>Okno czyści wtedy pole hasła (PasswordBox nie ma wiązania).</summary>
    public event EventHandler? NewPasswordCleared;

    private bool Set<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (!SetProperty(ref field, value, name))
        {
            return false;
        }

        _isDirty = true;
        return true;
    }
}
