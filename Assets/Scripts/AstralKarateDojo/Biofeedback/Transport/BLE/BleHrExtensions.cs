using System;

namespace AstralKarateDojo.Biofeedback.Transport.BLE
{
    public static class BleHrExtensions
    {
        // Implements parsing for the standard Heart Rate Measurement characteristic (0x2A37).
        public static bool Is8BitHrData(this byte[] b)
        {
            if (b == null || b.Length == 0) return true;
            return (b[0] & 0b00000001) == 0;
        }

        public static bool HasEnergyExpendedStatus(this byte[] b)
        {
            if (b == null || b.Length == 0) return false;
            return (b[0] & 0b00001000) != 0;
        }

        public static bool HasRrIntervalValue(this byte[] b)
        {
            if (b == null || b.Length == 0) return false;
            return (b[0] & 0b00010000) != 0;
        }

        public static ushort GetHr(this byte[] b)
        {
            if (b == null || b.Length < 2) return 0;
            if (Is8BitHrData(b))
                return b[1];
            if (b.Length < 3) return 0;
            return (ushort)(b[1] | b[2] << 8);
        }

        public static float[] GetRrIntervals(this byte[] b)
        {
            if (b == null || b.Length < 2) return Array.Empty<float>();

            int idx = Is8BitHrData(b) ? 2 : 3;
            if (HasEnergyExpendedStatus(b))
                idx += 2;

            int length = (b.Length - idx) / 2;
            if (length <= 0) return Array.Empty<float>();

            var intervals = new float[length];
            for (int i = 0; i < length; i++)
            {
                intervals[i] = (b[idx] | b[idx + 1] << 8) * 0.9765625f;
                idx += 2;
            }

            return intervals;
        }
    }
}

