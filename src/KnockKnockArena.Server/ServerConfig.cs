using System;

namespace KnockKnockArena.Server;

/// <summary>
/// Server configuration. Read from environment variables first (the Docker way),
/// then overridden by command-line arguments:
///
///   env:  KKARENA_PORT, KKARENA_HTTP_PORT, KKARENA_MAX_PLAYERS, KKARENA_DATA_DIR,
///         KKARENA_MAP, KKARENA_WEAPONS, KKARENA_PICKUPS, KKARENA_NAME,
///         KKARENA_PUBLIC_PORT, KKARENA_PUBLIC_HTTP_PORT, KKARENA_VERBOSE
///   args: --port N, --http-port N, --max-players N, --data-dir PATH,
///         --map PATH, --weapons PATH, --pickups PATH, --name TEXT,
///         --public-port N, --public-http-port N, --verbose, --dev-accounts
///
///   --dev-accounts has no environment variable on purpose: it seeds test/test, and a
///   shared server (a container on the central machine) must never get it by accident.
/// </summary>
public sealed class ServerConfig
{
    /// <summary>Used for BOTH the TCP listener and the UDP socket (same number, two protocols).</summary>
    public int Port { get; private set; } = 36363;

    /// <summary>Port for the REST API (accounts, leaderboard, status).</summary>
    public int HttpPort { get; private set; } = 8080;

    public int MaxPlayers { get; private set; } = 12;

    /// <summary>Directory for persisted data (accounts.json). Mounted as a volume in Docker.</summary>
    public string DataDir { get; private set; } = "data";

    /// <summary>Arena to run, as a path to an arena.json. Relative paths resolve inside
    /// <see cref="DataDir"/>, so the common case is just "--map arena2.json".</summary>
    public string MapFile { get; private set; } = "arena.json";

    /// <summary>Weapon table, same path rules as <see cref="MapFile"/>.</summary>
    public string WeaponsFile { get; private set; } = "weapons.json";

    /// <summary>Pickup table (per-type pickup radius), same path rules. Optional:
    /// without it every pickup keeps the default radius.</summary>
    public string PickupsFile { get; private set; } = "pickups.json";

    /// <summary>What LAN discovery and the server browser show for this instance.
    /// Defaults to the machine name, so two servers on one LAN tell apart.</summary>
    public string ServerName { get; private set; } = Environment.MachineName;

    /// <summary>
    /// The ports this instance ADVERTISES (in /api/status and LAN discovery answers),
    /// when they differ from the ones it binds. Behind Docker port mapping
    /// (-p 36395:36363) the server listens on 36363 inside the container, but a client
    /// outside must connect to 36395 — port mapping is NAT, and the server cannot see
    /// the outside port for itself. 0 = advertise the bound port.
    /// </summary>
    public int PublicPort { get; private set; }
    public int PublicHttpPort { get; private set; }

    // #01 de annoncerede porte skrives her

    public bool Verbose { get; private set; }

    /// <summary>Seed a new accounts.json with alice, bob and test/test - a local
    /// development server only (see <see cref="Auth.AccountStore"/>).</summary>
    public bool DevAccounts { get; private set; }

    /// <summary>Full path to the arena file, after applying the DataDir rule.</summary>
    public string MapPath => ResolveDataPath(MapFile);

    /// <summary>Full path to the weapon table, after applying the DataDir rule.</summary>
    public string WeaponsPath => ResolveDataPath(WeaponsFile);

    /// <summary>Full path to the pickup table, after applying the DataDir rule.</summary>
    public string PickupsPath => ResolveDataPath(PickupsFile);

    private string ResolveDataPath(string file)
        => System.IO.Path.IsPathRooted(file) ? file : System.IO.Path.Combine(DataDir, file);

