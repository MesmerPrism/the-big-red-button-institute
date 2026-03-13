using UnityEngine;

namespace TheBigRedButtonInstitute.VR
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BigRedButtonPressInteractor))]
    public sealed class QuestVrHandTipFollower : MonoBehaviour
    {
        [SerializeField] OVRHand hand;
        [SerializeField] OVRSkeleton skeleton;
        [SerializeField] Vector3 fallbackLocalOffset = new(0f, 0f, 0.08f);

        BigRedButtonPressInteractor _pressInteractor;
        Transform _tipTransform;

        void Awake()
        {
            _pressInteractor = GetComponent<BigRedButtonPressInteractor>();
            ResolveReferences(forceRefresh: true);
        }

        void OnEnable()
        {
            ResolveReferences(forceRefresh: false);
        }

        void LateUpdate()
        {
            ResolveReferences(forceRefresh: false);
            if (!TryResolveTipPose(out var worldPosition, out var worldRotation))
            {
                _pressInteractor?.SetTrackingValid(false);
                return;
            }

            transform.SetPositionAndRotation(worldPosition, worldRotation);
            _pressInteractor?.SetTrackingValid(true);
        }

        public void Configure(OVRHand targetHand, OVRSkeleton targetSkeleton, Vector3 targetFallbackLocalOffset)
        {
            hand = targetHand;
            skeleton = targetSkeleton;
            fallbackLocalOffset = targetFallbackLocalOffset;
            _tipTransform = null;
        }

        bool TryResolveTipPose(out Vector3 worldPosition, out Quaternion worldRotation)
        {
            worldPosition = transform.position;
            worldRotation = transform.rotation;

            if (hand == null || !hand.IsDataValid)
            {
                return false;
            }

            var tip = ResolveTipTransform();
            if (tip != null)
            {
                worldPosition = tip.position;
                worldRotation = tip.rotation;
                return true;
            }

            if (skeleton != null)
            {
                return false;
            }

            worldPosition = hand.transform.TransformPoint(fallbackLocalOffset);
            worldRotation = hand.transform.rotation;
            return true;
        }

        Transform ResolveTipTransform()
        {
            if (_tipTransform != null)
            {
                return _tipTransform;
            }

            if (skeleton == null || !skeleton.IsInitialized || skeleton.Bones == null)
            {
                return null;
            }

            _tipTransform = FindBoneTransform(
                skeleton,
                OVRSkeleton.BoneId.XRHand_IndexTip,
                OVRSkeleton.BoneId.Hand_IndexTip);

            return _tipTransform;
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
            if ((hand == null || forceRefresh) && transform.parent != null)
            {
                hand = transform.parent.GetComponentInChildren<OVRHand>(true);
            }

            if ((skeleton == null || forceRefresh) && hand != null)
            {
                skeleton = hand.GetComponent<OVRSkeleton>();
            }

            if (forceRefresh)
            {
                _tipTransform = null;
            }
        }
    }
}
