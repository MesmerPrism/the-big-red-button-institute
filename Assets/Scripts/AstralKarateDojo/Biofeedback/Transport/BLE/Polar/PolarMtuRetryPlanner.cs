using System.Collections.Generic;

namespace AstralKarateDojo.Biofeedback.Transport.BLE.Polar
{
    /// <summary>
    /// Shared helper for building deterministic MTU retry candidates.
    /// Keeps desired MTU first, removes duplicates, and filters invalid values.
    /// </summary>
    public static class PolarMtuRetryPlanner
    {
        public static int[] BuildOrderedCandidates(int desiredMtu, int[] retryCandidates)
        {
            var ordered = new List<int>();
            var seen = new HashSet<int>();

            if (desiredMtu > 23 && seen.Add(desiredMtu))
                ordered.Add(desiredMtu);

            if (retryCandidates == null || retryCandidates.Length == 0)
                return ordered.ToArray();

            for (int i = 0; i < retryCandidates.Length; i++)
            {
                int candidate = retryCandidates[i];
                if (candidate <= 23)
                    continue;
                if (!seen.Add(candidate))
                    continue;

                ordered.Add(candidate);
            }

            return ordered.ToArray();
        }
    }
}

