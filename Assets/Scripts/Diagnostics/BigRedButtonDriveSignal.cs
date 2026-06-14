using UnityEngine;

namespace TheBigRedButtonInstitute.Diagnostics
{
    public static class BigRedButtonDriveSignal
    {
        public const string DefaultOscDriveAddress = "/brb/manifold/drive/button";
        public const string DefaultOscAcknowledgementAddress = "/brb/manifold/drive/ack";

        public static bool ShouldTrigger(float previousValue01, float nextValue01, float threshold01, bool risingEdgeOnly)
        {
            var threshold = Mathf.Clamp01(threshold01);
            var previous = Mathf.Clamp01(previousValue01);
            var next = Mathf.Clamp01(nextValue01);
            if (risingEdgeOnly)
            {
                return previous < threshold && next >= threshold;
            }

            return next >= threshold;
        }
    }
}
