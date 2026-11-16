using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace KnockKnockArena.Shared.Protocol.Udp
{
    /// <summary>One server that answered a LAN search.</summary>
    public sealed class DiscoveredServer
    {
        /// <summary>Where the answer came from: the address to join.</summary>
        public IPAddress Address = IPAddress.None;

        /// <summary>The UDP port the answer came from (the server's game port).</summary>
        public int SourcePort;

        /// <summary>The server's protocol version (header byte of its answer).</summary>
        public byte ProtocolVersion;

        /// <summary>Parsed answer; null when the server speaks another protocol version.</summary>
        public DiscoveryResponsePacket? Info;

        public bool Compatible => Info != null;

        public override string ToString() => Info == null
            ? $"{Address}:{SourcePort}  (protocol v{ProtocolVersion} — incompatible, this client speaks v{ProtocolConstants.Version})"
            : $"{Info.ServerName}  {Address}:{Info.GamePort}  {Info.PlayersOnline}/{Info.MaxPlayers} players  " +
              $"map \"{Info.MapName}\"  REST :{Info.HttpPort}  v{ProtocolVersion}";
    }

    /// <summary>
    /// LAN server discovery (M05: broadcast). One datagram to 255.255.255.255 reaches
    /// every machine on the local network segment; each server answers directly.
    /// Routers do not forward broadcasts, which is exactly why this finds LAN servers
    /// only — the server browser (REST, M10) is how you find servers beyond the LAN.
    ///
    /// Blocking: call it from a background thread (the Unity client does).
    /// </summary>
    public static class LanDiscovery
    {
        public static List<DiscoveredServer> Search(int gamePort, TimeSpan listenFor)
        {
            List<DiscoveredServer> found = new List<DiscoveredServer>();
            uint nonce = (uint)new Random().Next();

            using (UdpClient socket = new UdpClient(new IPEndPoint(IPAddress.Any, 0)))
            {
                // Sending to a broadcast address needs explicit permission (SO_BROADCAST);
                // without it the OS refuses the send.
                socket.EnableBroadcast = true;
                byte[] request = new DiscoveryRequestPacket { Nonce = nonce }.ToDatagram();
                foreach (IPAddress target in BroadcastTargets())
                {
                    try
                    {
                        socket.Send(request, request.Length, new IPEndPoint(target, gamePort));
                    }
                    catch (SocketException)
                    {
                        // An interface that cannot send (down, no route): try the rest.
                    }
                }

                Stopwatch clock = Stopwatch.StartNew();
                while (true)
                {
                    int remainingMs = (int)(listenFor.TotalMilliseconds - clock.ElapsedMilliseconds);
                    if (remainingMs <= 0)
                        break;
                    socket.Client.ReceiveTimeout = remainingMs;

                    IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0);
                    byte[] datagram;
                    try
                    {
                        datagram = socket.Receive(ref remote);
                    }
                    catch (SocketException ex) when (ex.SocketErrorCode == SocketError.TimedOut)
                    {
                        break;
                    }
                    catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionReset)
                    {
                        continue; // Windows ICMP echo of an earlier send
                    }

                    try
                    {
                        if (datagram.Length < 2 || (UdpPacketType)datagram[0] != UdpPacketType.DiscoveryResponse)
                            continue;
                        DiscoveredServer server = new DiscoveredServer
                        {
                            Address = remote.Address,
                            SourcePort = remote.Port,
                            ProtocolVersion = DiscoveryResponsePacket.PeekVersion(datagram),
                        };
                        if (server.ProtocolVersion == ProtocolConstants.Version)
                        {
                            DiscoveryResponsePacket info = DiscoveryResponsePacket.FromDatagram(datagram);
                            if (info.Nonce != nonce)
                                continue; // an answer to somebody else's search
                            server.Info = info;
                        }
                        // One server answers once per broadcast that reached it, possibly
                        // from several of its own addresses: list it once.
                        if (!found.Exists(s => SameServer(s, server)))
                            found.Add(server);
                    }
                    catch (ProtocolException)
                    {
                        // Garbage on the discovery socket: ignore it, keep listening.
                    }
                }
            }
            return found;
        }

        /// <summary>
        /// Where the request goes: the limited broadcast 255.255.255.255, plus the
        /// directed broadcast of every IPv4 interface (e.g. 192.168.50.255 for
        /// 192.168.50.141/24 — the host bits all set to 1). On a machine with several
        /// adapters (VPN, Hyper-V, WSL) Windows sends 255.255.255.255 out on ONE
        /// interface of its choosing, which need not be the LAN the servers are on;
        /// the directed broadcasts reach every network the machine sits on.
        /// </summary>
        public static List<IPAddress> BroadcastTargets()
        {
            List<IPAddress> targets = new List<IPAddress> { IPAddress.Broadcast };
            try
            {
                foreach (System.Net.NetworkInformation.NetworkInterface nic in
                         System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up)
                        continue;
                    foreach (System.Net.NetworkInformation.UnicastIPAddressInformation unicast in
                             nic.GetIPProperties().UnicastAddresses)
                    {
                        if (unicast.Address.AddressFamily != AddressFamily.InterNetwork ||
                            unicast.IPv4Mask == null || IPAddress.IsLoopback(unicast.Address))
                            continue;
                        byte[] ip = unicast.Address.GetAddressBytes();
                        byte[] mask = unicast.IPv4Mask.GetAddressBytes();
                        byte[] broadcast = new byte[4];
                        for (int i = 0; i < 4; i++)
                            broadcast[i] = (byte)(ip[i] | ~mask[i]);
                        IPAddress directed = new IPAddress(broadcast);
                        if (!targets.Contains(directed))
                            targets.Add(directed);
                    }
                }
            }
            catch (Exception)
            {
                // Interface enumeration is not available everywhere; the limited
                // broadcast alone still finds servers on the default interface.
            }
            return targets;
        }

        private static bool SameServer(DiscoveredServer a, DiscoveredServer b)
        {
            // An incompatible answer cannot be read past the version byte, so there is
            // no name to compare: one entry per answering port is the best we can do.
            if (a.Info == null || b.Info == null)
                return a.Info == null && b.Info == null && a.SourcePort == b.SourcePort;
            return a.Info.ServerName == b.Info.ServerName && a.Info.GamePort == b.Info.GamePort &&
                   a.Info.HttpPort == b.Info.HttpPort;
        }
    }
}
