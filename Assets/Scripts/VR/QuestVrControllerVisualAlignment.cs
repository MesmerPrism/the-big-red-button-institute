using UnityEngine;

namespace TheBigRedButtonInstitute.VR
{
    [DisallowMultipleComponent]
    public sealed class QuestVrControllerVisualAlignment : MonoBehaviour
    {
        [SerializeField] Transform controllerAnchor;
        [SerializeField] OVRHand hand;
        [SerializeField] OVRSkeleton skeleton;
        [SerializeField] OVRInput.Hand handType = OVRInput.Hand.HandLeft;
        [SerializeField] Vector3 inHandPositionOffset = new(0f, -0.015f, 0.01f);
        [SerializeField] Vector3 inHandRotationOffsetEuler;

        Vector3 _defaultLocalPosition;
        Quaternion _defaultLocalRotation;
        bool _defaultPoseCaptured;
        Transform _palmTransform;

        void Awake()
        {
            CaptureDefaultPose();
            ResolveReferences(forceRefresh: true);
        }

        void OnEnable()
        {
            CaptureDefaultPose();
            ResolveReferences(forceRefresh: false);
        }

        void LateUpdate()
        {
            ResolveReferences(forceRefresh: false);
            if (!TryGetInHandPose(out var worldPosition, out var worldRotation))
            {
                RestoreDefaultPose();
                return;
            }

            transform.SetPositionAndRotation(worldPosition, worldRotation);
        }

        public void Configure(
            Transform targetControllerAnchor,
            OVRHand targetHand,
            OVRSkeleton targetSkeleton,
            OVRInput.Hand targetHandType,
            Vector3 targetInHandPositionOffset,
            Vector3 targetInHandRotationOffsetEuler)
        {
            controllerAnchor = targetControllerAnchor;
            hand = targetHand;
            skeleton = targetSkeleton;
            handType = targetHandType;
            inHandPositionOffset = targetInHandPositionOffset;
            inHandRotationOffsetEuler = targetInHandRotationOffsetEuler;
            CaptureDefaultPose();
        }

        void CaptureDefaultPose()
        {
            if (_defaultPoseCaptured)
            {
                return;
            }

            _defaultLocalPosition = transform.localPosition;
            _defaultLocalRotation = transform.localRotation;
            _defaultPoseCaptured = true;
        }

        void RestoreDefaultPose()
        {
            if (!_defaultPoseCaptured)
            {
                return;
            }

            transform.localPosition = _defaultLocalPosition;
            transform.localRotation = _defaultLocalRotation;
        }

        bool TryGetInHandPose(out Vector3 worldPosition, out Quaternion worldRotation)
        {
            worldPosition = transform.position;
            worldRotation = transform.rotation;

            if (controllerAnchor == null)
            {
                return false;
            }

            if (OVRInput.GetControllerIsInHandState(handType) != OVRInput.ControllerInHandState.ControllerInHand)
            {
                return false;
            }

            var palm = ResolvePalmTransform();
            if (palm == null)
            {
                return false;
            }

            worldRotation = controllerAnchor.rotation * Quaternion.Euler(inHandRotationOffsetEuler);
            worldPosition = palm.position + worldRotation * inHandPositionOffset;
            return true;
        }

        Transform ResolvePalmTransform()
        {
            if (_palmTransform != null)
            {
                return _palmTransform;
            }

            if (skeleton == null || !skeleton.IsInitialized || skeleton.Bones == null)
            {
                return hand != null && hand.IsDataValid ? hand.transform : null;
            }

            _palmTransform = FindBoneTransform(
                skeleton,
                OVRSkeleton.BoneId.XRHand_Palm,
                OVRSkeleton.BoneId.Hand_WristRoot,
                OVRSkeleton.BoneId.XRHand_Wrist);

            return _palmTransform != null
                ? _palmTransform
                : hand != null && hand.IsDataValid ? hand.transform : null;
        }

        static Transform FindBoneTransform(OVRSkeleton sourceSkeleton, params OVRSkeleton.BoneId[] boneIds)
        {
            if (sourceSkeleton == null || sourceSkeleton.Bones == null)
            {
                return null;
            }

            for (var i = 0; i < boneIds.Length; i++)
            {
                var boneId = boneIds[i];
                for (var boneIndex = 0; boneIndex < sourceSkeleton.Bones.Count; boneIndex++)
                {
                    var bone = sourceSkeleton.Bones[boneIndex];
                    if (bone != null && bone.Id == boneId && bone.Transform != null)
                    {
                        return bone.Transform;
                    }
                }
            }

            return null;
        }

        void ResolveReferences(bool forceRefresh)
        {
            if ((controllerAnchor == null || forceRefresh) && transform.parent != null)
            {
                controllerAnchor = transform.parent;
            }

            if ((hand == null || forceRefresh) && controllerAnchor != null)
            {
                hand = controllerAnchor.root.GetComponentInChildren<OVRHand>(true);
            }

            if ((skeleton == null || forceRefresh) && hand != null)
            {
                skeleton = hand.GetComponent<OVRSkeleton>();
            }

            if (forceRefresh)
            {
                _palmTransform = null;
            }
        }
    }
}
