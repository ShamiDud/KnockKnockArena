using System.IO;

namespace KnockKnockArena.Shared.Protocol.Udp
{
    /// <summary>
    /// Client -> broadcast. "Is there a KnockKnock server on this network?" Sent to
    /// 255.255.255.255 on the game port; every server on the LAN answers with a
    /// <see cref="DiscoveryResponsePacket"/>. New in protocol v6.
    ///
    /// Datagram: [type=3] [version] [nonce:uint32]   (6 bytes)
    ///
    /// The nonce is echoed in every answer, so the client can tell answers to THIS
    /// search from stray datagrams.
    /// </summary>
    public sealed class DiscoveryRequestPacket
    {
        public const int Size = 6;

        public uint Nonce;

        /// <summary>The protocol version the asking client speaks (the header byte).</summary>
        public byte ClientVersion = ProtocolConstants.Version;

        public byte[] ToDatagram()
        {
            return UdpPacketIO.Build(UdpPacketType.DiscoveryRequest, writer => writer.Write(Nonce));
        }

        /// <summary>
        /// Parses a request WITHOUT rejecting other protocol versions. Discovery is the
        /// one exchange that must work across versions: a server should still show up
        /// in an older or newer client's list — marked incompatible — instead of
        /// being invisible. The answer always carries the server's own version.
        /// </summary>
        public static DiscoveryRequestPacket FromDatagramAnyVersion(byte[] datagram)
        {
            if (datagram.Length < Size)
                throw new ProtocolException("malformed payload");
            using (MemoryStream stream = new MemoryStream(datagram))
            using (BinaryReader reader = new BinaryReader(stream))
            {
                reader.ReadByte(); // type
                return new DiscoveryRequestPacket
                {
                    ClientVersion = reader.ReadByte(),
                    Nonce = reader.ReadUInt32(),
                };
            }
        }
    }

    /// <summary>
    /// Server -> the asking client (unicast). New in protocol v6.
    ///
    /// Datagram: [type=102] [version] [nonce:uint32] [gamePort:ushort] [httpPort:ushort]
    ///           [playersOnline:byte] [maxPlayers:byte] [serverName:string] [mapName:string]
    ///
    /// The header's version byte IS the server's protocol version. A client reads it
    /// with <see cref="PeekVersion"/> before parsing the rest: a server on another
    /// version is listed as incompatible rather than misparsed.
    /// </summary>
    public sealed class DiscoveryResponsePacket
    {
        public uint Nonce;
        public ushort GamePort;
        public ushort HttpPort;
        public byte PlayersOnline;
        public byte MaxPlayers;
        public string ServerName = "";
        public string MapName = "";

        public byte[] ToDatagram()
        {
            return UdpPacketIO.Build(UdpPacketType.DiscoveryResponse, writer =>
            {
                writer.Write(Nonce);
                writer.Write(GamePort);
                writer.Write(HttpPort);
                writer.Write(PlayersOnline);
                writer.Write(MaxPlayers);
                ProtocolSerialization.WriteString(writer, ServerName);
                ProtocolSerialization.WriteString(writer, MapName);
            });
        }

        /// <summary>The server's protocol version: byte 1 of any datagram.</summary>
        public static byte PeekVersion(byte[] datagram)
        {
            if (datagram.Length < 2)
                throw new ProtocolException($"datagram too short ({datagram.Length} B)");
            return datagram[1];
        }

        public static DiscoveryResponsePacket FromDatagram(byte[] datagram)
        {
            return UdpPacketIO.Parse(datagram, reader => new DiscoveryResponsePacket
            {
                Nonce = reader.ReadUInt32(),
                GamePort = reader.ReadUInt16(),
                HttpPort = reader.ReadUInt16(),
                PlayersOnline = reader.ReadByte(),
                MaxPlayers = reader.ReadByte(),
                ServerName = ProtocolSerialization.ReadString(reader),
                MapName = ProtocolSerialization.ReadString(reader),
            });
        }
    }
}
