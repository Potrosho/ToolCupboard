using System;
using System.Security.Cryptography;
using System.Text;

namespace ToolCupboard.Web
{
    /// <summary>
    /// Decides which requests may reach the local web API.
    /// </summary>
    public static class WebAuth
    {
        public static string CreateToken()
        {
            return Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        }

        // A public site can point its own domain at 127.0.0.1, so only local host names are accepted
        public static bool IsLocalHost(string host, int port)
        {
            return string.Equals(host, $"127.0.0.1:{port}", StringComparison.OrdinalIgnoreCase)
                || string.Equals(host, $"localhost:{port}", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsValidToken(string token, string expected)
        {
            if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(expected)) { return false; }

            return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(token), Encoding.UTF8.GetBytes(expected));
        }
    }
}
