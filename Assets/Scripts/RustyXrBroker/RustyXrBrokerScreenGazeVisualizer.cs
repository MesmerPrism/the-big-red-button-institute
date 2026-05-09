using UnityEngine;

namespace TheBigRedButtonInstitute.RustyXrBroker
{
    [DisallowMultipleComponent]
    public sealed class RustyXrBrokerScreenGazeVisualizer : MonoBehaviour
    {
        [SerializeField] RustyXrBrokerScreenGazeReceiver receiver;
        [SerializeField] Transform anchor;
        [SerializeField] Transform marker;
        [SerializeField] bool autoCreateMarker = true;
        [SerializeField] bool hideWhenInvalid;
        [SerializeField, Min(0.05f)] float distanceMeters = 0.85f;
        [SerializeField] Vector2 halfExtentsMeters = new(0.28f, 0.18f);
        [SerializeField, Min(0.005f)] float markerDiameterMeters = 0.025f;
        [SerializeField] Color markerColor = new(0.1f, 0.95f, 0.8f, 1f);

        public Transform Marker => marker;

        void Awake()
        {
            ResolveReferences(forceRefresh: true);
            EnsureMarker();
        }

        void OnEnable()
        {
            ResolveReferences(forceRefresh: false);
            EnsureMarker();
        }

        void Update()
        {
            ResolveReferences(forceRefresh: false);
            EnsureMarker();
            if (receiver == null || marker == null)
            {
                return;
            }

            var valid = receiver.SampleValid;
            if (hideWhenInvalid && !valid)
            {
                marker.gameObject.SetActive(false);
                return;
            }

            marker.gameObject.SetActive(true);
            var targetAnchor = anchor != null ? anchor : Camera.main != null ? Camera.main.transform : transform;
            var point = receiver.NormalizedPoint;
            var offsetX = (point.x - 0.5f) * 2f * Mathf.Max(0.01f, halfExtentsMeters.x);
            var offsetY = (0.5f - point.y) * 2f * Mathf.Max(0.01f, halfExtentsMeters.y);
            marker.position = targetAnchor.position +
                              targetAnchor.forward * distanceMeters +
                              targetAnchor.right * offsetX +
                              targetAnchor.up * offsetY;
            marker.rotation = Quaternion.LookRotation(targetAnchor.forward, targetAnchor.up);
            marker.localScale = Vector3.one * markerDiameterMeters;
        }

        public void ConfigureReferences(RustyXrBrokerScreenGazeReceiver gazeReceiver, Transform targetAnchor)
        {
            receiver = gazeReceiver;
            anchor = targetAnchor;
        }

        void ResolveReferences(bool forceRefresh)
        {
            if (receiver == null || forceRefresh)
            {
                receiver = GetComponent<RustyXrBrokerScreenGazeReceiver>() ??
                           FindAnyObjectByType<RustyXrBrokerScreenGazeReceiver>();
            }
        }

        void EnsureMarker()
        {
            if (marker != null || !autoCreateMarker)
            {
                return;
            }

            var markerObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            markerObject.name = "Broker Screen Gaze Marker";
            markerObject.transform.SetParent(transform, worldPositionStays: false);
            marker = markerObject.transform;

            var collider = markerObject.GetComponent<Collider>();
            if (collider != null)
            {
                Destroy(collider);
            }

            var renderer = markerObject.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.material.color = markerColor;
            }
        }
    }
}
