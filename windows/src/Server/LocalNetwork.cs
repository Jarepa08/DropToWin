using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace DropToWin.Server;

sealed record LocalAddress(IPAddress Address, int PrefixLength, string InterfaceName, bool HasGateway);

sealed record NetworkProfile(string Name, string Category);

static class LocalNetwork
{
    // Adaptadores virtuales cuyas subredes podrían coincidir por casualidad con la del teléfono.
    static readonly string[] VirtualMarkers =
        ["Hyper-V", "vEthernet", "VirtualBox", "VMware", "WSL", "Docker", "Npcap", "Loopback"];

    /// <summary>IPv4 locales utilizables, primero las de adaptadores con puerta de enlace.</summary>
    public static List<LocalAddress> GetAddresses()
    {
        var result = new List<LocalAddress>();
        NetworkInterface[] interfaces;
        try { interfaces = NetworkInterface.GetAllNetworkInterfaces(); }
        catch (NetworkInformationException) { return result; }

        foreach (var ni in interfaces)
        {
            if (ni.OperationalStatus != OperationalStatus.Up) continue;
            if (ni.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
            if (VirtualMarkers.Any(m => ni.Name.Contains(m, StringComparison.OrdinalIgnoreCase) ||
                                        ni.Description.Contains(m, StringComparison.OrdinalIgnoreCase))) continue;

            var props = ni.GetIPProperties();
            bool hasGateway = props.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork &&
                                                              !g.Address.Equals(IPAddress.Any));
            foreach (var ua in props.UnicastAddresses)
            {
                if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                var bytes = ua.Address.GetAddressBytes();
                if (bytes[0] == 169 && bytes[1] == 254) continue;
                result.Add(new LocalAddress(ua.Address, ua.PrefixLength, ni.Name, hasGateway));
            }
        }
        return result.OrderByDescending(a => a.HasGateway).ToList();
    }

    /// <summary>Redes conectadas y su categoría (Pública/Privada/Dominio), vía Network List Manager.</summary>
    public static List<NetworkProfile> GetProfiles()
    {
        var list = new List<NetworkProfile>();
        try
        {
            var type = Type.GetTypeFromCLSID(new Guid("DCB00C01-570F-4A9B-8D69-199FDBA5723B"));
            if (type is null) return list;
            dynamic manager = Activator.CreateInstance(type)!;
            foreach (dynamic network in manager.GetNetworks(1 /* NLM_ENUM_NETWORK_CONNECTED */))
            {
                int category = network.GetCategory();
                string name = network.GetName();
                list.Add(new NetworkProfile(name, category switch
                {
                    0 => "Pública",
                    1 => "Privada",
                    2 => "Dominio",
                    _ => "Desconocida",
                }));
            }
        }
        catch (Exception ex)
        {
            Log.Error("Red", ex);
        }
        return list;
    }
}
