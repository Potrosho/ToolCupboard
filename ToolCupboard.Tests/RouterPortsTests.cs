using ToolCupboard.Functions;
using Xunit;

namespace ToolCupboard.Tests
{
    public class RouterPortsTests
    {
        [Theory]
        [InlineData("81.23.45.67", true)]
        [InlineData("100.63.255.255", true)]
        [InlineData("100.64.0.1", false)] // CGNAT
        [InlineData("100.127.255.255", false)] // CGNAT
        [InlineData("100.128.0.1", true)]
        [InlineData("192.168.0.10", false)] // router behind another router
        [InlineData("10.1.2.3", false)]
        [InlineData("172.16.0.1", false)]
        [InlineData("172.32.0.1", true)]
        [InlineData("0.0.0.0", false)]
        [InlineData("", false)]
        public void OnlyPublicAddressesAreReachable(string publicIp, bool reachable)
        {
            Assert.Equal(reachable, RouterPorts.WhyUnreachable(publicIp) == null);
        }
    }
}
