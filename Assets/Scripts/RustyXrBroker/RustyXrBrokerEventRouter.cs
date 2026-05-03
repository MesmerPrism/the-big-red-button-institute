using UnityEngine;

namespace TheBigRedButtonInstitute.RustyXrBroker
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-17)]
    public sealed class RustyXrBrokerEventRouter : MonoBehaviour
    {
        [SerializeField] RustyXrBrokerClient client;
        [SerializeField] bool autoResolveReferences = true;
        [SerializeField] RustyXrBrokerDriveSignalReceiver[] driveReceivers;

        int _routedEvents;
        int _appliedDriveEvents;

        public int RoutedEvents => _routedEvents;
        public int AppliedDriveEvents => _appliedDriveEvents;

        void Awake()
        {
            ResolveReferences(forceRefresh: true);
        }

        void OnEnable()
        {
            ResolveReferences(forceRefresh: false);
            if (client != null)
            {
                client.StreamEventReceived += HandleStreamEventReceived;
            }
        }

        void OnDisable()
        {
            if (client != null)
            {
                client.StreamEventReceived -= HandleStreamEventReceived;
            }
        }

        public void ConfigureReferences(RustyXrBrokerClient brokerClient, params RustyXrBrokerDriveSignalReceiver[] receivers)
        {
            client = brokerClient;
            driveReceivers = receivers;
        }

        public bool ApplyStreamEventJson(string json)
        {
            if (!RustyXrBrokerProtocol.TryParseStreamEvent(json, out var streamEvent))
            {
                return false;
            }

            return RouteStreamEvent(streamEvent);
        }

        public bool RouteStreamEvent(RustyXrBrokerStreamEvent streamEvent)
        {
            _routedEvents++;
            var applied = false;
            ResolveReferences(forceRefresh: false);

            if (driveReceivers != null)
            {
                for (var i = 0; i < driveReceivers.Length; i++)
                {
                    var receiver = driveReceivers[i];
                    if (receiver != null && receiver.ApplyStreamEvent(streamEvent))
                    {
                        applied = true;
                        _appliedDriveEvents++;
                    }
                }
            }

            return applied;
        }

        void HandleStreamEventReceived(RustyXrBrokerStreamEvent streamEvent)
        {
            RouteStreamEvent(streamEvent);
        }

        void ResolveReferences(bool forceRefresh)
        {
            if (!autoResolveReferences && !forceRefresh)
            {
                return;
            }

            if (client == null || forceRefresh)
            {
                client = GetComponent<RustyXrBrokerClient>() ?? FindAnyObjectByType<RustyXrBrokerClient>();
            }

            if (driveReceivers == null || driveReceivers.Length == 0 || forceRefresh)
            {
                driveReceivers = GetComponentsInChildren<RustyXrBrokerDriveSignalReceiver>(true);
            }
        }
    }
}
