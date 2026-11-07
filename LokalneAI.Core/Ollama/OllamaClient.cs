using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace LokalneAI.Core.Ollama;

/// <summary>
/// Klient lokalnego API Ollamy. Odpowiedzi strumieniowe (czat, pobieranie modelu) przychodzą
/// jako NDJSON, czyli jeden obiekt JSON w każdej linii.
/// </summary>
public sealed class OllamaClient : IDisposable
{
    public const string DefaultUrl = "http://127.0.0.1:11434";

    private readonly HttpClient _http;

    public OllamaClient(string? baseUrl = null, HttpMessageHandler? handler = null)
    {
        BaseUrl = NormalizeUrl(baseUrl);
        _http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        _http.BaseAddress = new Uri(BaseUrl + "/");

        // Odpowiedź modelu może trwać minuty (ładowanie, długi tekst). Przerywa ją przycisk Stop,
        // a krótkie zapytania mają własny limit (MetadataTimeout).
        _http.Timeout = Timeout.InfiniteTimeSpan;
    }

    public string BaseUrl { get; }

    /// <summary>Limit dla krótkich zapytań (wersja, lista modeli). Po nim Ollama „nie odpowiada”.</summary>
    public TimeSpan MetadataTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Ujednolica adres: bez ukośnika na końcu, z <c>http://</c>, gdy go brak.</summary>
    /// <exception cref="ArgumentException">Adres nie jest poprawnym adresem http(s).</exception>
    public static string NormalizeUrl(string? url)
    {
        var value = (url ?? "").Trim().TrimEnd('/');
        if (value.Length == 0)
        {
            return DefaultUrl;
        }

        if (!value.Contains("://", StringComparison.Ordinal))
        {
            value = "http://" + value;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException($"Niepoprawny adres Ollamy: „{url}”. Przykład: {DefaultUrl}", nameof(url));
        }

        return value;
    }

    public async Task<string> GetVersionAsync(CancellationToken ct = default)
        => (await GetJsonAsync<VersionResponse>("api/version", ct).ConfigureAwait(false)).Version ?? "?";

    public async Task<IReadOnlyList<LocalModel>> ListModelsAsync(CancellationToken ct = default)
        => (await GetJsonAsync<TagsResponse>("api/tags", ct).ConfigureAwait(false)).Models;

    public async Task<IReadOnlyList<RunningModel>> ListRunningAsync(CancellationToken ct = default)
        => (await GetJsonAsync<PsResponse>("api/ps", ct).ConfigureAwait(false)).Models;

