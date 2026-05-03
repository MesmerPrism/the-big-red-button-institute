using System;
using System.Globalization;
using System.Text;

namespace TheBigRedButtonInstitute.Diagnostics
{
    public readonly struct BigRedButtonOscDriveMessage
    {
        public BigRedButtonOscDriveMessage(
            string address,
            float value01,
            long sequenceId,
            long clientSendTimeUnixNs,
            long receivedTimeUnixNs,
            int replyPort,
            string firstArgumentType,
            string peer,
            string peerHost,
            int peerPort)
        {
            Address = address ?? string.Empty;
            Value01 = value01;
            SequenceId = sequenceId;
            ClientSendTimeUnixNs = clientSendTimeUnixNs;
            ReceivedTimeUnixNs = receivedTimeUnixNs;
            ReplyPort = replyPort;
            FirstArgumentType = firstArgumentType ?? string.Empty;
            Peer = peer ?? string.Empty;
            PeerHost = peerHost ?? string.Empty;
            PeerPort = peerPort;
        }

        public string Address { get; }
        public float Value01 { get; }
        public long SequenceId { get; }
        public long ClientSendTimeUnixNs { get; }
        public long ReceivedTimeUnixNs { get; }
        public int ReplyPort { get; }
        public string FirstArgumentType { get; }
        public string Peer { get; }
        public string PeerHost { get; }
        public int PeerPort { get; }
    }

    public static class BigRedButtonOscDriveMessageParser
    {
        public static bool TryDecodeDriveMessage(
            byte[] data,
            int length,
            string expectedAddress,
            string peer,
            string peerHost,
            int peerPort,
            long receivedTimeUnixNs,
            out BigRedButtonOscDriveMessage message,
            out string error)
        {
            message = default;
            error = string.Empty;

            if (data == null || length <= 0 || length > data.Length)
            {
                error = "empty packet";
                return false;
            }

            try
            {
                var limit = length;
                var address = ReadPaddedString(data, 0, limit, out var cursor);
                if (!address.StartsWith("/", StringComparison.Ordinal))
                {
                    error = "invalid OSC address";
                    return false;
                }

                if (!string.IsNullOrWhiteSpace(expectedAddress) &&
                    !string.Equals(address, expectedAddress, StringComparison.Ordinal))
                {
                    error = "unexpected OSC address";
                    return false;
                }

                var typeTags = ReadPaddedString(data, cursor, limit, out cursor);
                if (!typeTags.StartsWith(",", StringComparison.Ordinal))
                {
                    error = "invalid OSC type tags";
                    return false;
                }

                var tags = typeTags.Substring(1);
                var value01 = 0f;
                var sequenceId = 0L;
                var clientSendTimeUnixNs = 0L;
                var replyPort = 0;
                var firstArgumentType = string.Empty;
                var argumentCount = 0;
                for (var i = 0; i < tags.Length; i++)
                {
                    var tag = tags[i];
                    var argument = ReadArgument(data, limit, tag, ref cursor);
                    if (argumentCount == 0)
                    {
                        value01 = Clamp01(argument.AsFloat());
                        firstArgumentType = tag.ToString();
                    }
                    else if (argumentCount == 1)
                    {
                        sequenceId = argument.AsInt64();
                    }
                    else if (argumentCount == 2)
                    {
                        clientSendTimeUnixNs = argument.AsInt64();
                    }
                    else if (argumentCount == 3)
                    {
                        replyPort = ClampPort(argument.AsInt64());
                    }

                    argumentCount++;
                }

                if (argumentCount == 0)
                {
                    error = "OSC drive packet has no arguments";
                    return false;
                }

                if (cursor != limit)
                {
                    error = "OSC packet has trailing bytes";
                    return false;
                }

                message = new BigRedButtonOscDriveMessage(
                    address,
                    value01,
                    sequenceId,
                    clientSendTimeUnixNs,
                    receivedTimeUnixNs,
                    replyPort,
                    firstArgumentType,
                    peer,
                    peerHost,
                    peerPort);
                return true;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is FormatException || ex is OverflowException)
            {
                error = ex.Message;
                return false;
            }
        }

        public static byte[] EncodeDriveAcknowledgement(
            string address,
            long sequenceId,
            float value01,
            long clientSendTimeUnixNs,
            long unityReceiveTimeUnixNs,
            long unityAckSendTimeUnixNs,
            bool acceptedPulse)
        {
            using var stream = new System.IO.MemoryStream();
            WritePaddedString(stream, string.IsNullOrWhiteSpace(address) ? "/rusty-xr/drive/ack" : address);
            WritePaddedString(stream, acceptedPulse ? ",isssfT" : ",isssfF");
            WriteInt32BigEndian(stream, ClampInt32(sequenceId));
            WritePaddedString(stream, clientSendTimeUnixNs > 0 ? clientSendTimeUnixNs.ToString(CultureInfo.InvariantCulture) : string.Empty);
            WritePaddedString(stream, unityReceiveTimeUnixNs > 0 ? unityReceiveTimeUnixNs.ToString(CultureInfo.InvariantCulture) : string.Empty);
            WritePaddedString(stream, unityAckSendTimeUnixNs > 0 ? unityAckSendTimeUnixNs.ToString(CultureInfo.InvariantCulture) : string.Empty);
            WriteInt32BigEndian(stream, BitConverter.SingleToInt32Bits(Clamp01(value01)));
            return stream.ToArray();
        }

