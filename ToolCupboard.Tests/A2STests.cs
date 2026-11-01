using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using ToolCupboard.GameServer.Query;
using Xunit;

namespace ToolCupboard.Tests
{
    public class A2STests
    {
        private static readonly byte[] Prefix = { 0xFF, 0xFF, 0xFF, 0xFF };

        private static byte[] InfoReply(byte players, byte maxPlayers)
        {
            byte[] text = Encoding.ASCII.GetBytes("My Server\0Procedural Map\0rust\0Rust\0");
            return Prefix.Concat(new byte[] { 0x49, 0x11 }).Concat(text).Concat(new byte[] { 0x00, 0x00, players, maxPlayers, 0x00 }).ToArray();
        }

        private static async Task<string> Query(UdpClient fakeServer)
        {
            var query = new A2S();
            query.SetAddressPort("127.0.0.1", ((IPEndPoint)fakeServer.Client.LocalEndPoint).Port, 2);
            return await query.GetPlayersAndMaxPlayers();
        }

        [Fact]
        public async Task ReadsPlayersFromADirectReply()
        {
            using var fakeServer = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            Task<string> result = Query(fakeServer);

            UdpReceiveResult request = await fakeServer.ReceiveAsync();
            await fakeServer.SendAsync(InfoReply(3, 10), request.RemoteEndPoint);

            Assert.Equal("3/10", await result);
        }

        [Fact]
        public async Task AnswersAChallengeBeforeReadingPlayers()
        {
            using var fakeServer = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            byte[] challenge = { 0x01, 0x02, 0x03, 0x04 };
            Task<string> result = Query(fakeServer);

            UdpReceiveResult first = await fakeServer.ReceiveAsync();
            await fakeServer.SendAsync(Prefix.Concat(new byte[] { 0x41 }).Concat(challenge).ToArray(), first.RemoteEndPoint);

            UdpReceiveResult second = await fakeServer.ReceiveAsync();
            Assert.Equal(challenge, second.Buffer.Skip(second.Buffer.Length - 4));
            await fakeServer.SendAsync(InfoReply(7, 50), second.RemoteEndPoint);

            Assert.Equal("7/50", await result);
        }

        [Fact]
        public async Task ReturnsNullWhenTheReplyIsNotServerInfo()
        {
            using var fakeServer = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            Task<string> result = Query(fakeServer);

            UdpReceiveResult request = await fakeServer.ReceiveAsync();
            await fakeServer.SendAsync(Prefix.Concat(new byte[] { 0x6D }).ToArray(), request.RemoteEndPoint);

            Assert.Null(await result);
        }
    }
}
