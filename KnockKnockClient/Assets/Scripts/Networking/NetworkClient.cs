using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using KnockKnockArena.Shared.Protocol;
using KnockKnockArena.Shared.Protocol.Messages;
using KnockKnockArena.Shared.Protocol.Udp;

namespace KnockKnockArena.Networking
{
    /// <summary>Queued marker: the TCP connection is up and the login has been sent.</summary>
    public sealed class TcpConnectedEvent { }

    /// <summary>Queued marker: the TCP connection is gone (server closed it, or an error).</summary>
    public sealed class TcpDisconnectedEvent
    {
        public string Reason = "";
    }

    /// <summary>
    /// The connection to the game server: TCP (login, chat, events) and UDP (join,
    /// heartbeat). Pure C# — no Unity API anywhere in this class.
    ///
    /// Threading model (the pattern from T04/T05, exemplary on purpose):
    /// blocking receives run on background threads, which ONLY parse and enqueue.
    /// The Unity side drains <see cref="TcpMessages"/> from Update() on the main thread.
    /// </summary>
    public sealed class NetworkClient : IDisposable
    {
        /// <summary>Parsed TCP messages plus TcpConnectedEvent/TcpDisconnectedEvent markers.
        /// Drained on the Unity main thread.</summary>
        public readonly ConcurrentQueue<object> TcpMessages = new ConcurrentQueue<object>();

        public byte PlayerId { get; private set; }

        /// <summary>True once the join phase is over and the heartbeat has taken over.</summary>
        public bool InGame => _inGame;

        // Diagnostics (read from the main thread for the HUD).
        public long TcpMessagesSkipped;
        public long JoinsSent;

        private TcpClient? _tcp;
        private NetworkStream? _tcpStream;
        private UdpClient? _udp;
        private IPEndPoint? _serverEndPoint;
        private byte[] _sessionToken = Array.Empty<byte>();
        private IPAddress _serverAddress = IPAddress.None;
        private int _port;
        private volatile bool _running = true;
        private volatile bool _inGame;

        /// <summary>
        /// Connects and logs in on a background thread (TcpClient.Connect blocks, so
        /// it must not run on the Unity main thread). Progress arrives as queued
        /// events: TcpConnectedEvent, then LoginResponse/ServerInfo/etc., or
        /// TcpDisconnectedEvent on failure.
        /// </summary>
        public void ConnectAndLogin(string host, int port, string username, string password)
        {
            _port = port;

            Thread tcpThread = new Thread(() =>
            {
                try
                {
                    // Resolve once, here on the background thread (DNS can block): the
                    // Host field may hold "localhost", a hostname or an IP (M03). TCP and
                    // UDP then use the same IPv4 address.
                    _serverAddress = AddressResolver.Resolve(host);
                    _tcp = new TcpClient(AddressFamily.InterNetwork);
                    _tcp.Connect(_serverAddress, port);
                    _tcpStream = _tcp.GetStream();

                    TcpFraming.WriteMessage(_tcpStream, MessageType.LoginRequest, new LoginRequest
                    {
                        Username = username,
                        Password = password,
                    }.ToPayload());
                    TcpMessages.Enqueue(new TcpConnectedEvent());

                    ReceiveTcpLoop();
                }
                catch (Exception ex)
                {
                    TcpMessages.Enqueue(new TcpDisconnectedEvent { Reason = ex.Message });
                }
            })
            {
                IsBackground = true,
                Name = "kka-tcp",
            };
            tcpThread.Start();
        }

        private void ReceiveTcpLoop()
        {
            try
            {
                while (_running && TcpFraming.TryReadMessage(_tcpStream, out MessageType type, out byte[] payload))
                {
                    // Parse on the receive thread, enqueue the typed object; the main
                    // thread never touches the socket, this thread never touches Unity.
                    switch (type)
                    {
                        case MessageType.LoginResponse:
                            LoginResponse login = LoginResponse.FromPayload(payload);
                            if (login.Success)
                            {
                                PlayerId = login.PlayerId;
                                _sessionToken = login.SessionToken;
                            }
                            TcpMessages.Enqueue(login);
                            break;
                        case MessageType.ServerInfo:
                            TcpMessages.Enqueue(ServerInfoMessage.FromPayload(payload));
                            break;
                        case MessageType.ChatBroadcast:
                            TcpMessages.Enqueue(ChatBroadcast.FromPayload(payload));
                            break;
                        case MessageType.PlayerJoined:
                            TcpMessages.Enqueue(PlayerJoined.FromPayload(payload));
                            break;
                        case MessageType.PlayerLeft:
                            TcpMessages.Enqueue(PlayerLeft.FromPayload(payload));
                            break;
                        default:
                            // Unknown types are skipped: framing already consumed the
                            // payload, so the stream stays in sync. Counted, so the HUD
                            // can show it (a newer server sends types we do not know).
                            Interlocked.Increment(ref TcpMessagesSkipped);
                            break;
                    }
                }
                TcpMessages.Enqueue(new TcpDisconnectedEvent { Reason = "server closed the connection" });
            }
            catch (Exception ex)
            {
                if (_running)
                    TcpMessages.Enqueue(new TcpDisconnectedEvent { Reason = ex.Message });
            }
        }

