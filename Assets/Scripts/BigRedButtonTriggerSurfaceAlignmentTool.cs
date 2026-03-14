using System;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace TheBigRedButtonInstitute
{
    [DisallowMultipleComponent]
    public sealed class BigRedButtonTriggerSurfaceAlignmentTool : MonoBehaviour
    {
        [Serializable]
        sealed class AlignmentSnapshot
        {
            public Vector3 localPosition;
            public Vector3 localEuler;
            public Vector3 boxSize;
            public string savedAtUtc;
        }

        [SerializeField] Transform alignmentRoot;
        [SerializeField] bool autoResolveControllerAnchor = true;
        [SerializeField] Transform rightControllerAnchor;
        [SerializeField, Min(0.02f)] float grabDistance = 0.2f;

        Collider _targetCollider;
        Vector3 _controllerLocalPositionOffset;
        Quaternion _controllerLocalRotationOffset = Quaternion.identity;
        bool _grabbed;

        public bool IsGrabbed => _grabbed;

        public void Configure(Transform root)
        {
            alignmentRoot = root;
            ResolveControllerAnchor(forceRefresh: rightControllerAnchor == null);
            _targetCollider ??= GetComponent<Collider>();
        }

        void Awake()
        {
            _targetCollider = GetComponent<Collider>();
        }

        void OnEnable()
        {
            _targetCollider ??= GetComponent<Collider>();
            ResolveControllerAnchor(forceRefresh: rightControllerAnchor == null);
        }

        void OnDisable()
        {
            _grabbed = false;
        }

        void LateUpdate()
        {
            ResolveControllerAnchor(forceRefresh: false);
            if (rightControllerAnchor == null)
            {
                return;
            }

            if (_grabbed)
            {
                if (OVRInput.GetUp(OVRInput.RawButton.RIndexTrigger))
                {
                    EndGrab();
                    return;
                }

                UpdateGrabPose();
                return;
            }

            if (OVRInput.GetDown(OVRInput.RawButton.RIndexTrigger) && CanStartGrab())
            {
                BeginGrab();
            }
        }

        bool CanStartGrab()
        {
            if (rightControllerAnchor == null)
            {
                return false;
            }

            if (_targetCollider == null)
            {
                return true;
            }

            var controllerPosition = rightControllerAnchor.position;
            var closestPoint = _targetCollider.ClosestPoint(controllerPosition);
            return (closestPoint - controllerPosition).sqrMagnitude <= grabDistance * grabDistance;
        }

        void BeginGrab()
        {
            if (rightControllerAnchor == null)
            {
                return;
            }

            _grabbed = true;
            var controllerRotation = rightControllerAnchor.rotation;
            _controllerLocalPositionOffset = Quaternion.Inverse(controllerRotation) * (transform.position - rightControllerAnchor.position);
            _controllerLocalRotationOffset = Quaternion.Inverse(controllerRotation) * transform.rotation;
            UpdateGrabPose();
        }

        void EndGrab()
        {
            _grabbed = false;
            ReportPose();
        }

        void UpdateGrabPose()
        {
            var controllerRotation = rightControllerAnchor.rotation;
            var targetPosition = rightControllerAnchor.position + controllerRotation * _controllerLocalPositionOffset;
            var targetRotation = controllerRotation * _controllerLocalRotationOffset;
            transform.SetPositionAndRotation(targetPosition, targetRotation);
            transform.localScale = Vector3.one;
        }

        void ResolveControllerAnchor(bool forceRefresh)
        {
            if (rightControllerAnchor != null && !forceRefresh)
            {
                return;
            }

            if (!autoResolveControllerAnchor && !forceRefresh)
            {
                return;
            }

            var cameraRig = FindAnyObjectByType<OVRCameraRig>();
            if (cameraRig == null)
            {
                return;
            }

            rightControllerAnchor = cameraRig.rightControllerInHandAnchor != null
                ? cameraRig.rightControllerInHandAnchor
                : cameraRig.rightControllerAnchor != null
                    ? cameraRig.rightControllerAnchor
                    : cameraRig.rightHandOnControllerAnchor != null
                        ? cameraRig.rightHandOnControllerAnchor
                        : cameraRig.rightHandAnchor;
        }

        void ReportPose()
        {
            var root = alignmentRoot != null ? alignmentRoot : transform.parent;
            var localPosition = root != null ? root.InverseTransformPoint(transform.position) : transform.localPosition;
            var localRotation = root != null ? Quaternion.Inverse(root.rotation) * transform.rotation : transform.localRotation;
            var localEuler = localRotation.eulerAngles;
            var controller = root != null ? root.GetComponent<BigRedButtonManualPressController>() : null;
            controller?.SetTriggerSurfacePoseOverride(localPosition, localEuler);
#if UNITY_EDITOR
            WriteEditorSnapshot(localPosition, localEuler, _targetCollider is BoxCollider boxCollider ? boxCollider.size : Vector3.zero);
#endif

            Debug.Log(
                $"[BigRedButtonAlignment] Trigger surface saved localPosition={FormatVector(localPosition)} " +
                $"localEuler={FormatVector(localEuler)} localRotation={FormatQuaternion(localRotation)} " +
                $"worldPosition={FormatVector(transform.position)} worldEuler={FormatVector(transform.eulerAngles)}",
                this);
        }

#if UNITY_EDITOR
        static void WriteEditorSnapshot(Vector3 localPosition, Vector3 localEuler, Vector3 boxSize)
        {
            var snapshot = new AlignmentSnapshot
            {
                localPosition = localPosition,
                localEuler = localEuler,
                boxSize = boxSize,
                savedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)
            };

            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var snapshotPath = Path.Combine(projectRoot, "Temp", "big-red-button-trigger-surface-alignment.json");
            Directory.CreateDirectory(Path.GetDirectoryName(snapshotPath) ?? projectRoot);
            File.WriteAllText(snapshotPath, JsonUtility.ToJson(snapshot, true));
        }
#endif

        static string FormatVector(Vector3 value)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "({0:F6}, {1:F6}, {2:F6})",
                value.x,
                value.y,
                value.z);
        }

        static string FormatQuaternion(Quaternion value)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "({0:F6}, {1:F6}, {2:F6}, {3:F6})",
                value.x,
                value.y,
                value.z,
                value.w);
        }
    }
}
