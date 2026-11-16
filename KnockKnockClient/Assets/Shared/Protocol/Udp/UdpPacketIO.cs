using System;
using System.IO;

namespace KnockKnockArena.Shared.Protocol.Udp
{
    /// <summary>
    /// Builds and parses UDP datagrams. Every datagram starts with
    /// [type:byte] [version:byte], followed by the packet's fields.
    /// </summary>
    public static class UdpPacketIO
    {
        /// <summary>Reads the packet type from a received datagram without consuming it.</summary>
        public static UdpPacketType PeekType(byte[] datagram)
        {
            if (datagram.Length < 2)
                throw new ProtocolException($"datagram too short ({datagram.Length} B)");
            return (UdpPacketType)datagram[0];
        }

        public static byte[] Build(UdpPacketType type, Action<BinaryWriter> writeFields)
        {
            using (MemoryStream stream = new MemoryStream())
            {
                using (BinaryWriter writer = new BinaryWriter(stream))
                {
                    writer.Write((byte)type);
                    writer.Write(ProtocolConstants.Version);
                    writeFields(writer);
                }
                return stream.ToArray();
            }
        }

        /// <summary>
        /// Parses a datagram's fields (after the type byte), validating the version.
        /// Truncated datagrams surface as ProtocolException("malformed payload").
        /// </summary>
        public static T Parse<T>(byte[] datagram, Func<BinaryReader, T> readFields)
        {
            using (MemoryStream stream = new MemoryStream(datagram))
            using (BinaryReader reader = new BinaryReader(stream))
            {
                try
                {
                    reader.ReadByte(); // type, already dispatched on via PeekType
                    ProtocolSerialization.ReadAndValidateVersion(reader);
                    return readFields(reader);
                }
                catch (EndOfStreamException)
                {
                    throw new ProtocolException("malformed payload");
                }
            }
        }
    }
}