        public void SendChat(string text)
        {
            NetworkStream? stream = _tcpStream;
            if (stream == null)
                return;
            TcpFraming.WriteMessage(stream, MessageType.ChatSend, new ChatSend { Text = text }.ToPayload());
        }

        /// <summary>
        /// Starts the UDP side after a successful login: a join thread that sends the
        /// session token, then a heartbeat thread that keeps the session alive.
        /// </summary>
        public void StartGame()
        {
            // Deliberately UNCONNECTED UDP with an explicit IPv4 bind and an
            // explicitly resolved IPv4 server address. UdpClient.Connect() installs
            // an OS-level source filter, and subtle address-family differences
            // (dual-mode sockets, "localhost" -> ::1) make replies vanish silently
            // on some runtimes.
            _serverEndPoint = new IPEndPoint(_serverAddress, _port);
            _udp = new UdpClient(new IPEndPoint(IPAddress.Any, 0));

            // Windows: an ICMP "port unreachable" from a send would otherwise surface
            // as ConnectionReset on subsequent Receive calls (same pitfall as the
            // server, FEJLLOG #7). Not supported on all platforms — best effort.
            try
            {
                _udp.Client.IOControl(unchecked((int)0x9800000C) /* SIO_UDP_CONNRESET */,
                    new byte[] { 0 }, null);
            }
            catch
            {
                // Ignored: nothing is received on this socket before M06.
            }

            byte[] joinDatagram = new JoinPacket { SessionToken = _sessionToken }.ToDatagram();
            Thread joinThread = new Thread(() =>
            {
                // Module 5: the server sends no snapshots yet, so nothing confirms the
                // join. Send it JoinAttempts times (one lost datagram is survivable),
                // then assume we are in and let the heartbeat take over. From M06 the
                // first snapshot is the confirmation, and the loop runs until it lands.
                for (int i = 0; i < JoinAttempts && _running; i++)
                {
                    UdpSend(joinDatagram);
                    Interlocked.Increment(ref JoinsSent);
                    Thread.Sleep(500);
                }
                _inGame = true;
            })
            {
                IsBackground = true,
                Name = "kka-join",
            };
            joinThread.Start();

            Thread heartbeatThread = new Thread(HeartbeatLoop)
            {
                IsBackground = true,
                Name = "kka-heartbeat",
            };
            heartbeatThread.Start();
        }

        /// <summary>Joins sent before the heartbeat takes over (Module 5 only).</summary>
        private const int JoinAttempts = 4;

        /// <summary>
        /// Keeps the session alive when there is nothing else to send.
        ///
        /// The server cannot tell "quiet" from "gone" — UDP has no connection to lose —
        /// so without a sign of life it drops you after UdpTimeoutSeconds. In Module 5
        /// the heartbeat is all the client sends once it has joined; from M06 inputs go
        /// out 30 times a second and reset the timer, and the heartbeat only fills the
        /// gaps (menu, death, chat).
        ///
        /// It runs on its OWN thread rather than out of the game loop, because the
        /// cases it exists for are exactly the ones where the game loop is not
        /// running.
        /// </summary>
        private void HeartbeatLoop()
        {
            while (_running)
            {
                Thread.Sleep(HeartbeatCheckIntervalMs);

                if (!_inGame)
                    continue;

                double quietMs = (DateTime.UtcNow - LastUdpSendUtc).TotalMilliseconds;
                if (quietMs < HeartbeatIntervalMs)
                    continue; // something went out recently; nothing to do

                UdpSend(new HeartbeatPacket { PlayerId = PlayerId }.ToDatagram());
                Interlocked.Increment(ref HeartbeatsSent);
            }
        }

        /// <summary>Send a heartbeat after this long without sending anything. Well
        /// inside the server's 5 s timeout, so a few lost ones are survivable.</summary>
        private const int HeartbeatIntervalMs = 1000;

        private const int HeartbeatCheckIntervalMs = 200;

        /// <summary>When we last put a packet on the wire — joins and heartbeats both
        /// count, because the server's timeout counts them all.</summary>
        public DateTime LastUdpSendUtc { get; private set; } = DateTime.UtcNow;

        /// <summary>Shown in the dev HUD.</summary>
        public int HeartbeatsSent;

        private void UdpSend(byte[] datagram)
        {
            UdpClient? udp = _udp;
            IPEndPoint? server = _serverEndPoint;
            if (udp == null || server == null)
                return;

            LastUdpSendUtc = DateTime.UtcNow;
            try { udp.Send(datagram, datagram.Length, server); }
            catch (Exception ex) when (ex is SocketException || ex is ObjectDisposedException) { }
        }

        public void Dispose()
        {
            _running = false;
            try { _tcp?.Close(); } catch { }
            try { _udp?.Close(); } catch { }
        }
    }
}
