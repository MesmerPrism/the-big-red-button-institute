using System;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace TheBigRedButtonInstitute.RustyXrBroker
{
    public enum RustyXrBrokerConnectionState
    {
        Disconnected = 0,
        Connecting = 1,
        Connected = 2,
        WaitingToReconnect = 3
    }

    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-18)]
    public sealed class RustyXrBrokerClient : MonoBehaviour
    {
        [SerializeField] string websocketUri = "ws://127.0.0.1:8765/rustyxr/v1/events";
        [SerializeField] string clientId = "big-red-button-unity";
        [SerializeField] string appPackage = "org.thebigredbuttoninstitute.app";
        [SerializeField] string appLabel = "The Big Red Button Institute";
        [SerializeField] string appVersion = "0.1.0";
        [SerializeField] bool connectOnEnable = true;
        [SerializeField] bool subscribeOnConnect = true;
        [SerializeField] string[] defaultStreams = { RustyXrBrokerDriveSignal.DefaultStream };
        [SerializeField, Min(0.1f)] float reconnectDelaySeconds = 1f;
        [SerializeField, Min(0.1f)] float reconnectMaxDelaySeconds = 8f;
        [SerializeField, Min(1024)] int receiveBufferBytes = 8192;
        [SerializeField, Min(1)] int maxMessagesPerFrame = 16;

        readonly object _queueGate = new();
        readonly object _socketGate = new();
        readonly Queue<string> _incomingMessages = new();
        readonly SemaphoreSlim _sendGate = new(1, 1);

        CancellationTokenSource _loopCts;
        Task _loopTask;
        ClientWebSocket _socket;
        int _nextRequestId;
        long _sentMessages;
        long _receivedMessages;
        long _streamEvents;
        long _acceptedAcks;
        long _rejectedAcks;
        string _lastMessage = "";
        string _lastError = "";
        string _lastStatus = "disconnected";
        RustyXrBrokerConnectionState _state = RustyXrBrokerConnectionState.Disconnected;

        public event Action<string> BrokerMessageReceived;
        public event Action<RustyXrBrokerCommandAck> CommandAckReceived;
        public event Action<RustyXrBrokerStreamEvent> StreamEventReceived;

        public RustyXrBrokerConnectionState State => _state;
        public bool IsConnected => _state == RustyXrBrokerConnectionState.Connected;
        public long SentMessages => Interlocked.Read(ref _sentMessages);
        public long ReceivedMessages => Interlocked.Read(ref _receivedMessages);
        public long StreamEvents => Interlocked.Read(ref _streamEvents);
        public long AcceptedAcks => Interlocked.Read(ref _acceptedAcks);
        public long RejectedAcks => Interlocked.Read(ref _rejectedAcks);
        public string LastMessage => _lastMessage;
        public string LastError => _lastError;
        public string LastStatus => _lastStatus;

        void OnEnable()
        {
            if (connectOnEnable)
            {
                ConnectNow();
            }
        }

        void Update()
        {
            DrainIncomingMessages();
        }

        void OnDisable()
        {
            DisconnectNow();
        }

        void OnDestroy()
        {
            DisconnectNow();
            _sendGate.Dispose();
        }

        public void ConfigureIdentity(string packageName, string label, string version)
        {
            appPackage = string.IsNullOrWhiteSpace(packageName) ? appPackage : packageName;
            appLabel = string.IsNullOrWhiteSpace(label) ? appLabel : label;
            appVersion = string.IsNullOrWhiteSpace(version) ? appVersion : version;
        }

        public void ConfigureDefaultStreams(params string[] streams)
        {
            if (streams == null || streams.Length == 0)
            {
                defaultStreams = Array.Empty<string>();
                return;
            }

            var sanitized = new List<string>(streams.Length);
            for (var i = 0; i < streams.Length; i++)
            {
                var stream = streams[i];
                if (string.IsNullOrWhiteSpace(stream) || sanitized.Contains(stream))
                {
                    continue;
                }

                sanitized.Add(stream);
            }

            defaultStreams = sanitized.ToArray();
        }

        public void ConnectNow()
        {
            if (_loopTask != null && !_loopTask.IsCompleted)
            {
                return;
            }

            _loopCts = new CancellationTokenSource();
            _loopTask = Task.Run(() => RunConnectionLoopAsync(_loopCts.Token));
        }

        public void DisconnectNow()
        {
            var cts = _loopCts;
            _loopCts = null;
            if (cts != null)
            {
                cts.Cancel();
                cts.Dispose();
            }

            ClientWebSocket socket;
            lock (_socketGate)
            {
                socket = _socket;
                _socket = null;
            }

            if (socket != null)
            {
                try
                {
                    socket.Abort();
                    socket.Dispose();
                }
                catch (WebSocketException)
                {
                }
                catch (ObjectDisposedException)
                {
                }
            }

            SetState(RustyXrBrokerConnectionState.Disconnected, "disconnected");
        }

        public bool RequestStatus()
        {
            return SendCommand("status_request", null);
        }

        public bool RequestStreams()
        {
            return SendCommand("list_streams", null);
        }

        public bool OpenBrokerConsole()
        {
            return SendCommand("open_ui", null);
        }

        public bool CloseBrokerConsole()
        {
            return SendCommand("close_ui", null);
        }

        public int SubscribeToDefaultStreams()
        {
            var sent = 0;
            if (defaultStreams == null)
            {
                return 0;
            }

            for (var i = 0; i < defaultStreams.Length; i++)
            {
                if (Subscribe(defaultStreams[i]))
                {
                    sent++;
                }
            }

            return sent;
        }

        public bool Subscribe(string stream)
        {
            return SendCommand("subscribe", stream);
        }

        public bool Unsubscribe(string stream)
        {
            return SendCommand("unsubscribe", stream);
        }

        public string BuildStatusLabel()
        {
            var builder = new StringBuilder(160);
            builder.Append(_state);
            builder.Append(" / rx ");
            builder.Append(ReceivedMessages);
            builder.Append(" / events ");
            builder.Append(StreamEvents);
            builder.Append(" / acks ");
            builder.Append(AcceptedAcks);
            builder.Append("/");
            builder.Append(RejectedAcks);
            if (!string.IsNullOrWhiteSpace(_lastError))
            {
                builder.Append(" / ");
                builder.Append(_lastError);
            }

            return builder.ToString();
        }

        bool SendCommand(string command, string stream)
        {
            if (!IsConnected)
            {
                _lastError = "broker not connected";
                return false;
            }

            var requestId = NextRequestId(command);
            string json;
            switch (command)
            {
                case "status_request":
                    json = RustyXrBrokerProtocol.BuildStatusRequestCommandJson(requestId, clientId, appPackage, appLabel, appVersion);
                    break;
                case "list_streams":
                    json = RustyXrBrokerProtocol.BuildListStreamsCommandJson(requestId, clientId, appPackage, appLabel, appVersion);
                    break;
                case "list_capabilities":
                    json = RustyXrBrokerProtocol.BuildListCapabilitiesCommandJson(requestId, clientId, appPackage, appLabel, appVersion);
                    break;
                case "open_ui":
                    json = RustyXrBrokerProtocol.BuildOpenUiCommandJson(requestId, clientId, appPackage, appLabel, appVersion);
                    break;
                case "close_ui":
                    json = RustyXrBrokerProtocol.BuildCloseUiCommandJson(requestId, clientId, appPackage, appLabel, appVersion);
                    break;
                case "subscribe":
                    json = RustyXrBrokerProtocol.BuildSubscribeCommandJson(requestId, stream, clientId, appPackage, appLabel, appVersion);
                    break;
                case "unsubscribe":
                    json = RustyXrBrokerProtocol.BuildUnsubscribeCommandJson(requestId, stream, clientId, appPackage, appLabel, appVersion);
                    break;
                default:
                    json = RustyXrBrokerProtocol.BuildCommandJson(command, requestId, clientId, appPackage, appLabel, appVersion, stream);
                    break;
            }

            _ = SendAndReportAsync(json);
            return true;
        }

        async Task RunConnectionLoopAsync(CancellationToken token)
        {
            var delay = reconnectDelaySeconds;
            while (!token.IsCancellationRequested)
            {
                ClientWebSocket socket = null;
                try
                {
                    SetState(RustyXrBrokerConnectionState.Connecting, "connecting");
                    socket = new ClientWebSocket();
                    socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(15);
                    await socket.ConnectAsync(new Uri(websocketUri), token).ConfigureAwait(false);
                    lock (_socketGate)
                    {
                        _socket = socket;
                    }

                    SetState(RustyXrBrokerConnectionState.Connected, "connected");
                    delay = reconnectDelaySeconds;
                    await SendOnSocketAsync(socket, RustyXrBrokerProtocol.BuildHelloJson(clientId, appPackage, appLabel, appVersion), token).ConfigureAwait(false);
                    await SendOnSocketAsync(socket, RustyXrBrokerProtocol.BuildStatusRequestCommandJson(NextRequestId("status"), clientId, appPackage, appLabel, appVersion), token).ConfigureAwait(false);
                    if (subscribeOnConnect && defaultStreams != null)
                    {
                        for (var i = 0; i < defaultStreams.Length; i++)
                        {
                            var stream = defaultStreams[i];
                            if (!string.IsNullOrWhiteSpace(stream))
                            {
                                await SendOnSocketAsync(socket, RustyXrBrokerProtocol.BuildSubscribeCommandJson(NextRequestId("subscribe"), stream, clientId, appPackage, appLabel, appVersion), token).ConfigureAwait(false);
                            }
                        }
                    }

                    await ReceiveLoopAsync(socket, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex) when (ex is WebSocketException || ex is IOException || ex is InvalidOperationException || ex is UriFormatException)
                {
                    _lastError = ex.Message;
                }
                finally
                {
                    lock (_socketGate)
                    {
                        if (ReferenceEquals(_socket, socket))
                        {
                            _socket = null;
                        }
                    }

                    if (socket != null)
                    {
                        socket.Dispose();
                    }
                }

                if (token.IsCancellationRequested)
                {
                    break;
                }

                SetState(RustyXrBrokerConnectionState.WaitingToReconnect, "waiting to reconnect");
                await Task.Delay(TimeSpan.FromSeconds(delay), token).ConfigureAwait(false);
                delay = Mathf.Min(reconnectMaxDelaySeconds, delay * 2f);
            }

            SetState(RustyXrBrokerConnectionState.Disconnected, "disconnected");
        }

        async Task ReceiveLoopAsync(ClientWebSocket socket, CancellationToken token)
        {
            var buffer = new byte[Mathf.Max(1024, receiveBufferBytes)];
            while (!token.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                using (var message = new MemoryStream())
                {
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), token).ConfigureAwait(false);
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            return;
                        }

                        if (result.Count > 0)
                        {
                            message.Write(buffer, 0, result.Count);
                        }
                    }
                    while (!result.EndOfMessage);

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        EnqueueIncoming(Encoding.UTF8.GetString(message.ToArray()));
                    }
                }
            }
        }

        async Task SendAndReportAsync(string json)
        {
            try
            {
                await SendRawAsync(json, _loopCts != null ? _loopCts.Token : CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is WebSocketException || ex is IOException || ex is InvalidOperationException || ex is ObjectDisposedException)
            {
                _lastError = ex.Message;
            }
        }

        async Task SendRawAsync(string json, CancellationToken token)
        {
            ClientWebSocket socket;
            lock (_socketGate)
            {
                socket = _socket;
            }

            if (socket == null || socket.State != WebSocketState.Open)
            {
                throw new InvalidOperationException("Broker WebSocket is not open.");
            }

            await SendOnSocketAsync(socket, json, token).ConfigureAwait(false);
        }

        async Task SendOnSocketAsync(ClientWebSocket socket, string json, CancellationToken token)
        {
            if (string.IsNullOrEmpty(json))
            {
                return;
            }

            await _sendGate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                var bytes = Encoding.UTF8.GetBytes(json);
                await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, token).ConfigureAwait(false);
                Interlocked.Increment(ref _sentMessages);
            }
            finally
            {
                _sendGate.Release();
            }
        }

        void DrainIncomingMessages()
        {
            for (var i = 0; i < maxMessagesPerFrame; i++)
            {
                string message;
                lock (_queueGate)
                {
                    if (_incomingMessages.Count == 0)
                    {
                        return;
                    }

                    message = _incomingMessages.Dequeue();
                }

                _lastMessage = message;
                BrokerMessageReceived?.Invoke(message);
                if (RustyXrBrokerProtocol.TryParseCommandAck(message, out var ack))
                {
                    if (ack.accepted)
                    {
                        Interlocked.Increment(ref _acceptedAcks);
                    }
                    else
                    {
                        Interlocked.Increment(ref _rejectedAcks);
                    }

                    Debug.Log(
                        $"[RustyXrBrokerClient] command_ack command={ack.command} accepted={ack.accepted} request_id={ack.request_id}",
                        this);
                    CommandAckReceived?.Invoke(ack);
                }

                if (RustyXrBrokerProtocol.TryParseStreamEvent(message, out var streamEvent))
                {
                    Interlocked.Increment(ref _streamEvents);
                    var payloadValue01 = streamEvent.payload != null ? streamEvent.payload.value01 : 0f;
                    Debug.Log(
                        $"[RustyXrBrokerClient] stream_event stream={streamEvent.stream} sequence={streamEvent.sequence_id} value01={payloadValue01:0.000}",
                        this);
                    StreamEventReceived?.Invoke(streamEvent);
                }
            }
        }

        void EnqueueIncoming(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            Interlocked.Increment(ref _receivedMessages);
            lock (_queueGate)
            {
                _incomingMessages.Enqueue(message);
            }
        }

        string NextRequestId(string prefix)
        {
            var id = Interlocked.Increment(ref _nextRequestId);
            return $"{clientId}-{prefix}-{id}";
        }

        void SetState(RustyXrBrokerConnectionState state, string status)
        {
            _state = state;
            _lastStatus = status;
        }
    }
}
