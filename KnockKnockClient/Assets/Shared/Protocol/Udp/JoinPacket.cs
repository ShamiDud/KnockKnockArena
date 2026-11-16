using System.IO;

namespace KnockKnockArena.Shared.Protocol.Udp
{
    /// <summary>
    /// Client -> server. Binds the client's UDP endpoint to a logged-in user via the
    /// session token from the TCP LoginResponse.
    ///
    /// There is no join-ack packet: the confirmation is that snapshots start arriving.
    /// UDP is unreliable, so the client must resend Join every 0.5 s until it receives
    /// its first snapshot.
    ///
    /// Datagram: [type=0] [version] [sessionToken:16 bytes]
    /// </summary>
    public sealed class JoinPacket
    {
        public byte[] SessionToken = new byte[ProtocolConstants.SessionTokenLength];

        public byte[] ToDatagram()
        {
            return UdpPacketIO.Build(UdpPacketType.Join, writer =>
            {
                writer.Write(SessionToken);
            });
        }

        public static JoinPacket FromDatagram(byte[] datagram)
        {
            return UdpPacketIO.Parse(datagram, reader =>
            {
                // ReadBytes does not throw when the datagram is short: it returns fewer
                // bytes. A truncated join is malformed, not "a token nobody has".
                byte[] token = reader.ReadBytes(ProtocolConstants.SessionTokenLength);
                if (token.Length != ProtocolConstants.SessionTokenLength)
                    throw new ProtocolException("malformed payload");
                return new JoinPacket { SessionToken = token };
            });
        }
    }

    /// <summary>
    /// Client -> server. Keeps the connection alive when the client is not sending
    /// inputs. Input packets also count as heartbeats, so a client that sends inputs
    /// continuously never needs to send this.
    ///
    /// Datagram: [type=2] [version] [playerId:byte]
    /// </summary>
    public sealed class HeartbeatPacket
    {
        public byte PlayerId;

        public byte[] ToDatagram()
        {
            return UdpPacketIO.Build(UdpPacketType.Heartbeat, writer =>
            {
                writer.Write(PlayerId);
            });
        }

        public static HeartbeatPacket FromDatagram(byte[] datagram)
        {
            return UdpPacketIO.Parse(datagram, reader => new HeartbeatPacket
            {
                PlayerId = reader.ReadByte(),
            });
        }
    }
}
