using UnityEngine;

namespace TheBigRedButtonInstitute.CameraProjection
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(40)]
    public sealed class BrbCameraAnchoredTransform : MonoBehaviour
    {
        [SerializeField] Transform source;
        [SerializeField] Vector3 localPosition = new(0f, 0f, 1.35f);
        [SerializeField] Vector3 localEulerAngles;
        [SerializeField] bool smooth;
        [SerializeField] [Min(0.01f)] float followSharpness = 24f;

        public void Configure(Transform newSource, Vector3 newLocalPosition, Vector3 newLocalEulerAngles, bool enableSmoothing)
        {
            source = newSource;
            localPosition = newLocalPosition;
            localEulerAngles = newLocalEulerAngles;
            smooth = enableSmoothing;
            Apply(anchorImmediately: true);
        }

        void Reset()
        {
            if (Camera.main != null)
            {
                source = Camera.main.transform;
            }
        }

        void LateUpdate()
        {
            Apply(anchorImmediately: !Application.isPlaying || !smooth);
        }

        void Apply(bool anchorImmediately)
        {
            if (source == null)
            {
                return;
            }

            Vector3 targetPosition = source.TransformPoint(localPosition);
            Quaternion targetRotation = source.rotation * Quaternion.Euler(localEulerAngles);
            if (anchorImmediately)
            {
                transform.SetPositionAndRotation(targetPosition, targetRotation);
                return;
            }

            float t = 1f - Mathf.Exp(-followSharpness * Time.unscaledDeltaTime);
            transform.position = Vector3.Lerp(transform.position, targetPosition, t);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, t);
        }
    }
}
