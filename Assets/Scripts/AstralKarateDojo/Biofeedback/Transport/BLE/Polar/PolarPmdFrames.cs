using UnityEngine;

namespace AstralKarateDojo.Biofeedback.Transport.BLE.Polar
{
    /// <summary>
    /// Lightweight frame containers for Polar H10 PMD streaming.
    /// ECG samples are microvolts. ACC samples are milli-g (mg).
    /// SensorTimestampNs is the device timestamp; ReceivedUtcTicks is host receipt time.
    /// </summary>
    public readonly struct PolarPmdEcgFrame
    {
        public readonly long SensorTimestampNs;
        public readonly long ReceivedUtcTicks;
        public readonly int[] MicroVolts;

        public PolarPmdEcgFrame(long sensorTimestampNs, long receivedUtcTicks, int[] microVolts)
        {
            SensorTimestampNs = sensorTimestampNs;
            ReceivedUtcTicks = receivedUtcTicks;
            MicroVolts = microVolts;
        }
    }

    public readonly struct PolarAccSampleMg
    {
        public readonly short X;
        public readonly short Y;
        public readonly short Z;

        public PolarAccSampleMg(short x, short y, short z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public Vector3 ToG() => new Vector3(X, Y, Z) * 0.001f;
    }

    public readonly struct PolarPmdAccFrame
    {
        public readonly long SensorTimestampNs;
        public readonly long ReceivedUtcTicks;
        public readonly PolarAccSampleMg[] Samples;

        public PolarPmdAccFrame(long sensorTimestampNs, long receivedUtcTicks, PolarAccSampleMg[] samples)
        {
            SensorTimestampNs = sensorTimestampNs;
            ReceivedUtcTicks = receivedUtcTicks;
            Samples = samples;
        }
    }
}