    public static ServerConfig Load(string[] args)
    {
        ServerConfig config = new();

        if (int.TryParse(Environment.GetEnvironmentVariable("KKARENA_PORT"), out int envPort))
            config.Port = envPort;
        if (int.TryParse(Environment.GetEnvironmentVariable("KKARENA_HTTP_PORT"), out int envHttpPort))
            config.HttpPort = envHttpPort;
        if (int.TryParse(Environment.GetEnvironmentVariable("KKARENA_MAX_PLAYERS"), out int envMax))
            config.MaxPlayers = envMax;
        string? envDataDir = Environment.GetEnvironmentVariable("KKARENA_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(envDataDir))
            config.DataDir = envDataDir;
        string? envMap = Environment.GetEnvironmentVariable("KKARENA_MAP");
        if (!string.IsNullOrWhiteSpace(envMap))
            config.MapFile = envMap;
        string? envWeapons = Environment.GetEnvironmentVariable("KKARENA_WEAPONS");
        if (!string.IsNullOrWhiteSpace(envWeapons))
            config.WeaponsFile = envWeapons;
        string? envPickups = Environment.GetEnvironmentVariable("KKARENA_PICKUPS");
        if (!string.IsNullOrWhiteSpace(envPickups))
            config.PickupsFile = envPickups;
        string? envName = Environment.GetEnvironmentVariable("KKARENA_NAME");
        if (!string.IsNullOrWhiteSpace(envName))
            config.ServerName = envName;
        if (int.TryParse(Environment.GetEnvironmentVariable("KKARENA_PUBLIC_PORT"), out int envPublicPort))
            config.PublicPort = envPublicPort;
        if (int.TryParse(Environment.GetEnvironmentVariable("KKARENA_PUBLIC_HTTP_PORT"), out int envPublicHttpPort))
            config.PublicHttpPort = envPublicHttpPort;
        string? envVerbose = Environment.GetEnvironmentVariable("KKARENA_VERBOSE");
        if (envVerbose is "1" or "true" or "TRUE" or "True")
            config.Verbose = true;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--port" when i + 1 < args.Length && int.TryParse(args[i + 1], out int port):
                    config.Port = port;
                    i++;
                    break;
                case "--http-port" when i + 1 < args.Length && int.TryParse(args[i + 1], out int httpPort):
                    config.HttpPort = httpPort;
                    i++;
                    break;
                case "--max-players" when i + 1 < args.Length && int.TryParse(args[i + 1], out int max):
                    config.MaxPlayers = max;
                    i++;
                    break;
                case "--data-dir" when i + 1 < args.Length:
                    config.DataDir = args[i + 1];
                    i++;
                    break;
                case "--map" when i + 1 < args.Length:
                    config.MapFile = args[i + 1];
                    i++;
                    break;
                case "--weapons" when i + 1 < args.Length:
                    config.WeaponsFile = args[i + 1];
                    i++;
                    break;
                case "--pickups" when i + 1 < args.Length:
                    config.PickupsFile = args[i + 1];
                    i++;
                    break;
                case "--name" when i + 1 < args.Length:
                    config.ServerName = args[i + 1];
                    i++;
                    break;
                case "--public-port" when i + 1 < args.Length && int.TryParse(args[i + 1], out int publicPort):
                    config.PublicPort = publicPort;
                    i++;
                    break;
                case "--public-http-port" when i + 1 < args.Length && int.TryParse(args[i + 1], out int publicHttpPort):
                    config.PublicHttpPort = publicHttpPort;
                    i++;
                    break;
                case "--verbose":
                    config.Verbose = true;
                    break;
                case "--dev-accounts":
                    config.DevAccounts = true;
                    break;
                default:
                    throw new ArgumentException($"Unknown argument '{args[i]}'. " +
                        "Usage: --port N --http-port N --max-players N --data-dir PATH " +
                        "--map PATH --weapons PATH --pickups PATH --name TEXT --public-port N --public-http-port N --verbose --dev-accounts");
            }
        }

        if (config.Port is < 1024 or > 65535)
            throw new ArgumentException($"Port {config.Port} is out of range (1024-65535).");
        if (config.HttpPort is < 1024 or > 65535 || config.HttpPort == config.Port)
            throw new ArgumentException($"HTTP port {config.HttpPort} is out of range or collides with the game port.");
        if (config.MaxPlayers < 1)
            throw new ArgumentException($"Max players {config.MaxPlayers} is out of range (at least 1).");

        // Player ids double as snapshot entity ids, and pickups start at entity id
        // 100. A player id of 100+ would collide with a pickup, so the id ranges are
        // absolute: players 1-99, pickups 100-999, rockets 1000+.
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
