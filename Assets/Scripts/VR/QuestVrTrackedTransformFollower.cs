using UnityEngine;

namespace TheBigRedButtonInstitute.VR
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BigRedButtonPressInteractor))]
    public sealed class QuestVrTrackedTransformFollower : MonoBehaviour
    {
        [SerializeField] Transform sourceTransform;
        [SerializeField] OVRInput.Hand handType = OVRInput.Hand.HandLeft;
        [SerializeField] bool requireControllerInHand = true;
        [SerializeField] Vector3 localPositionOffset;
        [SerializeField] Vector3 localRotationOffsetEuler;

        BigRedButtonPressInteractor _pressInteractor;

        void Awake()
        {
            _pressInteractor = GetComponent<BigRedButtonPressInteractor>();
        }

        void LateUpdate()
        {
            if (sourceTransform == null)
            {
                _pressInteractor?.SetTrackingValid(false);
                return;
            }

            if (requireControllerInHand &&
                OVRInput.GetControllerIsInHandState(handType) != OVRInput.ControllerInHandState.ControllerInHand)
            {
                _pressInteractor?.SetTrackingValid(false);
                return;
            }

            transform.SetPositionAndRotation(
                sourceTransform.TransformPoint(localPositionOffset),
                sourceTransform.rotation * Quaternion.Euler(localRotationOffsetEuler));
            _pressInteractor?.SetTrackingValid(true);
        }

        public void Configure(Transform targetSourceTransform, OVRInput.Hand targetHandType, bool targetRequireControllerInHand, Vector3 targetLocalPositionOffset, Vector3 targetLocalRotationOffsetEuler)
        {
            sourceTransform = targetSourceTransform;
            handType = targetHandType;
            requireControllerInHand = targetRequireControllerInHand;
            localPositionOffset = targetLocalPositionOffset;
            localRotationOffsetEuler = targetLocalRotationOffsetEuler;
        }
    }
}
