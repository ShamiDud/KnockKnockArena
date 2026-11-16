using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using KnockKnockArena.Server.Auth;
using KnockKnockArena.Shared.Simulation;

namespace KnockKnockArena.Server.Game;

/// <summary>
/// The authoritative game state: every player in the world, advanced by the tick
/// loop. All mutation happens under one lock; the UDP receive thread only does
/// lock-free lookups (concurrent dictionary).
///
/// Module 5: joining, the endpoint lookup, and the timeout. Movement and snapshots
/// come in M06.
/// </summary>
public sealed class GameWorld
{
    private readonly object _gate = new();
    private readonly Dictionary<byte, PlayerState> _playersById = new();
    private readonly ConcurrentDictionary<IPEndPoint, PlayerState> _playersByEndPoint = new();

    /// <summary>Scratch list for timed-out players whose kick never despawned them.</summary>
    private readonly List<PlayerState> _orphaned = new();

    public uint ServerTick { get; private set; }

    public int PlayerCount
    {
        get { lock (_gate) return _playersById.Count; }
    }

    /// <summary>Lock-free lookup used by the UDP receive thread.</summary>
    public bool TryGetByEndPoint(IPEndPoint endPoint, out PlayerState player) =>
        _playersByEndPoint.TryGetValue(endPoint, out player!);

    /// <summary>
    /// Handles a UDP join: adds the player, or rebinds the endpoint if the player is
    /// already in the world (client restart, NAT rebind — the token proves identity).
    /// Returns true if this was a new join.
    /// </summary>
    public bool SpawnOrRebind(PlayerSession session, IPEndPoint endPoint, out PlayerState player)
    {
        lock (_gate)
        {
            if (_playersById.TryGetValue(session.PlayerId, out PlayerState? existing))
            {
                _playersByEndPoint.TryRemove(existing.EndPoint, out _);
                existing.EndPoint = endPoint;
                existing.LastUdpPacketUtc = DateTime.UtcNow;
                existing.TimedOut = false; // back on UDP: the timeout no longer applies
                _playersByEndPoint[endPoint] = existing;
                player = existing;
                return false;
            }

            player = new PlayerState(session, endPoint);
            _playersById[session.PlayerId] = player;
            _playersByEndPoint[endPoint] = player;
            return true;
        }
    }

    /// <summary>Removes a player from the world.</summary>
    public bool TryDespawn(byte playerId)
    {
        lock (_gate)
        {
            if (!_playersById.Remove(playerId, out PlayerState? player))
                return false;
            _playersByEndPoint.TryRemove(player.EndPoint, out _);
            return true;
        }
    }

    /// <summary>
    /// Advances the world one tick. Module 5: only the timeout — players whose UDP has
    /// been silent too long are returned in timedOut.
    /// </summary>
    public void Tick(List<PlayerState> timedOut)
    {
        lock (_gate)
        {
            ServerTick++;
            DateTime now = DateTime.UtcNow;

            foreach (PlayerState player in _playersById.Values)
            {
                // A timed-out player is reported ONCE and left in the world: the caller
                // kicks the TCP connection, and the ordinary disconnect path despawns
                // the player.
                double silentSeconds = (now - player.LastUdpPacketUtc).TotalSeconds;
                if (!player.TimedOut && silentSeconds > GameConstants.UdpTimeoutSeconds)
                {
                    player.TimedOut = true;
                    timedOut.Add(player);
                }
                else if (player.TimedOut && silentSeconds > GameConstants.UdpTimeoutSeconds * 3)
                {
                    _orphaned.Add(player);
                }
            }

            // Safety net: the kick should have despawned them within milliseconds. If
            // the TCP side never came back (no connection left to close), remove them
            // so no ghost stays in the world.
            foreach (PlayerState player in _orphaned)
            {
                Log.Warn($"Removing {player.Session.Username}#{player.Session.PlayerId}: timed out and no TCP disconnect followed");
                _playersById.Remove(player.Session.PlayerId);
                _playersByEndPoint.TryRemove(player.EndPoint, out _);
            }
            _orphaned.Clear();
        }
    }
}
