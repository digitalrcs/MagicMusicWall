using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace MagicMusicWall.App;

internal sealed record NetworkChoice(string Address, string Label);
internal sealed record AudioChoice(string Id, string Name);

internal static class NetworkAddresses
{
    public static List<NetworkChoice> Get()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .SelectMany(n => n.GetIPProperties().UnicastAddresses
                    .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork && !System.Net.IPAddress.IsLoopback(a.Address))
                    .Select(a => new NetworkChoice(a.Address.ToString(), $"{n.Name} · {a.Address}")))
                .Where(a => !a.Address.StartsWith("169.254.", StringComparison.Ordinal))
                .DistinctBy(a => a.Address).ToList();
        }
        catch (NetworkInformationException) { return []; }
    }

    public static string Resolve(string preferred)
    {
        var choices = Get();
        if (preferred.Length > 0)
            return choices.Any(c => c.Address == preferred) ? preferred : throw new InvalidOperationException("The saved network address is unavailable. Choose a connected address in Setup.");
        return choices.FirstOrDefault()?.Address ?? throw new InvalidOperationException("No connected local IPv4 address was found. Connect this PC to your home network.");
    }

    public static List<NetworkChoice> Choices(string saved)
    {
        var result = Get();
        result.Insert(0, new("", "Automatic · first connected network"));
        if (!result.Any(c => c.Address == saved)) result.Add(new(saved, $"{saved} · currently unavailable"));
        return result;
    }
}
