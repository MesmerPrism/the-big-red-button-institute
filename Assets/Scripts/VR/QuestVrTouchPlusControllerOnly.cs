using UnityEngine;

namespace TheBigRedButtonInstitute.VR
{
    [DisallowMultipleComponent]
    [ExecuteAlways]
    public sealed class QuestVrTouchPlusControllerOnly : MonoBehaviour
    {
        [SerializeField] bool autoResolveFromParentAnchor = true;
        [SerializeField] string controllerVisualName = string.Empty;
        [SerializeField] string touchPlusModelRootName = string.Empty;

        OVRControllerHelper _controllerHelper;
        GameObject _touchPlusModelRoot;

        public void Configure(string targetControllerVisualName, string targetTouchPlusModelRootName)
        {
            controllerVisualName = targetControllerVisualName ?? string.Empty;
            touchPlusModelRootName = targetTouchPlusModelRootName ?? string.Empty;
            ResolveReferences(forceRefresh: true);
            ApplyTouchPlusOnly();
        }

        void Awake()
        {
            ResolveReferences(forceRefresh: true);
            ApplyTouchPlusOnly();
        }

        void OnEnable()
        {
            ResolveReferences(forceRefresh: true);
            ApplyTouchPlusOnly();
        }

        void OnValidate()
        {
            ResolveReferences(forceRefresh: true);
            ApplyTouchPlusOnly();
        }

        void LateUpdate()
        {
            ResolveReferences(forceRefresh: false);
            ApplyTouchPlusOnly();
        }

        void ResolveReferences(bool forceRefresh)
        {
            if ((_controllerHelper == null || forceRefresh) && autoResolveFromParentAnchor)
            {
                var anchor = transform.parent;
                if (anchor != null)
                {
                    Transform controllerVisual = null;
                    if (!string.IsNullOrWhiteSpace(controllerVisualName))
                    {
                        controllerVisual = anchor.Find(controllerVisualName);
                    }

                    controllerVisual ??= anchor.GetComponentInChildren<OVRControllerHelper>(true)?.transform;
                    _controllerHelper = controllerVisual != null ? controllerVisual.GetComponent<OVRControllerHelper>() : null;
                }
            }

            if ((_touchPlusModelRoot == null || forceRefresh) && _controllerHelper != null)
            {
                if (!string.IsNullOrWhiteSpace(touchPlusModelRootName))
                {
                    _touchPlusModelRoot = _controllerHelper.transform.Find(touchPlusModelRootName)?.gameObject;
                }

                _touchPlusModelRoot ??= _controllerHelper.m_controller == OVRInput.Controller.LTouch
                    ? _controllerHelper.m_modelMetaTouchPlusLeftController
                    : _controllerHelper.m_modelMetaTouchPlusRightController;
            }
        }

        void ApplyTouchPlusOnly()
        {
            if (_controllerHelper == null || _touchPlusModelRoot == null)
            {
                return;
            }

            // Force the helper to animate and toggle only the Quest 3 Touch Plus controller model.
            _controllerHelper.m_modelOculusTouchQuestAndRiftSLeftController = _touchPlusModelRoot;
            _controllerHelper.m_modelOculusTouchQuestAndRiftSRightController = _touchPlusModelRoot;
            _controllerHelper.m_modelOculusTouchRiftLeftController = _touchPlusModelRoot;
            _controllerHelper.m_modelOculusTouchRiftRightController = _touchPlusModelRoot;
            _controllerHelper.m_modelOculusTouchQuest2LeftController = _touchPlusModelRoot;
            _controllerHelper.m_modelOculusTouchQuest2RightController = _touchPlusModelRoot;
            _controllerHelper.m_modelMetaTouchProLeftController = _touchPlusModelRoot;
            _controllerHelper.m_modelMetaTouchProRightController = _touchPlusModelRoot;
            _controllerHelper.m_modelMetaTouchPlusLeftController = _touchPlusModelRoot;
            _controllerHelper.m_modelMetaTouchPlusRightController = _touchPlusModelRoot;

            var controllerVisualRoot = _controllerHelper.transform;
            for (var childIndex = 0; childIndex < controllerVisualRoot.childCount; childIndex++)
            {
                var child = controllerVisualRoot.GetChild(childIndex);
                if (child == null)
                {
                    continue;
                }

                if (child.gameObject != _touchPlusModelRoot)
                {
                    child.gameObject.SetActive(false);
                }
            }
        }
    }
}
