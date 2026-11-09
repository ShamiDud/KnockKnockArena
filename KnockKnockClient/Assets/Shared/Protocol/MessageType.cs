namespace KnockKnockArena.Shared.Protocol
{
    public enum MessageType : byte
    {
        // Client -> server (0 -> 99)
        LoginRequest = 0,
        ChatSend = 1,

        // Server -> client (100 -> 199)
        LoginResponse = 100,
        ServerInfo = 101,
        ChatBroadcast = 102,
        PlayerJoined = 103,
        PlayerLeft = 104,
    }
}