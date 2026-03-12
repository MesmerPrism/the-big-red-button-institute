using System.Collections.Generic;
using UnityEngine;

namespace AstralKarateDojo.IndirectParticles.Biofeedback.Breathing
{
    internal enum PEQuantileSamplingMode
    {
        RoundedIndex = 0,
        LinearInterpolation = 1
    }

    internal static class PEAdaptiveBoundsMath
    {
        public static bool TryComputeQuantileBoundsInPlace(
            List<float> scratchValues,
            float lowerQuantile,
            float upperQuantile,
            PEQuantileSamplingMode samplingMode,
            out float lower,
            out float upper)
        {
            lower = 0f;
            upper = 0f;

            if (scratchValues == null || scratchValues.Count == 0)
                return false;

            scratchValues.Sort();
            lower = EvaluateSortedQuantile(scratchValues, lowerQuantile, samplingMode);
            upper = EvaluateSortedQuantile(scratchValues, upperQuantile, samplingMode);
            return upper > lower;
        }

        public static float EvaluateSortedQuantile(
            List<float> sortedValues,
            float quantile,
            PEQuantileSamplingMode samplingMode)
        {
            if (sortedValues == null || sortedValues.Count == 0)
                return 0f;

            quantile = Mathf.Clamp01(quantile);
            int maxIndex = sortedValues.Count - 1;

            if (samplingMode == PEQuantileSamplingMode.RoundedIndex)
            {
                int roundedIndex = Mathf.Clamp(Mathf.RoundToInt(maxIndex * quantile), 0, maxIndex);
                return sortedValues[roundedIndex];
            }

            float position = maxIndex * quantile;
            int lo = Mathf.FloorToInt(position);
            int hi = Mathf.CeilToInt(position);
            if (lo == hi)
                return sortedValues[lo];

            float t = position - lo;
            return Mathf.Lerp(sortedValues[lo], sortedValues[hi], t);
        }

        public static void ApplyEdgeEase(ref float min, ref float max, float edgeEase01)
        {
            float span = Mathf.Max(max - min, 0f);
            if (span < 1e-6f)
                return;

            float shrink = Mathf.Clamp(span * Mathf.Clamp01(edgeEase01), 0f, span * 0.49f);
            min += shrink;
            max -= shrink;
        }

        public static void EnforceSpanBounds(ref float min, ref float max, float minSpan, float maxSpan)
        {
            minSpan = Mathf.Max(0.0001f, minSpan);
            if (!float.IsInfinity(maxSpan))
                maxSpan = Mathf.Max(minSpan, maxSpan);

            float center = (min + max) * 0.5f;
            float span = Mathf.Max(max - min, minSpan);
            if (!float.IsInfinity(maxSpan))
                span = Mathf.Min(span, maxSpan);

            float half = span * 0.5f;
            min = center - half;
            max = center + half;
        }

        public static float ComputeExponentialLerp(float speed, float dt)
        {
            if (speed <= 0f || dt <= 0f)
                return 0f;

            return 1f - Mathf.Exp(-speed * dt);
        }
    }
}
