using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LokalneAI.Harness.Profiles;

namespace LokalneAI.Harness.Tools
{
    /// <summary>Wywołanie narzędzia zaproponowane przez model i sprawdzone przez bibliotekę.</summary>
    public sealed class ToolCall
    {
        /// <summary>Tworzy wywołanie. W programie robi to sesja; ręcznie przydaje się w testach wykonawcy.</summary>
        public ToolCall(ToolDefinition definition, JsonElement arguments, int step, int index)
        {
            Definition = definition;
            Arguments = arguments;
            Step = step;
            Index = index;
        }

        /// <summary>Definicja narzędzia z profilu.</summary>
        public ToolDefinition Definition { get; }

        /// <summary>Nazwa narzędzia, np. <c>wstaw_slup</c>.</summary>
        public string Name => Definition.Name;

        /// <summary>Argumenty po podstawieniu zmiennych sesji (np. <c>{{ja}}</c>).</summary>
        public JsonElement Arguments { get; }

        /// <summary>Argumenty jako zwarty JSON.</summary>
        public string ArgumentsJson => Text.Compact(Arguments);

        /// <summary>Numer kroku modelu w turze (od 1).</summary>
        public int Step { get; }

        /// <summary>Kolejność w kroku (od 1).</summary>
        public int Index { get; }

        /// <summary>Argument tekstowy albo null, gdy go nie ma albo nie jest tekstem.</summary>
        public string GetString(string name) => Arguments.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        /// <summary>Argument liczbowy (także całkowity) albo null, gdy go nie ma albo nie jest liczbą.</summary>
        public double? GetNumber(string name) => Arguments.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : (double?)null;

        /// <summary>Argument tak/nie albo null, gdy go nie ma albo nie jest wartością logiczną.</summary>
        public bool? GetBool(string name)
            => Arguments.TryGetProperty(name, out var v) && (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False) ? v.GetBoolean() : (bool?)null;

        /// <summary>Treść pytania o zgodę przygotowana przez wykonawcę (<see cref="IConfirmingExecutor"/>); null = opis domyślny.</summary>
        public string ConfirmationMessage { get; internal set; }

        /// <summary>Polecenie może coś zniszczyć — okno pytania powinno domyślnie odmawiać.</summary>
        public bool ConfirmationDangerous { get; internal set; }

        /// <summary>
        /// Folder spoza dozwolonych, o który sesja pyta użytkownika (<see cref="ConfirmationMessage"/> to treść pytania); null = zwykła zgoda.
        /// Gospodarz powinien zapytać zawsze, także w trybie „bez pytania”, bo zgoda poszerza dostęp (<see cref="FolderAccess"/>).
        /// </summary>
        public string AccessFolder { get; internal set; }

        /// <summary>Czy wywołanie coś zmienia (wymagało zgody). Ustala to sesja przed wykonaniem.</summary>
        public bool Mutating { get; internal set; }

        /// <summary>Np. „wstaw_slup {"x_mm":6000}”.</summary>
        public override string ToString() => Name + " " + ArgumentsJson;
    }

    /// <summary>Wynik wykonania narzędzia — tekst trafia do modelu.</summary>
    public sealed class ToolResult
    {
        private ToolResult(bool success, string text)
        {
            Success = success;
            Text = text ?? "";
        }

        /// <summary>Czy polecenie się udało.</summary>
        public bool Success { get; }

        /// <summary>Tekst wyniku dla modelu.</summary>
        public string Text { get; }

        /// <summary>
        /// Udało się. Tekst wraca do modelu, więc ma być krótki i konkretny. Po wstawieniu elementu dopisz <c>id=&lt;liczba&gt;</c>:
        /// model użyje go w poleceniach „zmień go…”.
        /// </summary>
        public static ToolResult Ok(string text) => new ToolResult(true, text);

        /// <summary>Nie udało się. Napisz zdanie, co poszło nie tak i czego brakuje: model poprawi wywołanie albo zapyta użytkownika.</summary>
        public static ToolResult Error(string text) => new ToolResult(false, text);

        /// <summary>Tekst wyniku; przy błędzie z „[błąd] ” na początku.</summary>
        public override string ToString() => (Success ? "" : "[błąd] ") + Text;
    }

    /// <summary>
    /// Wykonawca dostarczany przez gospodarza: tu polecenia trafiają do Tekli, bazy itd.
    /// Wyjątek z wykonawcy biblioteka zamienia na wynik z błędem i oddaje modelowi.
    /// </summary>
    public interface IToolExecutor
    {
        /// <summary>
        /// Wykonuje sprawdzone wywołanie i zwraca wynik dla modelu. Wołane z wątku w tle: jeśli API gospodarza wymaga wątku okna,
        /// przełącz się sam (Dispatcher, Control.Invoke). Wyjątek biblioteka zamienia na wynik z błędem dla modelu.
        /// </summary>
        Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken cancellationToken);
    }
}
