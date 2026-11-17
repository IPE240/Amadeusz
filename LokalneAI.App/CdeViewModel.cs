using System.Text.Json;
using System.Windows.Controls;
using LokalneAI.Core.Storage;
using Microsoft.Win32;
using Npgsql;

namespace LokalneAI.App;

/// <summary>
/// NIEUŻYWANE od wersji 0.5 — zastąpione przez <see cref="CredentialsViewModel"/> (Menedżer poświadczeń Windows).
/// Plik zostaje do decyzji użytkownika (zasada: nie kasujemy bez pytania).
/// Dawniej: Ustawienia → „Baza CDE”: dane logowania do PostgreSQL dla profilu cde zakładki Code, zapamiętane na stałe.
/// Adres, port, baza, użytkownik i person_id są w ustawieniach; hasło — zaszyfrowane kontem Windows (<see cref="CloudKeyStore"/>,
/// klucz <c>cde</c>) w <c>%APPDATA%\LokalneAI</c>, poza repozytorium. Asystent i tak czyta bazę tylko w transakcji READ ONLY.
/// </summary>
public sealed class CdeViewModel : ObservableObject
{
    /// <summary>Klucz hasła w <see cref="CloudKeyStore"/>.</summary>
    public const string PasswordKey = "cde";

    /// <summary>Tu leży plik połączenia, z którego korzystał Claude (repozytorium CDE, plik poza gitem).</summary>
    private const string ClaudeConnectionFolder = @"C:\ODS\cde\cdeReposForAI\rozmowa_z_baza_cde";

    private readonly AppSettings _settings;
    private readonly CloudKeyStore _keys;
    private readonly Action _save;
    private string _host;
    private string _portText;
    private string _database;
    private string _user;
    private string _personId;
    private string _status = "";
    private bool _statusIsError;
    private bool _isBusy;

    public CdeViewModel(AppSettings settings, CloudKeyStore keys, Action save)
    {
        _settings = settings;
        _keys = keys;
        _save = save;
        _host = settings.CdeHost ?? "";
        _portText = settings.CdePort?.ToString() ?? "";
        _database = settings.CdeDatabase ?? "";
        _user = settings.CdeUser ?? "";
        _personId = settings.CdePersonId ?? "";
        SaveCommand = new RelayCommand<PasswordBox>(SaveAsync);
        ImportCommand = new RelayCommand(ImportAsync, () => !IsBusy);
        TestCommand = new RelayCommand(TestAsync, () => !IsBusy && _settings.CdeUser is not null && HasPassword);
        RemoveCommand = new RelayCommand(() => { RemovePassword(); return Task.CompletedTask; });
        MigrateOldConnectionFile();
    }

    public RelayCommand<PasswordBox> SaveCommand { get; }

    public RelayCommand ImportCommand { get; }

    public RelayCommand TestCommand { get; }

    public RelayCommand RemoveCommand { get; }

    public string Host { get => _host; set => SetProperty(ref _host, value); }

    public string PortText { get => _portText; set => SetProperty(ref _portText, value); }

    public string Database { get => _database; set => SetProperty(ref _database, value); }

    public string User { get => _user; set => SetProperty(ref _user, value); }

    /// <summary>Identyfikator pytającego w CDE: model pyta o „moje” godziny przez <c>{{ja}}</c>.</summary>
    public string PersonId { get => _personId; set => SetProperty(ref _personId, value); }

    public bool HasPassword => _keys.Has(PasswordKey);

    public string KeyStatus => HasPassword ? "Hasło zapisane (zaszyfrowane kontem Windows, poza repozytorium)." : "Brak hasła.";

    public string Status { get => _status; private set => SetProperty(ref _status, value); }

