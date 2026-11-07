using System.Net;
using System.Net.Sockets;
using System.Text;

namespace LokalneAI.Gateway;

/// <summary>
/// Wyszukiwanie bram w podsieci: klient rozsyła po UDP pytanie „kto udostępnia model?”, a bramy z włączonym
/// udostępnianiem odpowiadają swoim <see cref="PeerInfo"/>. Działa tylko w obrębie podsieci (rozgłoszenia
/// nie przechodzą przez routery).
/// </summary>
public sealed class PeerDiscovery : IDisposable
{
    /// <summary>Treść pytania.</summary>
    public const string Question = "LOKALNEAI?1";

    private readonly UdpClient _udp;
    private readonly Func<PeerInfo> _describe;
    private readonly Func<IPAddress, bool> _allowed;
    private readonly CancellationTokenSource _stop = new();

    private PeerDiscovery(UdpClient udp, Func<PeerInfo> describe, Func<IPAddress, bool> allowed)
    {
        _udp = udp;
        _describe = describe;
        _allowed = allowed;
        _ = Task.Run(ListenAsync);
    }

    /// <summary>
    /// Odpowiada na pytania z adresów, które przepuszcza <paramref name="allowed"/>. Null, gdy port UDP jest zajęty.
    /// <paramref name="bindTo"/>: null = wszystkie karty sieciowe; 127.0.0.1 = próba na jednym komputerze.
    /// </summary>
    public static PeerDiscovery? TryStartResponder(int port, Func<PeerInfo> describe, Func<IPAddress, bool> allowed, IPAddress? bindTo = null)
    {
        try
        {
            var udp = new UdpClient(AddressFamily.InterNetwork);
            udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, false);
            IgnoreConnectionReset(udp);
            udp.Client.Bind(new IPEndPoint(bindTo ?? IPAddress.Any, port));
            return new PeerDiscovery(udp, describe, allowed);
        }
        catch (SocketException)
        {
            return null;
        }
    }

    /// <summary>
    /// Rozsyła pytanie do wszystkich podsieci tego PC (i do tego PC) i zbiera odpowiedzi przez <paramref name="wait"/>.
    /// Zwraca bramy posortowane po nazwie, bez powtórzeń.
    /// </summary>
    public static async Task<IReadOnlyList<PeerInfo>> FindAsync(TimeSpan wait, int port = GatewayOptions.DefaultDiscoveryPort, CancellationToken cancellationToken = default)
    {
        var found = new Dictionary<string, PeerInfo>(StringComparer.OrdinalIgnoreCase);
        using var udp = new UdpClient(AddressFamily.InterNetwork) { EnableBroadcast = true };
        IgnoreConnectionReset(udp);
        udp.Client.Bind(new IPEndPoint(IPAddress.Any, 0));
        var question = Encoding.ASCII.GetBytes(Question);
        var targets = LanAccess.BroadcastAddresses(LanAccess.LocalNetworks())
            .Append(IPAddress.Broadcast)
            .Append(IPAddress.Loopback)
            .Distinct()
            .ToList();
        foreach (var target in targets)
        {
            try
            {
                await udp.SendAsync(question, question.Length, new IPEndPoint(target, port)).ConfigureAwait(false);
            }
            catch (SocketException)
            {
                // Ta sieć nie przyjmuje rozgłoszeń — próbujemy pozostałych.
            }
        }

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(wait);
        try
        {
            while (true)
            {
                var received = await udp.ReceiveAsync(limit.Token).ConfigureAwait(false);
                var info = PeerInfo.Parse(Encoding.UTF8.GetString(received.Buffer));
                if (info is null)
                {
                    continue;
                }

                var from = LanAccess.Normalize(received.RemoteEndPoint.Address);
                info.Address = IPAddress.IsLoopback(from) ? "127.0.0.1" : from.ToString();
                found[info.Computer + "|" + info.Port] = PreferLan(found.GetValueOrDefault(info.Computer + "|" + info.Port), info);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Koniec czasu na odpowiedzi.
        }
        catch (SocketException)
        {
            // Sieć zniknęła w trakcie — zostaje to, co przyszło.
        }

        return found.Values.OrderBy(p => p.Computer, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Zamyka port UDP.</summary>
    public void Dispose()
    {
        _stop.Cancel();
        _udp.Dispose();
    }

    /// <summary>
    /// Windows po pytaniu wysłanym tam, gdzie nikt nie słucha (np. 127.0.0.1), zgłasza przy następnym odbiorze
    /// „połączenie zresetowane” i przerywa czekanie na pozostałe odpowiedzi. Wyłączamy to zachowanie (SIO_UDP_CONNRESET).
    /// </summary>
    private static void IgnoreConnectionReset(UdpClient udp)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        const int SioUdpConnReset = -1744830452;
        try
        {
            udp.Client.IOControl(SioUdpConnReset, new byte[] { 0, 0, 0, 0 }, null);
        }
        catch (SocketException)
        {
            // Starszy stos sieciowy — zostaje zachowanie domyślne.
        }
    }

    /// <summary>Ten sam komputer odpowiada z 127.0.0.1 i z adresu w sieci — do ustawień lepszy adres w sieci.</summary>
    private static PeerInfo PreferLan(PeerInfo? existing, PeerInfo candidate)
        => existing is null || existing.Address == "127.0.0.1" ? candidate : existing;

    private async Task ListenAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            UdpReceiveResult received;
            try
            {
                received = await _udp.ReceiveAsync(_stop.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (SocketException)
            {
                // Windows zgłasza tu „połączenie zresetowane” po nieudanej odpowiedzi — słuchamy dalej.
                continue;
            }

            if (Encoding.ASCII.GetString(received.Buffer) != Question || !_allowed(received.RemoteEndPoint.Address))
            {
                continue;
            }

            try
            {
                var answer = Encoding.UTF8.GetBytes(_describe().ToJson());
                await _udp.SendAsync(answer, answer.Length, received.RemoteEndPoint).ConfigureAwait(false);
            }
            catch (Exception e) when (e is SocketException or ObjectDisposedException)
            {
                // Pytający już nie słucha.
            }
        }
    }
}