    /// <summary>Czat strumieniowy: kolejne fragmenty tekstu, na końcu fragment z <c>Done</c> i statystykami.</summary>
    public async IAsyncEnumerable<ChatChunk> ChatStreamAsync(ChatRequest request, [EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Stream = true;
        await foreach (var chunk in PostStreamAsync<ChatChunk>("api/chat", request, request.Model, ct).ConfigureAwait(false))
        {
            if (!string.IsNullOrEmpty(chunk.Error))
            {
                throw new OllamaException("Ollama przerwała odpowiedź: " + chunk.Error);
            }

            yield return chunk;
            if (chunk.Done)
            {
                yield break;
            }
        }
    }

    /// <summary>Pobiera model z rejestru Ollamy (jak <c>ollama pull</c>), z postępem.</summary>
    public async IAsyncEnumerable<PullProgress> PullAsync(string model, [EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        await foreach (var progress in PostStreamAsync<PullProgress>("api/pull", new { model, stream = true }, model, ct).ConfigureAwait(false))
        {
            if (!string.IsNullOrEmpty(progress.Error))
            {
                throw new OllamaException("Pobieranie modelu przerwane: " + progress.Error);
            }

            yield return progress;
            if (progress.Status == "success")
            {
                yield break;
            }
        }
    }

    /// <summary>Usuwa model z pamięci (karty graficznej i RAM) od razu, bez czekania na limit bezczynności.</summary>
    public async Task UnloadAsync(string model, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        using var message = new HttpRequestMessage(HttpMethod.Post, "api/generate") { Content = JsonBody(new { model, keep_alive = 0 }) };
        using var response = await SendAsync(message, HttpCompletionOption.ResponseContentRead, model, ct, ct).ConfigureAwait(false);
    }

    public void Dispose() => _http.Dispose();

    private async Task<T> GetJsonAsync<T>(string path, CancellationToken ct)
        where T : new()
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(MetadataTimeout);
        using var message = new HttpRequestMessage(HttpMethod.Get, path);
        using var response = await SendAsync(message, HttpCompletionOption.ResponseContentRead, null, limit.Token, ct).ConfigureAwait(false);
        string text;
        try
        {
            text = await response.Content.ReadAsStringAsync(limit.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException e) when (!ct.IsCancellationRequested)
        {
            throw new OllamaUnavailableException(BaseUrl, e);
        }

        try
        {
            return JsonSerializer.Deserialize<T>(text, Json.Api) ?? new T();
        }
        catch (JsonException e)
        {
            throw new OllamaException("Nieczytelna odpowiedź Ollamy: " + Shorten(text), null, e);
        }
    }

    private async IAsyncEnumerable<T> PostStreamAsync<T>(string path, object body, string? model, [EnumeratorCancellation] CancellationToken ct)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonBody(body) };
        using var response = await SendAsync(message, HttpCompletionOption.ResponseHeadersRead, model, ct, ct).ConfigureAwait(false);
        using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        while (true)
        {
            string? line;
            try
            {
                line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            }
            catch (IOException e) when (!ct.IsCancellationRequested)
            {
                throw new OllamaException("Połączenie z Ollamą urwało się w trakcie odpowiedzi.", null, e);
            }

            if (line is null)
            {
                yield break;
            }

            if (line.Length == 0)
            {
                continue;
            }

            T? item;
            try
            {
                item = JsonSerializer.Deserialize<T>(line, Json.Api);
            }
            catch (JsonException e)
            {
                throw new OllamaException("Nieczytelna odpowiedź Ollamy: " + Shorten(line), null, e);
            }

            if (item is not null)
            {
                yield return item;
            }
        }
    }

    /// <summary>Wysyła żądanie i zamienia błędy sieci i HTTP na <see cref="OllamaException"/>.</summary>
    /// <param name="token">Token żądania (może zawierać limit czasu).</param>
    /// <param name="userToken">Token użytkownika: jego anulowanie to Stop, a nie awaria Ollamy.</param>
    private async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage message, HttpCompletionOption option, string? model, CancellationToken token, CancellationToken userToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(message, option, token).ConfigureAwait(false);
        }
        catch (HttpRequestException e)
        {
            throw new OllamaUnavailableException(BaseUrl, e);
        }
        catch (OperationCanceledException e) when (!userToken.IsCancellationRequested)
        {
            throw new OllamaUnavailableException(BaseUrl, e);
        }

        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        using (response)
        {
            var body = "";
            try
            {
                body = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
            }
            catch (Exception e) when (e is IOException or HttpRequestException or OperationCanceledException)
            {
                // Treść błędu nieczytelna; wystarczy kod HTTP.
            }

            var detail = ReadError(body) ?? $"HTTP {(int)response.StatusCode}";
            if (response.StatusCode == HttpStatusCode.NotFound && model is not null
                && detail.Contains("not found", StringComparison.OrdinalIgnoreCase))
            {
                throw new ModelNotFoundException(model, detail);
            }

            throw new OllamaException("Ollama zgłosiła błąd: " + detail, response.StatusCode);
        }
    }

    private static StringContent JsonBody(object body)
        => new(JsonSerializer.Serialize(body, body.GetType(), Json.Api), Encoding.UTF8, "application/json");

    private static string? ReadError(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ErrorResponse>(body, Json.Api)?.Error ?? Shorten(body);
        }
        catch (JsonException)
        {
            return Shorten(body);
        }
    }

    private static string Shorten(string text) => text.Length <= 200 ? text : text[..200] + "…";
}
