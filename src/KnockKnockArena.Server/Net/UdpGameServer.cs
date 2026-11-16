using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using KnockKnockArena.Server.Auth;
using KnockKnockArena.Server.Game;
using KnockKnockArena.Shared.Protocol;
using KnockKnockArena.Shared.Protocol.Udp;
using KnockKnockArena.Shared.Simulation;

namespace KnockKnockArena.Server.Net;

/// <summary>
/// The UDP side of the game server: one socket for all clients (no thread per
/// client — the pattern taught in T05), a receive thread that validates packets, and
/// a tick thread that advances the world at a fixed 30 Hz.
///
/// Module 5: join, heartbeat, timeout and LAN discovery. Inputs and snapshots come in M06.
/// </summary>
public sealed class UdpGameServer
{
    private readonly ServerConfig _config;
    private readonly SessionManager _sessions;
    private readonly GameWorld _world;
    private UdpClient? _udp;

    /// <summary>Wired to TcpGameServer.KickByPlayerId: a UDP timeout disconnects the
    /// player entirely, which also produces the PlayerLeft event on TCP.</summary>
    public Action<byte, string>? KickPlayer;

    public UdpGameServer(ServerConfig config, SessionManager sessions, GameWorld world)
    {
        _config = config;
        _sessions = sessions;
        _world = world;
    }

    public void Start()
    {
        _udp = new UdpClient(_config.Port);
        DisableIcmpConnectionReset(_udp);
        Log.Info($"UDP listening on port {_config.Port}, tick rate {GameConstants.TickRate} Hz");

        new Thread(ReceiveLoop) { IsBackground = true, Name = "udp-receive" }.Start();
        new Thread(TickLoop) { IsBackground = true, Name = "udp-tick" }.Start();
    }

    /// <summary>
    /// On Windows, a UDP send to a port nobody listens on can come back as an ICMP
    /// "port unreachable", which surfaces as a SocketException (ConnectionReset) on
    /// the NEXT Receive — and would kill the receive loop. This ioctl turns that off.
    /// </summary>
    private static void DisableIcmpConnectionReset(UdpClient udp)
    {
        const int SIO_UDP_CONNRESET = unchecked((int)0x9800000C);
        try
        {
            udp.Client.IOControl(SIO_UDP_CONNRESET, new byte[] { 0 }, null);
        }
        catch (Exception)
        {
            // Not supported outside Windows; the receive loop also catches the
            // exception as a fallback.
        }
    }

    private void ReceiveLoop()
    {
        while (true)
        {
            IPEndPoint remote = new(IPAddress.Any, 0);
            byte[] datagram;
            try
            {
                datagram = _udp!.Receive(ref remote);
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionReset)
            {
                continue; // see DisableIcmpConnectionReset
            }

            try
            {
                Dispatch(datagram, remote);
            }
            catch (ProtocolException ex)
            {
                Log.Warn($"REJECT UDP from {remote}: {ex.Reason}");
            }
        }
    }

    private void Dispatch(byte[] datagram, IPEndPoint remote)
    {
        UdpPacketType type = UdpPacketIO.PeekType(datagram);
        Log.Packet($"RECV UDP {type} ({datagram.Length} B) from {remote}");

        switch (type)
        {
            case UdpPacketType.Join:
                HandleJoin(JoinPacket.FromDatagram(datagram), remote);
                break;

            case UdpPacketType.Heartbeat:
                HandleHeartbeat(HeartbeatPacket.FromDatagram(datagram), remote);
                break;

            case UdpPacketType.DiscoveryRequest:
                HandleDiscovery(DiscoveryRequestPacket.FromDatagramAnyVersion(datagram), remote);
                break;

            default:
                throw new ProtocolException($"unknown type {(byte)type}");
        }
    }

    private void HandleJoin(JoinPacket join, IPEndPoint remote)
    {
        if (!_sessions.TryGetByToken(join.SessionToken, out PlayerSession? session))
            throw new ProtocolException("bad token");

        bool joined = _world.SpawnOrRebind(session!, remote, out PlayerState player);
        if (joined)
            Log.Info($"UDP join: {session!.Username}#{session.PlayerId} from {remote} " +
                     $"({_world.PlayerCount} in game)");
        else if (!player.EndPoint.Equals(remote))
            Log.Info($"UDP rebind: {session!.Username}#{session.PlayerId} now at {remote}");
        // A repeated Join from the same endpoint is normal (the client resends it,
        // because UDP may lose it) and needs no log beyond the packet line.
    }

    private void HandleHeartbeat(HeartbeatPacket heartbeat, IPEndPoint remote)
    {
        if (!_world.TryGetByEndPoint(remote, out PlayerState player))
            throw new ProtocolException("unknown endpoint");
        if (heartbeat.PlayerId != player.Session.PlayerId)
            throw new ProtocolException($"bad player id {heartbeat.PlayerId}");

        player.LastUdpPacketUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// LAN discovery (M05: broadcast). The request arrived as a broadcast to the game
    /// port; the answer goes straight back to the asker (unicast). No session needed —
    /// that is the point of discovery — and requests from other protocol versions are
    /// answered too, so an old client sees this server as incompatible instead of not
    /// at all.
    /// </summary>
    private void HandleDiscovery(DiscoveryRequestPacket request, IPEndPoint remote)
    {
        byte[] answer = new DiscoveryResponsePacket
        {
            Nonce = request.Nonce,
            GamePort = (ushort)_config.Port,
            HttpPort = 0,        // no REST API before M10
            PlayersOnline = (byte)_sessions.Count,
            MaxPlayers = (byte)_config.MaxPlayers,
            ServerName = _config.ServerName,
            MapName = "",        // no map before M06
        }.ToDatagram();
        _udp!.Send(answer, answer.Length, remote);
        Log.Packet($"Discovery answer to {remote} (client speaks v{request.ClientVersion}, {answer.Length} B)");
    }

    private void TickLoop()
    {
        // Fixed-rate loop without drift: target times accumulate from one stopwatch
        // instead of sleeping a fixed amount per iteration (Thread.Sleep overshoots).
        Stopwatch clock = Stopwatch.StartNew();
        double tickIntervalMs = 1000.0 / GameConstants.TickRate;
        double nextTickAtMs = tickIntervalMs;

        List<PlayerState> timedOut = new();

        while (true)
        {
            double waitMs = nextTickAtMs - clock.Elapsed.TotalMilliseconds;
            if (waitMs > 1)
            {
                Thread.Sleep((int)waitMs);
                continue;
            }
            if (waitMs < -1000)
            {
                // Massively behind (debugger pause, machine sleep): resync instead of
                // burst-running hundreds of catch-up ticks.
                Log.Warn("Tick loop fell behind; resyncing clock");
                nextTickAtMs = clock.Elapsed.TotalMilliseconds;
            }
            nextTickAtMs += tickIntervalMs;

            timedOut.Clear();
            _world.Tick(timedOut);

            foreach (PlayerState player in timedOut)
            {
                Log.Info($"UDP timeout: {player.Session.Username}#{player.Session.PlayerId} " +
                         $"(no packets for {GameConstants.UdpTimeoutSeconds:0} s)");
                KickPlayer?.Invoke(player.Session.PlayerId, "udp timeout");
            }
        }
    }
}
