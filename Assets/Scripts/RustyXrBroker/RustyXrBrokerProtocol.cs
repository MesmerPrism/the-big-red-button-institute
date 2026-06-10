using System;
using UnityEngine;

namespace TheBigRedButtonInstitute.RustyXrBroker
{
    public static class RustyXrBrokerProtocol
    {
        public const string ContractVersion = "rusty.xr.broker.v1";
        public const int ProtocolVersionMin = 1;
        public const int ProtocolVersionMax = 1;
        public const string HelloSchema = "rusty.xr.broker.client_hello.v1";
        public const string CommandSchema = "rusty.xr.broker.command.v1";
        public const string CommandAckSchema = "rusty.xr.broker.command_ack.v1";
        public const string StreamEventSchema = "rusty.xr.broker.stream_event.v1";
        public const string ReplayRecordSchema = "rusty.xr.broker.replay_record.v1";
        public const string StreamSampleHeaderSchema = "rusty.xr.broker.stream_sample_header.v1";
        public const string SyntheticWavePayloadSchema = "rusty.xr.synthetic.wave.v1";
        public const string EyeScreenGazePointSchema = "rusty.xr.eye.screen.gaze_point.v1";

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
                protocol_min = ProtocolVersionMin,
                protocol_max = ProtocolVersionMax,
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
            BuildCommandJson("status_request", requestId, clientId, appPackage, appLabel, appVersion, (string)null);

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
            BuildCommandJson("list_streams", requestId, clientId, appPackage, appLabel, appVersion, (string)null);

        public static string BuildListCapabilitiesCommandJson(
            string requestId,
            string clientId,
            string appPackage,
            string appLabel,
            string appVersion) =>
            BuildCommandJson("list_capabilities", requestId, clientId, appPackage, appLabel, appVersion, (string)null);

        public static string BuildOpenUiCommandJson(
            string requestId,
            string clientId,
            string appPackage,
            string appLabel,
            string appVersion) =>
            BuildCommandJson("open_ui", requestId, clientId, appPackage, appLabel, appVersion, (string)null);

        public static string BuildCloseUiCommandJson(
            string requestId,
            string clientId,
            string appPackage,
            string appLabel,
            string appVersion) =>
            BuildCommandJson("close_ui", requestId, clientId, appPackage, appLabel, appVersion, (string)null);

        public static string BuildPolarStartCommandJson(
            string requestId,
            string clientId,
            string appPackage,
            string appLabel,
            string appVersion,
            bool includeHeartRate,
            bool includePmd,
            int scanTimeoutMs) =>
            BuildCommandJson(
                "polar.start",
                requestId,
                clientId,
                appPackage,
                appLabel,
                appVersion,
                new RustyXrBrokerCommandParams
                {
                    include_hr = includeHeartRate,
                    include_pmd = includePmd,
                    scan_timeout_ms = scanTimeoutMs
                });

        public static string BuildPolarPmdStartCommandJson(
            string requestId,
            string clientId,
            string appPackage,
            string appLabel,
            string appVersion,
            int scanTimeoutMs) =>
            BuildCommandJson(
                "polar_pmd.start",
                requestId,
                clientId,
                appPackage,
                appLabel,
                appVersion,
                new RustyXrBrokerCommandParams
                {
                    scan_timeout_ms = scanTimeoutMs
                });

        public static string BuildPolarStopCommandJson(
            string requestId,
            string clientId,
            string appPackage,
            string appLabel,
            string appVersion,
            bool stopHeartRate,
            bool stopPmd) =>
            BuildCommandJson(
                "polar.stop",
                requestId,
                clientId,
                appPackage,
                appLabel,
                appVersion,
                new RustyXrBrokerCommandParams
                {
                    stop_hr = stopHeartRate,
                    stop_pmd = stopPmd
                });

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

        static string BuildCommandJson(
            string command,
            string requestId,
            string clientId,
            string appPackage,
            string appLabel,
            string appVersion,
            RustyXrBrokerCommandParams parameters)
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
                @params = parameters
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
                    !parsed.NormalizeFromHeader())
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

