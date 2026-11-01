using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using ToolCupboard.Functions;

namespace ToolCupboard.GameServer.Query
{
    public class A2S
    {
        private static readonly byte[] REQUEST_PREFIX = { 0xFF, 0xFF, 0xFF, 0xFF };
        private static readonly byte[] A2S_INFO = Encoding.ASCII.GetBytes("TSource Engine Query\0");

        private const byte CHALLENGE_RESPONSE = 0x41;
        private const byte INFO_RESPONSE = 0x49;

        private IPEndPoint _IPEndPoint;
        private int _timeout;

        public void SetAddressPort(string address, int port, int timeout = 5)
        {
            _IPEndPoint = new IPEndPoint(IPAddress.Parse(address), port);
            _timeout = timeout * 1000;
        }

        /// <summary>Asks the server how many players are online.</summary>
        /// <returns>Returns "players/maxplayers", or null if the server does not answer</returns>
        public async Task<string> GetPlayersAndMaxPlayers()
        {
            return await Task.Run(() =>
            {
                try
                {
                    byte[] requestData = REQUEST_PREFIX.Concat(A2S_INFO).ToArray();
                    byte[] responseData;

                    using (var udpHandler = new UdpClientHandler(_IPEndPoint))
                    {
                        responseData = udpHandler.GetResponse(requestData, requestData.Length, _timeout, _timeout).Skip(4).ToArray();

                        if (responseData[0] == CHALLENGE_RESPONSE)
                        {
                            requestData = requestData.Concat(responseData.Skip(1)).ToArray();
                            responseData = udpHandler.GetResponse(requestData, requestData.Length, _timeout, _timeout).Skip(4).ToArray();
                        }
                    }

                    if (responseData[0] != INFO_RESPONSE) { return null; }

                    using (var br = new BinaryReader(new MemoryStream(responseData)))
                    {
                        br.ReadBytes(2); // Header and protocol
                        for (int i = 0; i < 4; i++) { SkipString(br); } // Name, map, folder and game
                        br.ReadInt16(); // App id

                        return $"{br.ReadByte()}/{br.ReadByte()}";
                    }
                }
                catch (Exception e) when (e is IOException || e is IndexOutOfRangeException || e is System.Net.Sockets.SocketException)
                {
                    return null;
                }
            });
        }

        private static void SkipString(BinaryReader br)
        {
            while (br.ReadByte() != 0x00) { }
        }
    }
}