    public bool StatusIsError { get => _statusIsError; private set => SetProperty(ref _statusIsError, value); }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                System.Windows.Input.CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    /// <summary>Do nagłówka sekcji i zakładki Code: „użytkownik@serwer:port/baza”.</summary>
    public string Summary => _settings.CdeUser is null || !HasPassword
        ? "brak danych logowania"
        : $"{_settings.CdeUser}@{_settings.CdeHost}:{_settings.CdePort}/{_settings.CdeDatabase}"
          + (string.IsNullOrWhiteSpace(_settings.CdePersonId) ? " · brak person_id" : "");

    /// <summary>Połączenie z zapisanych danych albo null, gdy brakuje użytkownika lub hasła.</summary>
    public string? ConnectionString
    {
        get
        {
            var password = _keys.Get(PasswordKey);
            return _settings.CdeUser is null || string.IsNullOrEmpty(password) ? null : Build(_settings.CdeHost ?? "", _settings.CdePort ?? 5432, _settings.CdeDatabase ?? "", _settings.CdeUser, password);
        }
    }

    internal static string Build(string host, int port, string database, string user, string password)
        => new NpgsqlConnectionStringBuilder
        {
            Host = host,
            Port = port,
            Database = database,
            Username = user,
            Password = password,
            Timeout = 15,
            CommandTimeout = 30,
            ApplicationName = "LokalneAI",
        }.ConnectionString;

    private async Task SaveAsync(PasswordBox? box)
    {
        if (!TryApplyFields(out var problem))
        {
            SetStatus(problem, error: true);
            return;
        }

        var password = box?.Password ?? "";
        if (password.Length > 0)
        {
            if (!StorePassword(password))
            {
                return;
            }

            box!.Clear();
        }
        else if (!HasPassword)
        {
            SetStatus("Wpisz hasło (albo wczytaj plik połączenia).", error: true);
            return;
        }

        Changed();
        await TestAsync();
    }

    /// <summary>Wczytuje plik JSON (host, port, dbname, user, password) — np. <c>polaczenie.local.json</c>, z którego korzystał Claude.</summary>
    private async Task ImportAsync()
    {
        var dialog = new OpenFileDialog { Filter = "Plik połączenia (*.json)|*.json", Title = "Plik połączenia z bazą CDE" };
        if (Directory.Exists(ClaudeConnectionFolder))
        {
            dialog.InitialDirectory = ClaudeConnectionFolder;
            dialog.FileName = "polaczenie.local.json";
        }

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        if (await ImportFileAsync(dialog.FileName))
        {
            await TestAsync();
        }
    }