        public static bool TryParseReplayRecordAsStreamEvent(string json, out RustyXrBrokerStreamEvent streamEvent)
        {
            streamEvent = null;
            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            try
            {
                var parsed = JsonUtility.FromJson<RustyXrBrokerReplayRecordEnvelope>(json);
                if (parsed == null ||
                    parsed.type != "replay_record" ||
                    parsed.schema != ReplayRecordSchema ||
                    string.IsNullOrWhiteSpace(parsed.session_id) ||
                    parsed.header == null ||
                    parsed.payload == null)
                {
                    return false;
                }

                var normalized = new RustyXrBrokerStreamEvent
                {
                    type = "stream_event",
                    schema = StreamEventSchema,
                    stream = parsed.stream,
                    header = parsed.header,
                    payload = parsed.payload
                };

                if (!normalized.NormalizeFromHeader())
                {
                    return false;
                }

                streamEvent = normalized;
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
        public int protocol_min;
        public int protocol_max;
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
        public bool include_hr;
        public bool include_pmd;
        public bool stop_hr;
        public bool stop_pmd;
        public int scan_timeout_ms;
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
    public sealed class RustyXrBrokerReplayRecordEnvelope
    {
        public string type;
        public string schema;
        public string session_id;
        public string stream;
        public RustyXrBrokerStreamSampleHeader header;
        public RustyXrBrokerStreamPayload payload;
    }

    [Serializable]
    public sealed class RustyXrBrokerStreamEvent
    {
        public string type;
        public string schema;
        public string stream;
        public string subscription_id;
        public RustyXrBrokerStreamSampleHeader header;
        public long sequence_id;
        public long broker_time_unix_ns;
        public long broker_time_elapsed_ns;
        public long source_time_ns;
        public long source_time_unix_ns;
        public long dropped_before_sample;
        public long late_before_sample;
        public string payload_schema;
        public RustyXrBrokerStreamPayload payload;

        public bool NormalizeFromHeader()
        {
            if (header != null)
            {
                if (string.IsNullOrWhiteSpace(stream))
                {
                    stream = header.stream_id;
                }

                if (sequence_id == 0L)
                {
                    sequence_id = header.sequence_number;
                }

                if (broker_time_unix_ns == 0L)
                {
                    broker_time_unix_ns = header.broker_time_unix_ns;
                }

                if (broker_time_elapsed_ns == 0L)
                {
                    broker_time_elapsed_ns = header.broker_time_elapsed_ns;
                }

                if (source_time_ns == 0L)
                {
                    source_time_ns = header.source_time_ns;
                }

                if (source_time_unix_ns == 0L)
                {
                    source_time_unix_ns = header.source_time_unix_ns;
                }

                if (dropped_before_sample == 0L)
                {
                    dropped_before_sample = header.dropped_before_sample;
                }

                if (late_before_sample == 0L)
                {
                    late_before_sample = header.late_before_sample;
                }

                if (string.IsNullOrWhiteSpace(payload_schema))
                {
                    payload_schema = header.payload_schema;
                }
            }

            return !string.IsNullOrWhiteSpace(stream);
        }
    }

    [Serializable]
    public sealed class RustyXrBrokerStreamSampleHeader
    {
        public string schema;
        public string stream_id;
        public string session_id;
        public string source_id;
        public string payload_kind;
        public string payload_schema;
        public long sequence_number;
        public long broker_time_elapsed_ns;
        public long broker_time_unix_ns;
        public long source_time_ns;
        public long source_time_unix_ns;
        public long dropped_before_sample;
        public long late_before_sample;
    }

    [Serializable]
    public sealed class RustyXrBrokerStreamPayload
    {
        public string schema;
        public string address;
        public float value01;
        public RustyXrBrokerVec2 normalized_point;
        public RustyXrBrokerVec2 screen_pixel;
        public RustyXrBrokerEyeSampleBase @base;
        public string display_id;
        public float pupil_diameter_mm;
        public string peer;
        public string argument_type;
        public string path;
        public int payload_size_bytes;
        public bool lsl_forwarded;
        public bool osc_forwarded;
        public string fallback_transport;
        public string stream_id;
        public string source;
        public string source_detail;
        public string input_stream;
        public string output_stream;
        public string device_address;
        public string device_name;
        public string payload_base64;
        public float heart_rate_bpm;
        public int rr_count;
        public int sample_count;
        public long sample_time_unix_ns;
        public long sample_time_elapsed_ns;
        public long sensor_timestamp_ns;
        public long broker_receive_time_unix_ns;
        public long broker_receive_time_elapsed_ns;
        public long broker_publish_time_unix_ns;
        public long broker_publish_time_elapsed_ns;
        public float volume01;
        public float state01;
        public float tracking01;
        public float quality01;
        public bool has_volume;
        public bool is_calibrated;
        public bool is_calibrating;
        public bool compressed;
    }

    [Serializable]
    public sealed class RustyXrBrokerVec2
    {
        public float x;
        public float y;
    }

    [Serializable]
    public sealed class RustyXrBrokerEyeSampleBase
    {
        public string provider_id;
        public string source_device_id;
        public long sequence_number;
        public long sample_time_ns;
        public long broker_receive_time_ns;
        public RustyXrBrokerEyeValidityFlags validity;
        public float confidence;
        public string eye;
        public string coordinate_space;
    }

    [Serializable]
    public sealed class RustyXrBrokerEyeValidityFlags
    {
        public bool sample_valid;
        public bool left_valid;
        public bool right_valid;
        public bool blink;
        public bool tracking_lost;
    }
}
