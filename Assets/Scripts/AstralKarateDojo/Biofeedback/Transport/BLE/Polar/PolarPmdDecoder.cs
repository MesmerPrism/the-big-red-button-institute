using System;

namespace AstralKarateDojo.Biofeedback.Transport.BLE.Polar
{
    /// <summary>
    /// Decodes PMD data frames into usable ECG/ACC samples.
    /// Assumes the Polar PMD frame layout (10-byte header, then payload).
    /// </summary>
    public static class PolarPmdDecoder
    {
        /// <summary>
        /// Decode Polar H10 ECG samples (3-byte little-endian signed integers).
        /// </summary>
        public static int[] DecodeEcgMicroVolts(byte[] pmdFrame)
        {
            if (pmdFrame == null) throw new ArgumentNullException(nameof(pmdFrame));
            if (pmdFrame.Length < 10) throw new ArgumentException("PMD frame too short", nameof(pmdFrame));
            if ((pmdFrame.Length - 10) % 3 != 0) throw new ArgumentException("Bad ECG PMD frame length", nameof(pmdFrame));

            int sampleCount = (pmdFrame.Length - 10) / 3;
            var microVolts = new int[sampleCount];

            int idx = 0;
            for (int offset = 10; offset < pmdFrame.Length; offset += 3)
            {
                int raw = pmdFrame[offset] | (pmdFrame[offset + 1] << 8) | (pmdFrame[offset + 2] << 16);
                // Sign-extend 24-bit to 32-bit
                if ((raw & 0x0080_0000) != 0)
                    raw |= unchecked((int)0xFF00_0000);
                microVolts[idx++] = raw;
            }

            return microVolts;
        }

        /// <summary>
        /// Decode Polar H10 ACC samples. Supports uncompressed frames (type 0x01) and compressed delta frames.
        /// </summary>
        public static PolarAccSampleMg[] DecodeAccMilliG(byte[] pmdFrame, bool isCompressed = false, byte frameTypeBase = 0x01)
        {
            if (pmdFrame == null) throw new ArgumentNullException(nameof(pmdFrame));
            if (pmdFrame.Length < 10) throw new ArgumentException("PMD frame too short", nameof(pmdFrame));

            // Uncompressed type 1: x,y,z as 16-bit signed integers (6 bytes per sample)
            if (!isCompressed && frameTypeBase == 0x01)
            {
                if ((pmdFrame.Length - 10) % 6 != 0)
                    throw new ArgumentException("Bad ACC PMD frame length for uncompressed type 1", nameof(pmdFrame));

                int sampleCount = (pmdFrame.Length - 10) / 6;
                var samples = new PolarAccSampleMg[sampleCount];

                int idx = 0;
                for (int offset = 10; offset < pmdFrame.Length; offset += 6)
                {
                    short x = BitConverter.ToInt16(pmdFrame, offset);
                    short y = BitConverter.ToInt16(pmdFrame, offset + 2);
                    short z = BitConverter.ToInt16(pmdFrame, offset + 4);
                    samples[idx++] = new PolarAccSampleMg(x, y, z);
                }
                return samples;
            }

            return DecodeCompressedAccFrame(pmdFrame, frameTypeBase);
        }

        private static PolarAccSampleMg[] DecodeCompressedAccFrame(byte[] pmdFrame, byte frameTypeBase)
        {
            if (pmdFrame.Length < 16)
                throw new ArgumentException("Compressed ACC frame too short", nameof(pmdFrame));

            var samples = new System.Collections.Generic.List<PolarAccSampleMg>(64);

            // Reference sample at offset 10: 3x 16-bit signed integers
            int refX = BitConverter.ToInt16(pmdFrame, 10);
            int refY = BitConverter.ToInt16(pmdFrame, 12);
            int refZ = BitConverter.ToInt16(pmdFrame, 14);
            samples.Add(new PolarAccSampleMg((short)refX, (short)refY, (short)refZ));

            if (pmdFrame.Length <= 16)
                return samples.ToArray();

            int bitOffset = 0;
            int byteOffset = 16;
            int remainingBytes = pmdFrame.Length - 16;

            // Typical for H10 ACC at 200Hz with 16-bit resolution
            int deltaBitWidth = 16;

            int prevX = refX, prevY = refY, prevZ = refZ;

            int bitsPerSample = deltaBitWidth * 3;
            int totalBits = remainingBytes * 8;
            int deltaSampleCount = totalBits / bitsPerSample;

            for (int i = 0; i < deltaSampleCount; i++)
            {
                int dx = ReadSignedBits(pmdFrame, byteOffset, ref bitOffset, deltaBitWidth);
                int dy = ReadSignedBits(pmdFrame, byteOffset, ref bitOffset, deltaBitWidth);
                int dz = ReadSignedBits(pmdFrame, byteOffset, ref bitOffset, deltaBitWidth);

                prevX += dx;
                prevY += dy;
                prevZ += dz;

                samples.Add(new PolarAccSampleMg(
                    ClampToInt16(prevX),
                    ClampToInt16(prevY),
                    ClampToInt16(prevZ)));
            }

            return samples.ToArray();
        }

        private static short ClampToInt16(int value)
        {
            if (value > short.MaxValue) return short.MaxValue;
            if (value < short.MinValue) return short.MinValue;
            return (short)value;
        }

        private static int ReadSignedBits(byte[] data, int startByteOffset, ref int bitOffset, int bitWidth)
        {
            if (bitWidth <= 0 || bitWidth > 32)
                throw new ArgumentOutOfRangeException(nameof(bitWidth));

            int totalBitPos = bitOffset;
            int bytePos = startByteOffset + (totalBitPos / 8);
            int bitInByte = totalBitPos % 8;

            long value = 0;
            int bitsRead = 0;

            while (bitsRead < bitWidth && bytePos < data.Length)
            {
                int bitsAvailableInByte = 8 - bitInByte;
                int bitsToRead = Math.Min(bitsAvailableInByte, bitWidth - bitsRead);

                int mask = (1 << bitsToRead) - 1;
                int bits = (data[bytePos] >> bitInByte) & mask;

                value |= (long)bits << bitsRead;
                bitsRead += bitsToRead;

                bytePos++;
                bitInByte = 0;
            }

            bitOffset += bitWidth;

            // Sign-extend
            if (bitWidth < 32 && (value & (1L << (bitWidth - 1))) != 0)
            {
                value |= ~((1L << bitWidth) - 1);
            }

            return (int)value;
        }

        public static long ReadTimestampNs(byte[] pmdFrame)
        {
            if (pmdFrame == null) throw new ArgumentNullException(nameof(pmdFrame));
            if (pmdFrame.Length < 9) throw new ArgumentException("PMD frame too short", nameof(pmdFrame));

            ulong ts = BitConverter.ToUInt64(pmdFrame, 1);
            return unchecked((long)ts);
        }
    }
}

