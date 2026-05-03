using System;
using System.Collections.Generic;

namespace TheBigRedButtonInstitute.IndirectParticles
{
    /// <summary>
    /// Runtime registry for named raw biofeedback signals.
    /// Producers publish unbounded finite floats by name; consumers read by name.
    /// </summary>
    public static class PEBiofeedbackRawSignalRegistry
    {
        private static readonly Dictionary<string, float> Signals =
            new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

        public static event Action<string, float> SignalUpdated;

        public static void Publish(string signalName, float value)
        {
            string key = NormalizeName(signalName);
            if (string.IsNullOrEmpty(key))
                return;

            if (float.IsNaN(value) || float.IsInfinity(value))
                return;

            bool changed = !Signals.TryGetValue(key, out float prev) || Math.Abs(prev - value) >= 0.000001f;
            Signals[key] = value;

            if (changed)
                SignalUpdated?.Invoke(key, value);
        }

        public static bool TryGetValue(string signalName, out float value)
        {
            value = 0f;
            string key = NormalizeName(signalName);
            if (string.IsNullOrEmpty(key))
                return false;

            if (!Signals.TryGetValue(key, out float found))
                return false;

            value = found;
            return true;
        }

        public static float ReadOrFallback(string signalName, float fallback)
        {
            return TryGetValue(signalName, out float value)
                ? value
                : fallback;
        }

        public static string[] GetSignalNames()
        {
            string[] names = new string[Signals.Count];
            int i = 0;
            foreach (KeyValuePair<string, float> pair in Signals)
                names[i++] = pair.Key;
            Array.Sort(names, StringComparer.OrdinalIgnoreCase);
            return names;
        }

        public static void Clear()
        {
            Signals.Clear();
        }

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlayModeEntry()
        {
            Signals.Clear();
            SignalUpdated = null;
        }

        private static string NormalizeName(string signalName)
        {
            return string.IsNullOrWhiteSpace(signalName) ? string.Empty : signalName.Trim();
        }
    }
}
