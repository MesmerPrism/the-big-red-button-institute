using System;
using UnityEngine;

namespace TheBigRedButtonInstitute.RustyXrBroker
{
    public static class RustyXrBrokerProtocol
    {
        public const string ContractVersion = "rusty.xr.broker.v1";
        public const string HelloSchema = "rusty.xr.broker.hello.v1";
        public const string CommandSchema = "rusty.xr.broker.command.v1";
        public const string CommandAckSchema = "rusty.xr.broker.command_ack.v1";
        public const string StreamEventSchema = "rusty.xr.broker.stream_event.v1";

        public static string BuildHelloJson(
            string clientId,
            string appPackage,
            string appLabel,
            string appVersion)
        {
            var hello = new RustyXrBrokerHelloEnvelope
            {
                type = "hello",
                schema = HelloSchema,
                client_id = clientId,
                app_package = appPackage,
                app_label = appLabel,
                app_version = appVersion,
                protocol_min = ContractVersion,
                protocol_max = ContractVersion,
                supports_commands = true
            };

            return JsonUtility.ToJson(hello);
        }

        public static string BuildStatusRequestCommandJson(
            string requestId,
            string clientId,
            string appPackage,
            string appLabel,
            string appVersion) =>
            BuildCommandJson("status_request", requestId, clientId, appPackage, appLabel, appVersion, null);

        public static string BuildSubscribeCommandJson(
            string requestId,
            string stream,
            string clientId,
            string appPackage,
            string appLabel,
            string appVersion) =>
            BuildCommandJson("subscribe", requestId, clientId, appPackage, appLabel, appVersion, stream);

        public static string BuildUnsubscribeCommandJson(
            string requestId,
            string stream,
            string clientId,
            string appPackage,
            string appLabel,
            string appVersion) =>
            BuildCommandJson("unsubscribe", requestId, clientId, appPackage, appLabel, appVersion, stream);

        public static string BuildListStreamsCommandJson(
            string requestId,
            string clientId,
            string appPackage,
            string appLabel,
            string appVersion) =>
            BuildCommandJson("list_streams", requestId, clientId, appPackage, appLabel, appVersion, null);

        public static string BuildListCapabilitiesCommandJson(
            string requestId,
            string clientId,
            string appPackage,
            string appLabel,
            string appVersion) =>
            BuildCommandJson("list_capabilities", requestId, clientId, appPackage, appLabel, appVersion, null);

        public static string BuildOpenUiCommandJson(
            string requestId,
            string clientId,
            string appPackage,
            string appLabel,
            string appVersion) =>
            BuildCommandJson("open_ui", requestId, clientId, appPackage, appLabel, appVersion, null);

        public static string BuildCloseUiCommandJson(
            string requestId,
            string clientId,
            string appPackage,
            string appLabel,
            string appVersion) =>
            BuildCommandJson("close_ui", requestId, clientId, appPackage, appLabel, appVersion, null);

        public static string BuildCommandJson(
            string command,
            string requestId,
            string clientId,
            string appPackage,
            string appLabel,
            string appVersion,
            string stream)
        {
            var envelope = new RustyXrBrokerCommandEnvelope
            {
                type = "command",
                schema = CommandSchema,
                request_id = string.IsNullOrWhiteSpace(requestId) ? Guid.NewGuid().ToString("N") : requestId,
                command = command,
                client_id = clientId,
                app_package = appPackage,
                app_label = appLabel,
                app_version = appVersion,
                @params = string.IsNullOrWhiteSpace(stream)
                    ? null
                    : new RustyXrBrokerCommandParams
                    {
                        stream = stream
                    }
            };

            return JsonUtility.ToJson(envelope);
        }

        public static bool TryParseCommandAck(string json, out RustyXrBrokerCommandAck ack)
        {
            ack = null;
            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            try
            {
                var parsed = JsonUtility.FromJson<RustyXrBrokerCommandAck>(json);
                if (parsed == null ||
                    parsed.type != "command_ack" ||
                    parsed.schema != CommandAckSchema ||
                    string.IsNullOrWhiteSpace(parsed.request_id))
                {
                    return false;
                }

                ack = parsed;
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        public static bool TryParseStreamEvent(string json, out RustyXrBrokerStreamEvent streamEvent)
        {
            streamEvent = null;
            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            try
            {
                var parsed = JsonUtility.FromJson<RustyXrBrokerStreamEvent>(json);
                if (parsed == null ||
                    parsed.type != "stream_event" ||
                    parsed.schema != StreamEventSchema ||
                    string.IsNullOrWhiteSpace(parsed.stream))
                {
                    return false;
                }

                streamEvent = parsed;
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }
    }

    [Serializable]
    public sealed class RustyXrBrokerHelloEnvelope
    {
        public string type;
        public string schema;
        public string client_id;
        public string app_package;
        public string app_label;
        public string app_version;
        public string protocol_min;
        public string protocol_max;
        public bool supports_commands;
    }

    [Serializable]
    public sealed class RustyXrBrokerCommandEnvelope
    {
        public string type;
        public string schema;
        public string request_id;
        public string command;
        public string client_id;
        public string app_package;
        public string app_label;
        public string app_version;
        public RustyXrBrokerCommandParams @params;
    }

    [Serializable]
    public sealed class RustyXrBrokerCommandParams
    {
        public string stream;
    }

    [Serializable]
    public sealed class RustyXrBrokerCommandAck
    {
        public string type;
        public string schema;
        public string request_id;
        public string command;
        public bool accepted;
        public string message;
        public RustyXrBrokerCommandAckResult result;
        public RustyXrBrokerCommandError error;
    }

    [Serializable]
    public sealed class RustyXrBrokerCommandAckResult
    {
        public string stream;
        public string subscription_id;
    }

    [Serializable]
    public sealed class RustyXrBrokerCommandError
    {
        public string code;
        public string message;
    }

    [Serializable]
    public sealed class RustyXrBrokerStreamEvent
    {
        public string type;
        public string schema;
        public string stream;
        public string subscription_id;
        public long sequence_id;
        public long broker_time_unix_ns;
        public long broker_time_elapsed_ns;
        public RustyXrBrokerStreamPayload payload;
    }

    [Serializable]
    public sealed class RustyXrBrokerStreamPayload
    {
        public string address;
        public float value01;
        public string peer;
        public string argument_type;
        public string path;
        public int payload_size_bytes;
        public bool lsl_forwarded;
        public bool osc_forwarded;
        public string fallback_transport;
    }
}
