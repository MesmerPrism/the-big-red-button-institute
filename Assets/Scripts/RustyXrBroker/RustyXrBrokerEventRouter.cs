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
        [SerializeField] RustyXrBrokerScreenGazeReceiver[] screenGazeReceivers;
        [SerializeField] RustyXrBrokerBioSignalReceiver[] bioSignalReceivers;

        int _routedEvents;
        int _appliedDriveEvents;
        int _appliedScreenGazeEvents;
        int _appliedBioSignalEvents;

        public int RoutedEvents => _routedEvents;
        public int AppliedDriveEvents => _appliedDriveEvents;
        public int AppliedScreenGazeEvents => _appliedScreenGazeEvents;
        public int AppliedBioSignalEvents => _appliedBioSignalEvents;

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

        public void ConfigureScreenGazeReferences(params RustyXrBrokerScreenGazeReceiver[] receivers)
        {
            screenGazeReceivers = receivers;
        }

        public void ConfigureBioSignalReferences(params RustyXrBrokerBioSignalReceiver[] receivers)
        {
            bioSignalReceivers = receivers;
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

            if (screenGazeReceivers != null)
            {
                for (var i = 0; i < screenGazeReceivers.Length; i++)
                {
                    var receiver = screenGazeReceivers[i];
                    if (receiver != null && receiver.ApplyStreamEvent(streamEvent))
                    {
                        applied = true;
                        _appliedScreenGazeEvents++;
                    }
                }
            }

            if (bioSignalReceivers != null)
            {
                for (var i = 0; i < bioSignalReceivers.Length; i++)
                {
                    var receiver = bioSignalReceivers[i];
                    if (receiver != null && receiver.ApplyStreamEvent(streamEvent))
                    {
                        applied = true;
                        _appliedBioSignalEvents++;
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

            if (screenGazeReceivers == null || screenGazeReceivers.Length == 0 || forceRefresh)
            {
                screenGazeReceivers = GetComponentsInChildren<RustyXrBrokerScreenGazeReceiver>(true);
            }

            if (bioSignalReceivers == null || bioSignalReceivers.Length == 0 || forceRefresh)
            {
                bioSignalReceivers = GetComponentsInChildren<RustyXrBrokerBioSignalReceiver>(true);
            }
        }
    }
}
