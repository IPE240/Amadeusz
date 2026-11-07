using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LokalneAI.Harness.Profiles;

namespace LokalneAI.Harness.Tools
{
    /// <summary>Wywołanie narzędzia zaproponowane przez model i sprawdzone przez bibliotekę.</summary>
    public sealed class ToolCall
    {
        public ToolCall(ToolDefinition definition, JsonElement arguments, int step, int index)
        {
            Definition = definition;
            Arguments = arguments;
            Step = step;
            Index = index;
        }

        public ToolDefinition Definition { get; }

        public string Name => Definition.Name;

        /// <summary>Argumenty po podstawieniu zmiennych sesji (np. <c>{{ja}}</c>).</summary>
        public JsonElement Arguments { get; }

        public string ArgumentsJson => Text.Compact(Arguments);

        /// <summary>Numer kroku modelu w turze (od 1).</summary>
        public int Step { get; }

        /// <summary>Kolejność w kroku (od 1).</summary>
        public int Index { get; }

        public string GetString(string name) => Arguments.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        public double? GetNumber(string name) => Arguments.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : (double?)null;

        /// <summary>Treść pytania o zgodę przygotowana przez wykonawcę (<see cref="IConfirmingExecutor"/>); null = opis domyślny.</summary>
        public string ConfirmationMessage { get; internal set; }

        /// <summary>Polecenie może coś zniszczyć — okno pytania powinno domyślnie odmawiać.</summary>
        public bool ConfirmationDangerous { get; internal set; }

        /// <summary>Czy wywołanie coś zmienia (wymagało zgody). Ustala to sesja przed wykonaniem.</summary>
        public bool Mutating { get; internal set; }

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

        public bool Success { get; }

        public string Text { get; }

        public static ToolResult Ok(string text) => new ToolResult(true, text);

        public static ToolResult Error(string text) => new ToolResult(false, text);

        public override string ToString() => (Success ? "" : "[błąd] ") + Text;
    }

    /// <summary>
    /// Wykonawca dostarczany przez gospodarza: tu polecenia trafiają do Tekli, bazy itd.
    /// Wyjątek z wykonawcy biblioteka zamienia na wynik z błędem i oddaje modelowi.
    /// </summary>
    public interface IToolExecutor
    {
        Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken cancellationToken);
    }
}
