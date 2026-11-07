using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace LokalneAI.Harness.Tools
{
    /// <summary>Wykonawca z funkcji — najprostsze podpięcie gospodarza.</summary>
    public sealed class DelegateExecutor : IToolExecutor
    {
        private readonly Func<ToolCall, CancellationToken, Task<ToolResult>> _execute;

        /// <summary>Tworzy wykonawcę z funkcji.</summary>
        public DelegateExecutor(Func<ToolCall, CancellationToken, Task<ToolResult>> execute)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        }

        /// <inheritdoc/>
        public Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken cancellationToken) => _execute(call, cancellationToken);
    }

    /// <summary>Kieruje wywołania do różnych wykonawców według nazwy narzędzia.</summary>
    public sealed class RoutingExecutor : IToolExecutor, IConfirmingExecutor
    {
        private readonly Dictionary<string, IToolExecutor> _routes = new Dictionary<string, IToolExecutor>(StringComparer.Ordinal);
        private readonly IToolExecutor _fallback;

        /// <summary>Tworzy rozdzielacz. <paramref name="fallback"/> dostaje polecenia bez własnej trasy; null = błąd „nieobsługiwane” dla modelu.</summary>
        public RoutingExecutor(IToolExecutor fallback = null)
        {
            _fallback = fallback;
        }

        /// <summary>Kieruje narzędzie o tej nazwie do wykonawcy. Zwraca ten sam rozdzielacz, więc wywołania można łączyć.</summary>
        public RoutingExecutor Route(string toolName, IToolExecutor executor)
        {
            _routes[toolName] = executor;
            return this;
        }

        /// <inheritdoc/>
        public Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken cancellationToken)
        {
            if (_routes.TryGetValue(call.Name, out var executor))
            {
                return executor.ExecuteAsync(call, cancellationToken);
            }

            return _fallback != null
                ? _fallback.ExecuteAsync(call, cancellationToken)
                : Task.FromResult(ToolResult.Error("Gospodarz nie obsługuje polecenia „" + call.Name + "”."));
        }

        /// <summary>Decyzję podejmuje wykonawca, do którego trafi polecenie; null = stałe <c>zmienia_model</c>.</summary>
        public ConfirmationDecision Decide(ToolCall call)
        {
            var target = _routes.TryGetValue(call.Name, out var executor) ? executor : _fallback;
            return (target as IConfirmingExecutor)?.Decide(call);
        }
    }

    /// <summary>
    /// Symulator modelu konstrukcji do prób bez Tekli. Rozpoznaje polecenia po nazwie:
    /// <c>wstaw_*</c> tworzy element z nowym id, <c>usun*</c> usuwa, <c>zmien_*</c>/<c>popraw*</c>/<c>przesun*</c>
    /// zmienia, <c>wyselekcjonuj*</c>/<c>zaznacz*</c> wybiera po typie i poziomie, <c>policz*</c> liczy,
    /// reszta zwraca opis stanu. Niczego nie liczy statycznie — to tylko atrapa do rozmowy.
    /// </summary>
    public sealed class SimulatedModelExecutor : IToolExecutor
    {
        private readonly Dictionary<int, Dictionary<string, string>> _objects = new Dictionary<int, Dictionary<string, string>>();
        private readonly List<int> _selection = new List<int>();
        private int _nextId;

        /// <summary>Tworzy pusty symulowany model; pierwszy wstawiony element dostanie <paramref name="firstId"/>.</summary>
        public SimulatedModelExecutor(int firstId = 1001)
        {
            _nextId = firstId;
        }

        /// <summary>Elementy symulowanego modelu: id → właściwości.</summary>
        public IReadOnlyDictionary<int, Dictionary<string, string>> Objects => _objects;

        /// <inheritdoc/>
        public Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken cancellationToken)
        {
            var name = call.Name;
            var args = Flatten(call.Arguments);
            ToolResult result;
            if (name.StartsWith("wstaw", StringComparison.Ordinal) || name.StartsWith("dodaj", StringComparison.Ordinal))
            {
                var id = _nextId++;
                args["typ"] = args.TryGetValue("typ", out var t) ? t : name.Substring(name.IndexOf('_') + 1);
                _objects[id] = args;
                result = ToolResult.Ok("OK id=" + id + " (" + name + ")");
            }
            else if (TryGetId(args, out var id) && !_objects.ContainsKey(id) && !name.StartsWith("wyselekcjonuj", StringComparison.Ordinal))
            {
                result = ToolResult.Error("BLAD: nie ma elementu id=" + id);
            }
            else if (name.StartsWith("usun", StringComparison.Ordinal) && TryGetId(args, out id))
            {
                _objects.Remove(id);
                result = ToolResult.Ok("OK usunięto id=" + id);
            }
            else if ((name.StartsWith("zmien", StringComparison.Ordinal) || name.StartsWith("popraw", StringComparison.Ordinal)
                || name.StartsWith("przesun", StringComparison.Ordinal) || name.StartsWith("obroc", StringComparison.Ordinal)) && TryGetId(args, out id))
            {
                foreach (var pair in args)
                {
                    _objects[id][pair.Key] = pair.Value;
                }

                result = ToolResult.Ok("OK zmieniono id=" + id);
            }
            else if (name.StartsWith("wyselekcjonuj", StringComparison.Ordinal) || name.StartsWith("zaznacz", StringComparison.Ordinal))
            {
                var found = _objects.Where(o => Matches(o.Value, args)).Select(o => o.Key).ToList();
                _selection.Clear();
                _selection.AddRange(found);
                result = ToolResult.Ok("OK zaznaczono " + found.Count + (found.Count > 0 ? ": id=" + string.Join(", ", found) : ""));
            }
            else if (name.StartsWith("policz", StringComparison.Ordinal))
            {
                result = ToolResult.Ok("OK liczba=" + _objects.Count(o => Matches(o.Value, args)));
            }
            else if (TryGetId(args, out id) && _objects.TryGetValue(id, out var obj))
            {
                result = ToolResult.Ok("OK id=" + id + ": " + string.Join(", ", obj.Select(p => p.Key + "=" + p.Value)));
            }
            else
            {
                result = ToolResult.Ok("OK (symulator) " + name);
            }

            return Task.FromResult(result);
        }

        private static bool Matches(Dictionary<string, string> obj, Dictionary<string, string> filter)
        {
            foreach (var pair in filter)
            {
                if (pair.Key.StartsWith("tolerancja", StringComparison.Ordinal) || pair.Key == "id")
                {
                    continue;
                }

                if (pair.Key.StartsWith("poziom", StringComparison.Ordinal))
                {
                    var level = Parse(pair.Value);
                    var tolerance = filter.TryGetValue("tolerancja_m", out var tol) ? Parse(tol) : 0.01;
                    var levels = obj.Where(o => o.Key.StartsWith("poziom", StringComparison.Ordinal)).Select(o => Parse(o.Value));
                    if (!levels.Any(l => Math.Abs(l - level) <= tolerance))
                    {
                        return false;
                    }
                }
                else if (obj.TryGetValue(pair.Key, out var value) && !string.Equals(value, pair.Value, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }

        private static double Parse(string text)
            => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : double.NaN;

        private static bool TryGetId(Dictionary<string, string> args, out int id)
        {
            id = 0;
            return args.TryGetValue("id", out var text) && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out id);
        }

        private static Dictionary<string, string> Flatten(JsonElement arguments)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            if (arguments.ValueKind == JsonValueKind.Object)
            {
                foreach (var p in arguments.EnumerateObject())
                {
                    map[p.Name] = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : p.Value.GetRawText();
                }
            }

            return map;
        }
    }

    /// <summary>
    /// Wykonawca do testów: oczekiwane wywołania z gotowymi wynikami. Wywołanie zgodne z oczekiwanym dostaje jego
    /// wynik, niezgodne — błąd. Zapamiętuje wszystkie wywołania (do oceny modelu).
    /// </summary>
    public sealed class ScriptedExecutor : IToolExecutor
    {
        private readonly List<ExpectedCall> _pending = new List<ExpectedCall>();

        /// <summary>Wszystkie wywołania od modelu, po kolei.</summary>
        public List<ToolCall> Received { get; } = new List<ToolCall>();

        /// <summary>Oczekiwane wywołania, które się zgodziły.</summary>
        public List<ExpectedCall> Matched { get; } = new List<ExpectedCall>();

        /// <summary>Ustawia oczekiwane wywołania i czyści poprzednie wyniki.</summary>
        public void Expect(IEnumerable<ExpectedCall> calls)
        {
            _pending.Clear();
            _pending.AddRange(calls);
            Received.Clear();
            Matched.Clear();
        }

        /// <inheritdoc/>
        public Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken cancellationToken)
        {
            Received.Add(call);
            var match = _pending.FirstOrDefault(e => e.Matches(call));
            if (match == null)
            {
                return Task.FromResult(ToolResult.Error("BLAD: symulator nie oczekiwał wywołania " + call));
            }

            _pending.Remove(match);
            Matched.Add(match);
            return Task.FromResult(ToolResult.Ok(match.Result ?? "OK"));
        }
    }

    /// <summary>Oczekiwane wywołanie: nazwa i argumenty, które muszą się zgadzać (liczby z tolerancją, teksty bez wielkości liter).</summary>
    public sealed class ExpectedCall
    {
        /// <summary>Nazwa narzędzia.</summary>
        public string Tool { get; set; }

        /// <summary>Argumenty, które muszą się zgadzać; pozostałe mogą być dowolne. Brak = dowolne argumenty.</summary>
        public JsonElement Arguments { get; set; }

        /// <summary>Wynik zwracany modelowi; null = „OK”.</summary>
        public string Result { get; set; }

        /// <summary>Czy wywołanie pasuje: ta sama nazwa i zgodne podane argumenty.</summary>
        public bool Matches(ToolCall call)
        {
            if (!string.Equals(call.Name, Tool, StringComparison.Ordinal))
            {
                return false;
            }

            if (Arguments.ValueKind != JsonValueKind.Object)
            {
                return true;
            }

            foreach (var expected in Arguments.EnumerateObject())
            {
                if (!call.Arguments.TryGetProperty(expected.Name, out var actual) || !Same(expected.Value, actual))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Nazwa i oczekiwane argumenty.</summary>
        public override string ToString() => Tool + " " + (Arguments.ValueKind == JsonValueKind.Undefined ? "{}" : Text.Compact(Arguments));

        internal static bool Same(JsonElement expected, JsonElement actual)
        {
            if (expected.ValueKind == JsonValueKind.Number && actual.ValueKind == JsonValueKind.Number)
            {
                var e = expected.GetDouble();
                return Math.Abs(e - actual.GetDouble()) <= Math.Max(1e-6, Math.Abs(e) * 1e-6);
            }

            if (expected.ValueKind == JsonValueKind.String && actual.ValueKind == JsonValueKind.String)
            {
                return string.Equals(Normalize(expected.GetString()), Normalize(actual.GetString()), StringComparison.OrdinalIgnoreCase);
            }

            if (expected.ValueKind == JsonValueKind.Array && actual.ValueKind == JsonValueKind.Array)
            {
                var a = expected.EnumerateArray().ToList();
                var b = actual.EnumerateArray().ToList();
                return a.Count == b.Count && a.Zip(b, Same).All(x => x);
            }

            if (expected.ValueKind == JsonValueKind.Object && actual.ValueKind == JsonValueKind.Object)
            {
                return expected.EnumerateObject().All(p => actual.TryGetProperty(p.Name, out var v) && Same(p.Value, v));
            }

            return expected.ValueKind == actual.ValueKind && expected.GetRawText() == actual.GetRawText();
        }

        private static string Normalize(string text)
        {
            var builder = new StringBuilder();
            foreach (var c in text ?? "")
            {
                if (!char.IsWhiteSpace(c))
                {
                    builder.Append(c);
                }
            }

            return builder.ToString();
        }
    }
}
