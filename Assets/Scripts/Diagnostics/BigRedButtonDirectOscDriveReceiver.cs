using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using TheBigRedButtonInstitute.RustyXrBroker;
using TheBigRedButtonInstitute.VR;
using UnityEngine;

namespace TheBigRedButtonInstitute.Diagnostics
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-17)]
    public sealed class BigRedButtonDirectOscDriveReceiver : MonoBehaviour
    {
        readonly ConcurrentQueue<BigRedButtonOscDriveMessage> _pendingMessages = new();
        readonly object _sendLock = new();

        [SerializeField] QuestVrInputManager inputManager;
        [SerializeField] BigRedButtonDiagnosticComparisonController comparisonController;
        [SerializeField] bool autoResolveReferences = true;
        [SerializeField] bool startOnEnable = true;
        [SerializeField, Min(1)] int listenPort = 9001;
        [SerializeField] string acceptedAddress = RustyXrBrokerDriveSignal.DefaultAddress;
        [SerializeField] bool sendAcknowledgements = true;
        [SerializeField] string acknowledgementAddress = "/rusty-xr/drive/ack";
        [SerializeField, Range(0f, 1f)] float triggerThreshold01 = 0.5f;
        [SerializeField, Min(0f)] float minimumTriggerIntervalSeconds = 0.25f;
        [SerializeField] bool triggerOnRisingEdgeOnly = true;

        UdpClient _udpClient;
        Thread _receiveThread;
        volatile bool _running;
        float _previousValue01;
        double _lastTriggerTime = -1d;
        long _localSequence;
        long _receivedPackets;
        long _rejectedPackets;
        string _lastState = "idle";
        string _lastError = string.Empty;

        public int ListenPort => listenPort;
        public string AcceptedAddress => string.IsNullOrWhiteSpace(acceptedAddress) ? RustyXrBrokerDriveSignal.DefaultAddress : acceptedAddress;
        public long ReceivedPackets => _receivedPackets;
        public long RejectedPackets => _rejectedPackets;
        public string LastState => string.IsNullOrWhiteSpace(_lastState) ? "idle" : _lastState;
        public string LastError => _lastError ?? string.Empty;

        void Awake()
        {
            ResolveReferences(forceRefresh: true);
        }

        void OnEnable()
        {
            ResolveReferences(forceRefresh: false);
            if (startOnEnable)
            {
                StartReceiver();
            }
        }

        void Update()
        {
            ResolveReferences(forceRefresh: false);
            DrainMessages();
        }

        void OnDisable()
        {
            StopReceiver();
        }

        void OnApplicationQuit()
        {
            StopReceiver();
        }

        public void ConfigureReferences(
            QuestVrInputManager manager,
            BigRedButtonDiagnosticComparisonController controller)
        {
            inputManager = manager;
            comparisonController = controller;
        }

        public void StartReceiver()
        {
            if (_running)
            {
                return;
            }

            try
            {
                _udpClient = new UdpClient();
                _udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, listenPort));
                _running = true;
                _receiveThread = new Thread(ReceiveLoop)
                {
                    IsBackground = true,
                    Name = "BRB Direct OSC Receiver"
                };
                _receiveThread.Start();
                _lastState = $"listening udp:{listenPort}";
                _lastError = string.Empty;
            }
            catch (Exception ex) when (ex is SocketException || ex is ObjectDisposedException)
            {
                _running = false;
                _lastState = "listen failed";
                _lastError = ex.Message;
                Debug.LogWarning($"[BigRedButtonDirectOscDriveReceiver] OSC listen failed on udp:{listenPort}: {ex.Message}", this);
            }
        }

        public void StopReceiver()
        {
            _running = false;
            if (_udpClient != null)
            {
                _udpClient.Close();
                _udpClient = null;
            }

            _receiveThread = null;
            _lastState = "stopped";
        }

        void ReceiveLoop()
        {
            while (_running)
            {
                try
                {
                    var endpoint = new IPEndPoint(IPAddress.Any, 0);
                    var client = _udpClient;
                    if (client == null)
                    {
                        return;
                    }

                    var data = client.Receive(ref endpoint);
                    var receivedTimeUnixNs = BigRedButtonDiagnosticComparisonController.UnixTimeNanoseconds(DateTimeOffset.UtcNow);
                    if (BigRedButtonOscDriveMessageParser.TryDecodeDriveMessage(
                            data,
                            data.Length,
                            AcceptedAddress,
                            endpoint.ToString(),
                            endpoint.Address.ToString(),
                            endpoint.Port,
                            receivedTimeUnixNs,
                            out var message,
                            out var error))
                    {
                        _pendingMessages.Enqueue(message);
                        Interlocked.Increment(ref _receivedPackets);
                    }
                    else
                    {
                        _lastError = error;
                        Interlocked.Increment(ref _rejectedPackets);
                    }
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                catch (SocketException ex)
                {
                    if (!_running)
                    {
                        return;
                    }

                    _lastError = ex.Message;
                    Interlocked.Increment(ref _rejectedPackets);
                }
            }
        }

        void DrainMessages()
        {
            while (_pendingMessages.TryDequeue(out var message))
            {
                ApplyMessage(message);
            }
        }

        void ApplyMessage(BigRedButtonOscDriveMessage message)
        {
            var value = Mathf.Clamp01(message.Value01);
            var sequence = message.SequenceId > 0 ? message.SequenceId : ++_localSequence;
            var nowSeconds = Time.unscaledTimeAsDouble;
            var shouldTrigger = RustyXrBrokerButtonDriver.ShouldTrigger(
                _previousValue01,
                value,
                triggerThreshold01,
                triggerOnRisingEdgeOnly);
            _previousValue01 = value;

            var acceptedPulse = false;
            if (shouldTrigger &&
                (_lastTriggerTime < 0d || nowSeconds - _lastTriggerTime >= minimumTriggerIntervalSeconds))
            {
                acceptedPulse = inputManager != null && inputManager.TriggerButtonPressFromRuntime();
                if (acceptedPulse)
                {
                    _lastTriggerTime = nowSeconds;
                }
            }

            comparisonController?.RecordRouteSample(
                BigRedButtonDiagnosticRouteId.DirectUnityOsc,
                new BigRedButtonDiagnosticSample(
                    sequence,
                    value,
                    message.ClientSendTimeUnixNs,
                    0L,
                    message.ReceivedTimeUnixNs > 0L
                        ? message.ReceivedTimeUnixNs
                        : BigRedButtonDiagnosticComparisonController.UnixTimeNanoseconds(DateTimeOffset.UtcNow),
                    message.Peer),
                acceptedPulse);

            SendAcknowledgement(message, sequence, value, acceptedPulse);
            _lastState = acceptedPulse ? $"pulse {value:0.00}" : $"armed {value:0.00}";
            if (acceptedPulse)
            {
                Debug.Log($"[BigRedButtonDirectOscDriveReceiver] direct OSC pulse sequence={sequence} value01={value:0.000} peer={message.Peer}", this);
            }
        }

        void SendAcknowledgement(
            BigRedButtonOscDriveMessage message,
            long sequence,
            float value01,
            bool acceptedPulse)
        {
            if (!sendAcknowledgements ||
                string.IsNullOrWhiteSpace(message.PeerHost) ||
                message.ReplyPort <= 0)
            {
                return;
            }

            try
            {
                var receiveTimeUnixNs = message.ReceivedTimeUnixNs > 0L
                    ? message.ReceivedTimeUnixNs
                    : BigRedButtonDiagnosticComparisonController.UnixTimeNanoseconds(DateTimeOffset.UtcNow);
                var ackSendTimeUnixNs = BigRedButtonDiagnosticComparisonController.UnixTimeNanoseconds(DateTimeOffset.UtcNow);
                var payload = BigRedButtonOscDriveMessageParser.EncodeDriveAcknowledgement(
                    acknowledgementAddress,
                    sequence,
                    value01,
                    message.ClientSendTimeUnixNs,
                    receiveTimeUnixNs,
                    ackSendTimeUnixNs,
                    acceptedPulse);

                var client = _udpClient;
                if (client == null)
                {
                    return;
                }

                lock (_sendLock)
                {
                    client.Send(payload, payload.Length, message.PeerHost, message.ReplyPort);
                }
            }
            catch (Exception ex) when (ex is SocketException || ex is ObjectDisposedException || ex is ArgumentException)
            {
                _lastError = $"ack failed: {ex.Message}";
                Debug.LogWarning($"[BigRedButtonDirectOscDriveReceiver] OSC ack failed for {message.PeerHost}:{message.ReplyPort}: {ex.Message}", this);
            }
        }

        void ResolveReferences(bool forceRefresh)
        {
            if (!autoResolveReferences && !forceRefresh)
            {
                return;
            }

            if (inputManager == null || forceRefresh)
            {
                inputManager = GetComponent<QuestVrInputManager>() ?? FindAnyObjectByType<QuestVrInputManager>();
            }

            if (comparisonController == null || forceRefresh)
            {
                comparisonController = GetComponent<BigRedButtonDiagnosticComparisonController>() ?? FindAnyObjectByType<BigRedButtonDiagnosticComparisonController>();
            }
        }
    }
}
