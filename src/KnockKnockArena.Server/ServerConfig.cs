using System;

namespace KnockKnockArena.Server;

public sealed class ServerConfig
{
    /// <summary>The TCP listener's port (M05: the UDP socket uses the same number).</summary>
    public int Port { get; private set; } = 36363;

    public int MaxPlayers { get; private set; } = 12;

    public bool Verbose { get; private set; }

    public static ServerConfig Load(string[] args)
    {
        ServerConfig config = new();

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--port" when i + 1 < args.Length && int.TryParse(args[i + 1], out int port):
                    config.Port = port;
                    i++;
                    break;
                case "--max-players" when i + 1 < args.Length && int.TryParse(args[i + 1], out int max):
                    config.MaxPlayers = max;
                    i++;
                    break;
                case "--verbose":
                    config.Verbose = true;
                    break;
                default:
                    throw new ArgumentException($"Unknown argument '{args[i]}'. " +
                        "Usage: --port N --max-players N --verbose");
            }
        }

        if (config.Port is < 1024 or > 65535)
            throw new ArgumentException($"Port {config.Port} is out of range (1024-65535).");
        if (config.MaxPlayers < 1)
            throw new ArgumentException($"Max players {config.MaxPlayers} is out of range (at least 1).");

        int maxPlayerId = KnockKnockArena.Shared.Simulation.GameConstants.PickupEntityIdBase - 1;
        if (config.MaxPlayers > maxPlayerId)
        {
            Log.Warn($"Max players {config.MaxPlayers} clamped to {maxPlayerId}: player ids must stay " +
                     $"below the pickup entity id range ({maxPlayerId + 1}+).");
            config.MaxPlayers = maxPlayerId;
        }

        return config;
    }
}
