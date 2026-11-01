using ToolCupboard.Functions;

namespace ToolCupboard.GameServer.Data
{
    static class Class
    {
        public static dynamic Get(string serverGame, ServerConfig serverData = null)
        {
            return serverGame == RUST.FullName ? new RUST(serverData) : null;
        }
    }
}
