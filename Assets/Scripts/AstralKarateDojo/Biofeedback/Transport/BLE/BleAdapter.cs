using System;
using UnityEngine;
using AstralKarateDojo.RuntimeUtilities;

namespace AstralKarateDojo.Biofeedback.Transport.BLE
{
    /// <summary>
    /// Adapter between the Java BLE plugin and Unity C# runtime.
    /// The Android plugin sends JSON messages to a GameObject named "BleAdapter".
    /// This class is a thin relay: parse JSON, raise C# events.
    /// </summary>
    public class BleAdapter : MonoBehaviour
    {
        private const int MaxPayloadPreviewChars = 256;

        public event Action<BleObject> OnMessageReceived;
        public event Action<string> OnErrorReceived;

        private void Awake()
        {
            // Required name for UnitySendMessage callbacks from Java plugin.
            gameObject.name = nameof(BleAdapter);
        }

        /// <summary>
        /// Called by the Android BLE plugin via UnitySendMessage.
        /// </summary>
        public void OnBleMessage(string jsonMessage)
        {
            if (string.IsNullOrWhiteSpace(jsonMessage))
            {
                EmitMalformedMessage("BLE plugin sent empty JSON payload.", jsonMessage);
                return;
            }

            BleObject obj;
            try
            {
                obj = JsonUtility.FromJson<BleObject>(jsonMessage);
            }
            catch (Exception ex)
            {
                EmitMalformedMessage($"BLE plugin JSON parse failed: {ex.Message}", jsonMessage);
                return;
            }

            if (obj == null)
            {
                EmitMalformedMessage("BLE plugin JSON parsed to null object.", jsonMessage);
                return;
            }

            // Non-error messages should always include a command name.
            if (!obj.HasError && string.IsNullOrEmpty(obj.Command))
            {
                EmitMalformedMessage("BLE plugin message missing required 'command' field.", jsonMessage);
                return;
            }

            if (obj.HasError)
            {
                string message = string.IsNullOrWhiteSpace(obj.ErrorMessage)
                    ? "BLE plugin reported an unspecified error."
                    : obj.ErrorMessage;
                OnErrorReceived?.Invoke(message);
            }
            else
            {
                OnMessageReceived?.Invoke(obj);
            }
        }

        /// <summary>
        /// Optional logging hook for plugin-side logs.
        /// </summary>
        public void LogMessage(string log)
        {
            if (RuntimeLogManager.IsLogChannelEnabled(RuntimeLogManager.RuntimeLogChannel.BleVerboseMessages))
                Debug.Log(log);
        }

        private void EmitMalformedMessage(string reason, string payload)
        {
            string safePayload = string.IsNullOrEmpty(payload)
                ? "<empty>"
                : payload.Length > MaxPayloadPreviewChars
                    ? payload.Substring(0, MaxPayloadPreviewChars) + "..."
                    : payload;

            string msg = $"[BleAdapter] {reason} Payload={safePayload}";
            Debug.LogWarning(msg);
            OnErrorReceived?.Invoke(msg);
        }
    }
}