    /// <summary>Wczytanie pliku połączenia bez okna (także dla dawnego ustawienia „plik połączenia” zakładki Code).</summary>
    internal Task<bool> ImportFileAsync(string path)
    {
        if (Path.GetFileName(path).Contains("admin", StringComparison.OrdinalIgnoreCase))
        {
            SetStatus("To plik konta administratora — asystent tylko czyta bazę, więc go nie używa. Wybierz zwykłe konto (np. polaczenie.local.json).", error: true);
            return Task.FromResult(false);
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var json = document.RootElement;
            string? Get(string name) => json.TryGetProperty(name, out var v) ? (v.ValueKind == JsonValueKind.String ? v.GetString() : v.GetRawText()) : null;
            var user = Get("user");
            var password = Get("password");
            if (string.IsNullOrWhiteSpace(user) || string.IsNullOrEmpty(password))
            {
                SetStatus("W pliku nie ma pól user i password.", error: true);
                return Task.FromResult(false);
            }

            Host = Get("host") ?? Host;
            PortText = Get("port") ?? PortText;
            Database = Get("dbname") ?? Database;
            User = user;
            if (!TryApplyFields(out var problem))
            {
                SetStatus(problem, error: true);
                return Task.FromResult(false);
            }

            if (!StorePassword(password))
            {
                return Task.FromResult(false);
            }

            Changed();
            SetStatus("Wczytano dane z " + Path.GetFileName(path) + " i zapisano w programie. Plik nie jest już potrzebny.", error: false);
            return Task.FromResult(true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            SetStatus("Nie wczytałem pliku: " + e.Message, error: true);
            return Task.FromResult(false);
        }
    }

    /// <summary>Łączy się (tylko odczyt) i sprawdza, czy person_id ma wpisy godzin. Pierwsze połączenie z bazą bywa zimne — jedno ponowienie.</summary>
    private async Task TestAsync()
    {
        var connection = ConnectionString;
        if (connection is null)
        {
            SetStatus("Najpierw zapisz użytkownika i hasło.", error: true);
            return;
        }

        IsBusy = true;
        SetStatus("Łączę się z bazą…", error: false);
        try
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    SetStatus(await CheckAsync(connection, _settings.CdePersonId), error: false);
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
            SetStatus("Nie połączyłem się: " + e.Message, error: true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static async Task<string> CheckAsync(string connectionString, string? personId)
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
            who = "Połączono jako " + reader.GetString(0) + " (PostgreSQL " + reader.GetString(1) + ").";
        }

        if (string.IsNullOrWhiteSpace(personId))
        {
            await transaction.RollbackAsync();
            return who + " Wpisz jeszcze swój person_id — bez niego asystent nie odpowie o Twoich godzinach.";
        }

        await using (var command = new NpgsqlCommand("SELECT EXISTS (SELECT 1 FROM timesheet.v_my_hours h WHERE h.person_id::text = @p)", connection, transaction))
        {
            command.Parameters.AddWithValue("p", personId.Trim());
            var found = (bool)(await command.ExecuteScalarAsync() ?? false);
            await transaction.RollbackAsync();
            return who + (found ? " Twój person_id ma wpisy godzin." : " UWAGA: dla tego person_id nie ma żadnych godzin — sprawdź identyfikator.");
        }
    }

    private bool TryApplyFields(out string problem)
    {
        problem = "";
        if (!int.TryParse(PortText.Trim(), out var port) || port is < 1 or > 65535)
        {
            problem = "Port to liczba, np. 5432.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(User))
        {
            problem = "Wpisz użytkownika bazy.";
            return false;
        }

        _settings.CdeHost = string.IsNullOrWhiteSpace(Host) ? null : Host.Trim();
        _settings.CdePort = port;
        _settings.CdeDatabase = string.IsNullOrWhiteSpace(Database) ? "cde" : Database.Trim();
        _settings.CdeUser = User.Trim();
        _settings.CdePersonId = string.IsNullOrWhiteSpace(PersonId) ? null : PersonId.Trim();
        return true;
    }

    private bool StorePassword(string password)
    {
        try
        {
            _keys.Set(PasswordKey, password);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        {
            SetStatus("Nie zapisałem hasła: " + e.Message, error: true);
            return false;
        }
    }

    private void RemovePassword()
    {
        try
        {
            _keys.Set(PasswordKey, null);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            SetStatus("Nie usunąłem hasła: " + e.Message, error: true);
            return;
        }

        Changed();
        SetStatus("Usunięto hasło z programu.", error: false);
    }

    /// <summary>Dawne ustawienie zakładki Code (ścieżka do pliku JSON) → dane zapamiętane w programie, raz.</summary>
    private void MigrateOldConnectionFile()
    {
        if (_settings.CdeUser is null && !HasPassword && _settings.CdeConnectionFile is { Length: > 0 } file && File.Exists(file))
        {
            _ = ImportFileAsync(file);
        }
    }

    private void Changed()
    {
        _save();
        OnPropertyChanged(nameof(HasPassword));
        OnPropertyChanged(nameof(KeyStatus));
        OnPropertyChanged(nameof(Summary));
        SummaryChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Zmieniły się dane logowania (zakładka Code odświeża opis).</summary>
    public event EventHandler? SummaryChanged;

    private void SetStatus(string text, bool error)
    {
        Status = text;
        StatusIsError = error;
    }
}
