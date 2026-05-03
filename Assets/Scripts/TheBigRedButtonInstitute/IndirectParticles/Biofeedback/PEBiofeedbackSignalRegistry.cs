using System;
using System.Collections.Generic;
using UnityEngine;

namespace TheBigRedButtonInstitute.IndirectParticles
{
    /// <summary>
    /// Runtime registry for named 0..1 biofeedback signals.
    /// Producers publish by name, consumers read by name.
    /// </summary>
    public static class PEBiofeedbackSignalRegistry
    {
        private static readonly Dictionary<string, float> Signals =
            new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

        public static event Action<string, float> SignalUpdated;

        public static void Publish(string signalName, float value01)
        {
            string key = NormalizeName(signalName);
            if (string.IsNullOrEmpty(key))
                return;

            float clamped = Mathf.Clamp01(value01);
            bool changed = !Signals.TryGetValue(key, out float prev) || Mathf.Abs(prev - clamped) >= 0.0001f;
            Signals[key] = clamped;

            if (changed)
                SignalUpdated?.Invoke(key, clamped);
        }

        public static bool TryGetValue01(string signalName, out float value01)
        {
            value01 = 0f;
            string key = NormalizeName(signalName);
            if (string.IsNullOrEmpty(key))
                return false;

            if (!Signals.TryGetValue(key, out float found))
                return false;

            value01 = Mathf.Clamp01(found);
            return true;
        }

        public static float ReadOrFallback01(string signalName, float fallback01)
        {
            return TryGetValue01(signalName, out float value01)
                ? value01
                : Mathf.Clamp01(fallback01);
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

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
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
