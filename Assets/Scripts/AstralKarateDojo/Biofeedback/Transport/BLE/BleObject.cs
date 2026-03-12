using System;
using UnityEngine;

namespace AstralKarateDojo.Biofeedback.Transport.BLE
{
    /// <summary>
    /// JSON message payload sent from the Android BLE plugin to Unity.
    /// This mirrors the UnityAndroidBLE message schema.
    /// </summary>
    [Serializable]
    public class BleObject
    {
        // Device info
        public string Device => device;
        [SerializeField] private string device;

        public string Name => name;
        [SerializeField] private string name;

        public string Service => service;
        [SerializeField] private string service;

        public string Characteristic => characteristic;
        [SerializeField] private string characteristic;

        // Command info
        public string Command => command;
        [SerializeField] private string command;

        // Error info
        public bool HasError => hasError;
        [SerializeField] private bool hasError;

        public string ErrorMessage => errorMessage;
        [SerializeField] private string errorMessage = string.Empty;

        // Data
        public string Base64Message => base64Message;
        [SerializeField] private string base64Message = string.Empty;

        public byte[] GetByteMessage() => Convert.FromBase64String(base64Message);

        public override string ToString() => JsonUtility.ToJson(this, true);
    }
}