        static OscArgumentValue ReadArgument(byte[] data, int limit, char tag, ref int cursor)
        {
            switch (tag)
            {
                case 'f':
                    Require(data, cursor, limit, 4);
                    var floatBits = ReadInt32BigEndian(data, cursor);
                    cursor += 4;
                    return OscArgumentValue.Float(BitConverter.Int32BitsToSingle(floatBits));
                case 'i':
                    Require(data, cursor, limit, 4);
                    var intValue = ReadInt32BigEndian(data, cursor);
                    cursor += 4;
                    return OscArgumentValue.Int(intValue);
                case 's':
                    var stringValue = ReadPaddedString(data, cursor, limit, out cursor);
                    return OscArgumentValue.String(stringValue);
                case 'T':
                    return OscArgumentValue.Bool(true);
                case 'F':
                    return OscArgumentValue.Bool(false);
                default:
                    throw new ArgumentException("unsupported OSC type tag: " + tag);
            }
        }

        static string ReadPaddedString(byte[] data, int offset, int limit, out int nextOffset)
        {
            if (offset >= limit)
            {
                throw new ArgumentException("unexpected end of OSC packet");
            }

            var cursor = offset;
            while (cursor < limit && data[cursor] != 0)
            {
                cursor++;
            }

            if (cursor >= limit)
            {
                throw new ArgumentException("OSC string missing null terminator");
            }

            var value = Encoding.UTF8.GetString(data, offset, cursor - offset);
            nextOffset = offset + PaddedLength(cursor - offset + 1);
            Require(data, offset, limit, nextOffset - offset);
            return value;
        }

        static int ReadInt32BigEndian(byte[] data, int offset)
        {
            return (data[offset] << 24) |
                   (data[offset + 1] << 16) |
                   (data[offset + 2] << 8) |
                   data[offset + 3];
        }

        static void WritePaddedString(System.IO.Stream stream, string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
            stream.Write(bytes, 0, bytes.Length);
            stream.WriteByte(0);
            while (stream.Length % 4 != 0)
            {
                stream.WriteByte(0);
            }
        }

        static void WriteInt32BigEndian(System.IO.Stream stream, int value)
        {
            stream.WriteByte((byte)((value >> 24) & 0xFF));
            stream.WriteByte((byte)((value >> 16) & 0xFF));
            stream.WriteByte((byte)((value >> 8) & 0xFF));
            stream.WriteByte((byte)(value & 0xFF));
        }

        static int PaddedLength(int value) => value + ((4 - (value % 4)) % 4);

        static void Require(byte[] data, int offset, int limit, int length)
        {
            if (data == null || offset < 0 || length < 0 || offset + length > limit)
            {
                throw new ArgumentException("unexpected end of OSC packet");
            }
        }

        static float Clamp01(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                return 0f;
            }

            if (value < 0f)
            {
                return 0f;
            }

            return value > 1f ? 1f : value;
        }

        static int ClampPort(long value)
        {
            if (value <= 0L || value > 65535L)
            {
                return 0;
            }

            return (int)value;
        }

        static int ClampInt32(long value)
        {
            if (value < int.MinValue)
            {
                return int.MinValue;
            }

            if (value > int.MaxValue)
            {
                return int.MaxValue;
            }

            return (int)value;
        }

        readonly struct OscArgumentValue
        {
            readonly float _floatValue;
            readonly long _intValue;
            readonly string _stringValue;
            readonly bool _boolValue;
            readonly char _kind;

            OscArgumentValue(char kind, float floatValue, long intValue, string stringValue, bool boolValue)
            {
                _kind = kind;
                _floatValue = floatValue;
                _intValue = intValue;
                _stringValue = stringValue ?? string.Empty;
                _boolValue = boolValue;
            }

            public static OscArgumentValue Float(float value) => new OscArgumentValue('f', value, 0L, string.Empty, false);
            public static OscArgumentValue Int(long value) => new OscArgumentValue('i', 0f, value, string.Empty, false);
            public static OscArgumentValue String(string value) => new OscArgumentValue('s', 0f, 0L, value, false);
            public static OscArgumentValue Bool(bool value) => new OscArgumentValue('b', value ? 1f : 0f, value ? 1L : 0L, string.Empty, value);

            public float AsFloat()
            {
                switch (_kind)
                {
                    case 'f':
                        return _floatValue;
                    case 'i':
                        return _intValue;
                    case 's':
                        return float.Parse(_stringValue.Trim(), CultureInfo.InvariantCulture);
                    case 'b':
                        return _boolValue ? 1f : 0f;
                    default:
                        return 0f;
                }
            }

            public long AsInt64()
            {
                switch (_kind)
                {
                    case 'f':
                        return (long)Math.Round(_floatValue);
                    case 'i':
                        return _intValue;
                    case 's':
                        return long.Parse(_stringValue.Trim(), CultureInfo.InvariantCulture);
                    case 'b':
                        return _boolValue ? 1L : 0L;
                    default:
                        return 0L;
                }
            }
        }
    }
}
