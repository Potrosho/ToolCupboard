using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Mono.Nat;

namespace ToolCupboard.Functions
{
    /// <summary>
    /// Opens and closes UDP ports on the home router through UPnP, so players outside the network can join.
    /// </summary>
    public static class RouterPorts
    {
        const int SearchSeconds = 8;

        // Returns the router's public IP. Throws with a message for the user when the ports cannot be opened.
        public static async Task<string> Open(int[] ports, string description)
        {
            INatDevice router = await FindRouter();
            string publicIp = (await router.GetExternalIPAsync()).ToString();
            string reason = WhyUnreachable(publicIp);
            if (reason != null) { throw new InvalidOperationException(reason); }

            foreach (int port in ports)
            {
                // Lifetime 0 is "until removed" on UPnP routers
                await router.CreatePortMapAsync(new Mapping(Protocol.Udp, port, port, 0, description));
            }

            return publicIp;
        }

        public static async Task Close(int[] ports)
        {
            INatDevice router = await FindRouter();
            foreach (int port in ports)
            {
                try { await router.DeletePortMapAsync(new Mapping(Protocol.Udp, port, port)); }
                catch (MappingException) { } // already gone
            }
        }

        // Null when friends can reach this public IP, otherwise the reason they cannot
        public static string WhyUnreachable(string publicIp)
        {
            if (!IPAddress.TryParse(publicIp, out IPAddress ip) || ip.Equals(IPAddress.Any)) { return "The router does not have an internet address yet."; }

            byte[] b = ip.GetAddressBytes();
            if (b[0] == 100 && b[1] >= 64 && b[1] <= 127)
            {
                return $"Your router's internet address ({publicIp}) is shared by your internet provider (CGNAT), so players outside your network cannot reach it. Ask your provider for a public IP address.";
            }

            if (b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168))
            {
                return $"Your router's internet address ({publicIp}) is a private one, so there is another router in front of it. Forward the ports on that one too, or put your router in bridge mode.";
            }

            return null;
        }

        // NatUtility is global, so servers starting together must search one at a time
        static readonly System.Threading.SemaphoreSlim _searching = new System.Threading.SemaphoreSlim(1, 1);

        static async Task<INatDevice> FindRouter()
        {
            var found = new TaskCompletionSource<INatDevice>(TaskCreationOptions.RunContinuationsAsynchronously);
            EventHandler<DeviceEventArgs> onFound = (_, e) => found.TrySetResult(e.Device);

            await _searching.WaitAsync();
            NatUtility.DeviceFound += onFound;
            try
            {
                NatUtility.StartDiscovery(NatProtocol.Upnp);
                if (await Task.WhenAny(found.Task, Task.Delay(TimeSpan.FromSeconds(SearchSeconds))) != found.Task)
                {
                    throw new InvalidOperationException("No router answered. Turn on UPnP in your router's settings, or forward the ports by hand.");
                }

                return found.Task.Result;
            }
            finally
            {
                NatUtility.DeviceFound -= onFound;
                NatUtility.StopDiscovery();
                _searching.Release();
            }
        }
    }
}
