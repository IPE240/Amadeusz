using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace LokalneAI.Gateway;

/// <summary>Sieć lokalna tego komputera: adres i długość prefiksu, np. 10.0.0.20/24.</summary>
public readonly record struct LocalNetwork(IPAddress Address, int PrefixLength)
{
    /// <summary>Adres sieci z prefiksem, np. „10.0.0.0/24”.</summary>
    public override string ToString() => LanAccess.NetworkAddress(Address, PrefixLength) + "/" + PrefixLength;
}

/// <summary>Kto jest „w sieci biura”: ten sam komputer albo adres z podsieci jednej z kart sieciowych tego PC.</summary>
public static class LanAccess
{
    /// <summary>Adres IPv4 zapisany jako IPv6 (<c>::ffff:10.0.0.5</c>) zamienia na zwykły IPv4.</summary>
    public static IPAddress Normalize(IPAddress address)
        => address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

    /// <summary>Połączenie z tego samego komputera (127.0.0.1, ::1).</summary>
    public static bool IsLoopback(IPAddress address) => IPAddress.IsLoopback(Normalize(address));

    /// <summary>Sieci włączonych kart sieciowych tego PC (bez pętli zwrotnej).</summary>
    public static IReadOnlyList<LocalNetwork> LocalNetworks()
    {
        var result = new List<LocalNetwork>();
        try
        {
            foreach (var card in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (card.OperationalStatus != OperationalStatus.Up || card.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                {
                    continue;
                }

                foreach (var unicast in card.GetIPProperties().UnicastAddresses)
                {
                    var address = unicast.Address;
                    if (IPAddress.IsLoopback(address) || unicast.PrefixLength <= 0)
                    {
                        continue;
                    }

                    if (address.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6)
                    {
                        result.Add(new LocalNetwork(address, unicast.PrefixLength));
                    }
                }
            }
        }
        catch (NetworkInformationException)
        {
            // Bez listy kart: żaden adres nie jest „w podsieci”, przechodzą tylko połączenia z tego PC.
        }

        return result;
    }

    /// <summary>
    /// Czy adres jest w podsieci jednej z <paramref name="networks"/>. Adresy IPv6 „link-local” (fe80::/10)
    /// z definicji są w tym samym segmencie sieci, więc też przechodzą.
    /// </summary>
    public static bool IsInLocalSubnet(IPAddress remote, IEnumerable<LocalNetwork> networks)
    {
        var address = Normalize(remote);
        if (address.AddressFamily == AddressFamily.InterNetworkV6 && address.IsIPv6LinkLocal)
        {
            return true;
        }

        foreach (var network in networks)
        {
            var local = Normalize(network.Address);
            if (local.AddressFamily == address.AddressFamily && SamePrefix(local, address, network.PrefixLength))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Krótki opis sieci IPv4 do okna, np. „10.0.0.0/24”.</summary>
    public static string Describe(IEnumerable<LocalNetwork> networks)
    {
        var ipv4 = networks.Where(n => n.Address.AddressFamily == AddressFamily.InterNetwork).Select(n => n.ToString()).Distinct().ToList();
        return ipv4.Count == 0 ? "brak sieci IPv4" : string.Join(", ", ipv4);
    }

    /// <summary>Adresy rozgłoszeniowe sieci IPv4 (np. 10.0.0.255) do wyszukiwania komputerów.</summary>
    public static IEnumerable<IPAddress> BroadcastAddresses(IEnumerable<LocalNetwork> networks)
    {
        foreach (var network in networks)
        {
            if (network.Address.AddressFamily != AddressFamily.InterNetwork || network.PrefixLength is < 8 or > 30)
            {
                continue;
            }

            var bytes = network.Address.GetAddressBytes();
            var value = (uint)(bytes[0] << 24 | bytes[1] << 16 | bytes[2] << 8 | bytes[3]);
            var mask = network.PrefixLength == 0 ? 0u : uint.MaxValue << (32 - network.PrefixLength);
            var broadcast = value | ~mask;
            yield return new IPAddress(new[] { (byte)(broadcast >> 24), (byte)(broadcast >> 16), (byte)(broadcast >> 8), (byte)broadcast });
        }
    }

    internal static string NetworkAddress(IPAddress address, int prefixLength)
    {
        var bytes = Normalize(address).GetAddressBytes();
        for (var bit = prefixLength; bit < bytes.Length * 8; bit++)
        {
            bytes[bit / 8] &= (byte)~(0x80 >> (bit % 8));
        }

        return new IPAddress(bytes).ToString();
    }

    private static bool SamePrefix(IPAddress a, IPAddress b, int prefixLength)
    {
        var x = a.GetAddressBytes();
        var y = b.GetAddressBytes();
        if (x.Length != y.Length || prefixLength > x.Length * 8)
        {
            return false;
        }

        var fullBytes = prefixLength / 8;
        for (var i = 0; i < fullBytes; i++)
        {
            if (x[i] != y[i])
            {
                return false;
            }
        }

        var rest = prefixLength % 8;
        if (rest == 0)
        {
            return true;
        }

        var mask = (byte)(0xFF << (8 - rest));
        return (x[fullBytes] & mask) == (y[fullBytes] & mask);
    }
}
