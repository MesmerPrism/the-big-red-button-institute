using UnityEngine;

namespace TheBigRedButtonInstitute.VR
{
    [DisallowMultipleComponent]
    public sealed class QuestVrHandOnControllerAlignment : MonoBehaviour
    {
        [SerializeField] Transform controllerAnchor;
        [SerializeField] OVRInput.Hand handType = OVRInput.Hand.HandLeft;
        [SerializeField] Vector3 inControllerPositionOffset = new(0f, -0.035f, -0.015f);
        [SerializeField] Vector3 inControllerRotationOffsetEuler;

        Vector3 _defaultLocalPosition;
        Quaternion _defaultLocalRotation;
        bool _defaultPoseCaptured;

        void Awake()
        {
            CaptureDefaultPose();
        }

        void OnEnable()
        {
            CaptureDefaultPose();
        }

        void LateUpdate()
        {
            if (!TryGetAlignedPose(out var worldPosition, out var worldRotation))
            {
                RestoreDefaultPose();
                return;
            }

            transform.SetPositionAndRotation(worldPosition, worldRotation);
        }

        public void Configure(Transform targetControllerAnchor, OVRInput.Hand targetHandType, Vector3 targetInControllerPositionOffset, Vector3 targetInControllerRotationOffsetEuler)
        {
            controllerAnchor = targetControllerAnchor;
            handType = targetHandType;
            inControllerPositionOffset = targetInControllerPositionOffset;
            inControllerRotationOffsetEuler = targetInControllerRotationOffsetEuler;
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

        bool TryGetAlignedPose(out Vector3 worldPosition, out Quaternion worldRotation)
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

            if (!OVRInput.AreHandPosesGeneratedByControllerData(OVRPlugin.Step.Render, handType))
            {
                return false;
            }

            worldRotation = controllerAnchor.rotation * Quaternion.Euler(inControllerRotationOffsetEuler);
            worldPosition = controllerAnchor.position + worldRotation * inControllerPositionOffset;
            return true;
        }
    }
}
