using System;
using System.Net;
using KnockKnockArena.Server.Auth;

namespace KnockKnockArena.Server.Game;

/// <summary>
/// Server-side state for one player that has joined the game world via UDP.
/// Module 5: who the player is, where their datagrams come from, and when the last
/// one arrived. Position comes in M06, weapons in M08, health in M09.
/// </summary>
public sealed class PlayerState
{
    public PlayerSession Session { get; }

    /// <summary>The UDP endpoint the player's datagrams come from. Updated if the client rejoins.</summary>
    public IPEndPoint EndPoint;

    /// <summary>Time of the last UDP packet from this player (join or heartbeat); drives the timeout.</summary>
    public DateTime LastUdpPacketUtc = DateTime.UtcNow;

    /// <summary>Reported as timed out; the kick is under way. Set once, so a silent
    /// player is not kicked again every tick until the disconnect lands.</summary>
    public bool TimedOut;

    public PlayerState(PlayerSession session, IPEndPoint endPoint)
    {
        Session = session;
        EndPoint = endPoint;
    }
}
