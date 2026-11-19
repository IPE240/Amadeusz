using System.Text;
using System.Text.RegularExpressions;

namespace LokalneAI.Gateway;

/// <summary>Suma tokenów policzonych na tym PC dla jednego komputera (wiersz w Ustawieniach → Kolejka).</summary>
public sealed record TokenTotal
{
    /// <summary>Nazwa komputera, który pytał (z nagłówka <c>X-LokalneAI-Komputer</c>, bez niego adres IP).</summary>
    public string Computer { get; init; } = "";

    /// <summary>Ostatni adres IP, z którego pytał.</summary>
    public string? Address { get; init; }

    /// <summary>Zapytanie z tego PC (czat, zakładka Code, inne programy na tym komputerze).</summary>
    public bool IsThisComputer { get; init; }

    /// <summary>Ile zapytań policzył dla niego ten PC.</summary>
    public long Requests { get; init; }

    /// <summary>Tokeny przeczytane przez model (prompt; Ollama: <c>prompt_eval_count</c>, API OpenAI: <c>prompt_tokens</c>).</summary>
    public long PromptTokens { get; init; }

    /// <summary>Tokeny napisane przez model (Ollama: <c>eval_count</c>, API OpenAI: <c>completion_tokens</c>).</summary>
    public long OutputTokens { get; init; }

    /// <summary>Kiedy ostatnio.</summary>
    public DateTimeOffset LastUsed { get; init; }
}

/// <summary>
/// Liczniki tokenów bramy: ile model tego PC przeczytał i napisał dla każdego komputera (i dla siebie). Liczby są
/// dokładne — z końca odpowiedzi serwera modelu, którą brama i tak przepuszcza. Zapytania przekazane do innego komputera
/// („Gdzie liczyć”) liczy tamten komputer, nie ten. Bezpieczne wątkowo.
/// </summary>
public sealed class TokenTally
{
    private readonly object _lock = new();
    private readonly Dictionary<string, TokenTotal> _totals = new(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset _since = DateTimeOffset.Now;

    /// <summary>Liczniki się zmieniły (wątek w tle).</summary>
    public event EventHandler? Changed;

    /// <summary>Od kiedy liczy (start bez zapisanych liczników albo ostatni <see cref="Reset"/>).</summary>
    public DateTimeOffset Since
    {
        get
        {
            lock (_lock)
            {
                return _since;
            }
        }
    }

    /// <summary>Dolicza jedno zapytanie.</summary>
    public void Add(string computer, string? address, bool isThisComputer, int promptTokens, int outputTokens, DateTimeOffset when)
    {
        var key = isThisComputer ? "" : computer;
        lock (_lock)
        {
            _totals.TryGetValue(key, out var old);
            _totals[key] = new TokenTotal
            {
                Computer = isThisComputer ? "" : computer,
                Address = address ?? old?.Address,
                IsThisComputer = isThisComputer,
                Requests = (old?.Requests ?? 0) + 1,
                PromptTokens = (old?.PromptTokens ?? 0) + Math.Max(0, promptTokens),
                OutputTokens = (old?.OutputTokens ?? 0) + Math.Max(0, outputTokens),
                LastUsed = when,
            };
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Liczniki: najpierw ten PC, potem komputery od największej liczby tokenów.</summary>
    public IReadOnlyList<TokenTotal> Snapshot()
    {
        lock (_lock)
        {
            return _totals.Values
                .OrderByDescending(t => t.IsThisComputer)
                .ThenByDescending(t => t.PromptTokens + t.OutputTokens)
                .ThenBy(t => t.Computer, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    /// <summary>Wczytuje zapisane liczniki (po uruchomieniu okna); zastępuje obecne.</summary>
    public void Load(IEnumerable<TokenTotal> totals, DateTimeOffset since)
    {
        ArgumentNullException.ThrowIfNull(totals);
        lock (_lock)
        {
            _totals.Clear();
            foreach (var total in totals)
            {
                _totals[total.IsThisComputer ? "" : total.Computer] = total;
            }

            _since = since;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Zeruje liczniki (przycisk „Resetuj”).</summary>
    public void Reset()
    {
        lock (_lock)
        {
            _totals.Clear();
            _since = DateTimeOffset.Now;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>
/// Ostatnie kilka KB odpowiedzi serwera modelu i odczyt z nich liczby tokenów. Liczby stoją na końcu odpowiedzi: w ostatniej
/// linii strumienia Ollamy (<c>"done": true</c>), na końcu odpowiedzi bez strumienia i w polu <c>usage</c> API OpenAI.
/// </summary>
internal sealed class ResponseTail
{
    private const int Size = 8 * 1024;

    // Cudzysłów tuż za nazwą pola: w treści odpowiedzi modelu cudzysłowy są poprzedzone ukośnikiem, więc się nie liczą.
    private static readonly Regex PromptCount = new("\"(?:prompt_eval_count|prompt_tokens)\"\\s*:\\s*(\\d+)", RegexOptions.CultureInvariant);
    private static readonly Regex OutputCount = new("\"(?:eval_count|completion_tokens)\"\\s*:\\s*(\\d+)", RegexOptions.CultureInvariant);

    private readonly byte[] _buffer = new byte[Size];
    private int _length;

    public void Append(ReadOnlySpan<byte> data)
    {
        if (data.Length >= Size)
        {
            data[^Size..].CopyTo(_buffer);
            _length = Size;
            return;
        }

        var overflow = _length + data.Length - Size;
        if (overflow > 0)
        {
            Buffer.BlockCopy(_buffer, overflow, _buffer, 0, _length - overflow);
            _length -= overflow;
        }

        data.CopyTo(_buffer.AsSpan(_length));
        _length += data.Length;
    }

    /// <summary>Tokeny promptu i odpowiedzi z ostatniego wystąpienia pól; 0, gdy serwer ich nie podał.</summary>
    public (int Prompt, int Output) Read()
    {
        var text = Encoding.UTF8.GetString(_buffer, 0, _length);
        return (Last(PromptCount, text), Last(OutputCount, text));
    }

    private static int Last(Regex regex, string text)
    {
        var matches = regex.Matches(text);
        return matches.Count > 0 && int.TryParse(matches[^1].Groups[1].Value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? value
            : 0;
    }
}
