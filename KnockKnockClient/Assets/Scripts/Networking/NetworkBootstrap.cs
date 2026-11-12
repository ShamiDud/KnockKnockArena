using System;
using System.Collections.Generic;
using KnockKnockArena.Shared.Protocol.Messages;
using UnityEngine;

namespace KnockKnockArena.Networking
{
    /// <summary>
    /// THE coupling point (HANDOVER 6.1): the single scene object that owns the
    /// network connection. Everything network-related funnels through here — the
    /// student base project is produced by deleting the Networking folder and this
    /// object from the scene.
    ///
    /// MODULE 4: TCP only. Login, chat and the joined/left feed. The receive thread in
    /// NetworkClient parses and queues; Update() drains the queue on the main thread —
    /// the only thread allowed to touch Unity. The dev UI is NetworkHud (handed out):
    /// it only reads the public properties below, listens to the two events and calls
    /// the public methods. Nothing here draws anything.
    /// </summary>
    public sealed class NetworkBootstrap : MonoBehaviour
    {
        [Header("Server")]
        public string Host = "localhost";
        public int Port = 36363;
        public string Username = "test";
        public string Password = "test";

        public string StatusLine { get; private set; } = "";
        public string ServerInfoLine { get; private set; } = "";
        public bool IsConnecting { get; private set; }
        public bool IsLoggedIn => _client != null && !IsConnecting;
        public long TcpMessagesSkipped => _client?.TcpMessagesSkipped ?? 0;

        public event Action<string> ChatReceived;
        public event Action<string> PlayerFeed;

        private NetworkClient _client;

        private readonly Dictionary<byte, string> _usernames = new Dictionary<byte, string>();

        private void Update()
        {
            if (_client != null)
                DrainTcpMessages();
        }

        // ------------------------------------------------------------- connection

        public void Connect()
        {
            IsConnecting = true;
            StatusLine = $"Connecting to {Host}:{Port}...";
            _client = new NetworkClient();
            _client.ConnectAndLogin(Host, Port, Username, Password);
        }

        public void Disconnect(string reason)
        {
            _client?.Dispose();
            _client = null;
            IsConnecting = false;
            StatusLine = reason;
            ServerInfoLine = "";
            _usernames.Clear();
        }

        public void SendChat(string text)
        {
            _client?.SendChat(text);
        }

        private void OnDestroy()
        {
            _client?.Dispose();
        }

        private void DrainTcpMessages()
        {
            while (_client != null && _client.TcpMessages.TryDequeue(out object message))
            {
                switch (message)
                {
                    case TcpConnectedEvent _:
                        StatusLine = "Connected, logging in...";
                        break;

                    case LoginResponse login when login.Success:
                        StatusLine = $"Logged in as player #{login.PlayerId}";
                        IsConnecting = false;
                        _usernames[login.PlayerId] = Username;
                        break;

                    case LoginResponse login:
                        Disconnect($"Login failed: {login.Error}");
                        break;

                    case ServerInfoMessage info:
                        ServerInfoLine = $"server v{info.ServerVersion} | tick {info.TickRate} Hz | " +
                                         $"{info.PlayersOnline}/{info.MaxPlayers} online";
                        break;

                    case ChatBroadcast chat:
                        ChatReceived?.Invoke($"[{chat.Username}] {chat.Text}");
                        break;

                    case PlayerJoined joined:
                        _usernames[joined.PlayerId] = joined.Username;
                        PlayerFeed?.Invoke($"{joined.Username} joined");
                        break;

                    case PlayerLeft left:
                        _usernames.Remove(left.PlayerId);
                        PlayerFeed?.Invoke($"{left.Username} left");
                        break;

                    case TcpDisconnectedEvent disconnected:
                        Disconnect($"Disconnected: {disconnected.Reason}");
                        break;
                }
            }
        }
    }
}
