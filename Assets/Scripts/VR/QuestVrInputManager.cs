using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace TheBigRedButtonInstitute.VR
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-21)]
    public sealed class QuestVrInputManager : MonoBehaviour
    {
        const string ButtonObjectName = "Big Red Button";

        public enum VrActionId
        {
            None = 0,
            ToggleHud = 1,
            SelectPreviousCommand = 2,
            SelectNextCommand = 3,
            ExecuteSelectedCommand = 4,
            CenterButton = 5,
            ReplayButtonPress = 6
        }

        public enum VrTerminalCommandId
        {
            None = 0,
            CenterButton = 1,
            PressButton = 2,
            ToggleHud = 3,
            StatusSnapshot = 4
        }

        public enum VrControllerButtonId
        {
            None = 0,
            RightPrimaryButtonA = 1,
            RightSecondaryButtonB = 2,
            RightIndexTrigger = 3,
            RightThumbstickUp = 4,
            RightThumbstickDown = 5,
            RightThumbstickClick = 6,
            LeftPrimaryButtonX = 7,
            LeftSecondaryButtonY = 8,
            LeftIndexTrigger = 9
        }

        [Serializable]
        public struct ActionBinding
        {
            public VrControllerButtonId controllerButton;
            public KeyCode keyboardKey;
            public VrActionId action;
            public string label;
        }

        [Serializable]
        public struct TerminalCommand
        {
            public string command;
            public string description;
            public VrTerminalCommandId action;
        }

        [Header("References")]
        [SerializeField] QuestVrOverlayHud hud;
        [SerializeField] Transform headTransform;
        [SerializeField] Transform buttonTransform;
        [SerializeField] BigRedButtonAnimationTester buttonAnimationTester;

        [Header("Behavior")]
        [SerializeField] bool autoResolveReferences = true;
        [SerializeField, Min(0.6f)] float buttonDistanceFromHead = 1.35f;
        [SerializeField] float buttonVerticalOffset = -0.35f;
        [SerializeField, Min(0.4f)] float minimumButtonHeight = 0.8f;
        [SerializeField, Min(0.1f)] float targetButtonHeight = 0.36f;
        [SerializeField] Vector3 buttonRotationOffsetEuler;

        [Header("Thumbstick Flick")]
        [SerializeField, Range(0.3f, 0.95f)] float thumbstickPressThreshold = 0.7f;
        [SerializeField, Range(0.05f, 0.5f)] float thumbstickRearmThreshold = 0.25f;

        [Header("Bindings")]
        [SerializeField] List<ActionBinding> bindings = new();
        [SerializeField] List<TerminalCommand> commands = new();

        bool _rightThumbstickVerticalArmed = true;
        int _buttonPressCount;

        public IReadOnlyList<ActionBinding> Bindings => bindings;
        public IReadOnlyList<TerminalCommand> Commands => commands;
        public int ButtonPressCount => _buttonPressCount;

        void Reset()
        {
            EnsureConfiguration();
            ResolveReferences(forceRefresh: true);
        }

        void Awake()
        {
            EnsureConfiguration();
            ResolveReferences(forceRefresh: true);
            hud?.ConfigureReferences(this, headTransform);
            EnsureReasonableButtonScale();
        }

        void OnEnable()
        {
            ResolveReferences(forceRefresh: false);
            hud?.ConfigureReferences(this, headTransform);
            hud?.RefreshImmediately();
        }

        void Update()
        {
            ResolveReferences(forceRefresh: false);

            for (var i = 0; i < bindings.Count; i++)
            {
                var binding = bindings[i];
                if (binding.action == VrActionId.None)
                {
                    continue;
                }

                if (WasPressed(binding))
                {
                    ExecuteAction(binding.action);
                }
            }
        }

        public void ConfigureReferences(QuestVrOverlayHud targetHud, Transform head, Transform button, BigRedButtonAnimationTester tester)
        {
            hud = targetHud;
            headTransform = head;
            buttonTransform = button;
            buttonAnimationTester = tester;
            EnsureConfiguration();
            hud?.ConfigureReferences(this, headTransform);
        }

        public void EnsureConfiguration()
        {
            if (bindings == null || bindings.Count == 0)
            {
                bindings = BuildDefaultBindings();
            }

            if (commands == null || commands.Count == 0)
            {
                commands = BuildDefaultCommands();
            }
        }

        public string BuildHudText(int selectedCommandIndex, string transientMessage)
        {
            ResolveReferences(forceRefresh: false);

            var builder = new StringBuilder(2048);
            var statusText = string.IsNullOrWhiteSpace(transientMessage) ? "ready" : transientMessage.Trim();

            builder.AppendLine("<b><size=118%><color=#8FE6FF>=== BIG RED BUTTON ===</color></size></b>");
            builder.AppendLine($"<size=78%><color=#7FA6B8>[ {DateTime.UtcNow:HH:mm:ss} UTC ]</color></size>");
            builder.AppendLine();

            builder.AppendLine("<b><color=#66FFCC>[BUTTON]</color></b>");
            builder.AppendLine($"<color=#AFC0CF>Status:</color> <color=#EAF6FF>{EscapeRichText(statusText)}</color>");
            builder.AppendLine($"<color=#AFC0CF>Press count:</color> <color=#EAF6FF>{_buttonPressCount:N0}</color>");

            if (TryGetButtonBounds(out var bounds))
            {
                builder.AppendLine($"<color=#AFC0CF>Height:</color> <color=#EAF6FF>{bounds.size.y:0.00} m</color>");
            }
            else
            {
                builder.AppendLine("<color=#AFC0CF>Height:</color> <color=#EAF6FF>n/a</color>");
            }

            if (headTransform != null && buttonTransform != null)
            {
                builder.AppendLine(
                    $"<color=#AFC0CF>Distance:</color> <color=#EAF6FF>{Vector3.Distance(headTransform.position, buttonTransform.position):0.00} m</color>");
            }
            else
            {
                builder.AppendLine("<color=#AFC0CF>Distance:</color> <color=#EAF6FF>n/a</color>");
            }

            builder.AppendLine();
            builder.AppendLine("<b><color=#FFD892>[TERMINAL]</color></b>");

            for (var i = 0; i < commands.Count; i++)
            {
                var command = commands[i];
                var marker = i == selectedCommandIndex ? "&gt;" : " ";
                var markerColor = i == selectedCommandIndex ? "#FFD79A" : "#6E8190";
                var commandColor = i == selectedCommandIndex ? "#FFF4D0" : "#E8F2FF";
                var description = string.IsNullOrWhiteSpace(command.description) ? "no description" : command.description.Trim();

                builder.AppendLine(
                    $"<color={markerColor}>{marker}</color> <color={commandColor}>{EscapeRichText(command.command)}</color> " +
                    $"<color=#97A9B6>-</color> <color=#AFC0CF>{EscapeRichText(description)}</color>");
            }

            builder.AppendLine("<color=#4C5A66>--------------------</color>");
            builder.AppendLine("<size=78%><b><color=#C7FFA2>INPUT MAPPINGS</color></b>");

            for (var i = 0; i < bindings.Count; i++)
            {
                var binding = bindings[i];
                if (binding.action == VrActionId.None)
                {
                    continue;
                }

                var label = string.IsNullOrWhiteSpace(binding.label) ? binding.action.ToString() : binding.label.Trim();
                builder.AppendLine(
                    $"<b><color=#FFD79A>{EscapeRichText(GetControllerButtonLabel(binding.controllerButton))}</color></b> " +
                    $"<color=#97A9B6>/</color> <b><color=#C2F5A1>{EscapeRichText(GetKeyboardLabel(binding.keyboardKey))}</color></b> " +
                    $"<color=#97A9B6>-></color> <color=#E8F2FF>{EscapeRichText(label)}</color>");
            }

            builder.AppendLine();
            builder.AppendLine("<color=#8FA3B2>Terminal cursor:</color>");
            builder.AppendLine(
                "<color=#AFC0CF>Move</color> <color=#97A9B6>-></color> " +
                "<color=#E8F2FF>Up/Down or right stick Y flick</color>");
            builder.AppendLine(
                "<color=#AFC0CF>Select</color> <color=#97A9B6>-></color> " +
                "<color=#E8F2FF>R Trigger / Enter</color></size>");

            return builder.ToString().TrimEnd('\n');
        }

        public void ExecuteSelectedCommand()
        {
            if (hud == null)
            {
                return;
            }

            ExecuteCommand(hud.GetSelectedCommand());
        }

        public void CenterButtonInFrontOfHead()
        {
            ResolveReferences(forceRefresh: false);
            if (buttonTransform == null || headTransform == null)
            {
                hud?.SetTransientMessage("center_button failed: missing references");
                return;
            }

            EnsureReasonableButtonScale();

            var horizontalForward = Vector3.ProjectOnPlane(headTransform.forward, Vector3.up);
            if (horizontalForward.sqrMagnitude < 0.001f)
            {
                horizontalForward = headTransform.forward;
            }

            horizontalForward.Normalize();

            var targetPosition = headTransform.position + horizontalForward * buttonDistanceFromHead;
            targetPosition.y = Mathf.Max(minimumButtonHeight, headTransform.position.y + buttonVerticalOffset);

            buttonTransform.position = targetPosition;

            var baseRotation = Quaternion.LookRotation(-horizontalForward, Vector3.up);
            buttonTransform.rotation = baseRotation * Quaternion.Euler(buttonRotationOffsetEuler);

            hud?.SetTransientMessage("center_button executed");
            hud?.RefreshImmediately();
        }

        void ExecuteAction(VrActionId action)
        {
            switch (action)
            {
                case VrActionId.ToggleHud:
                    if (hud != null)
                    {
                        hud.ToggleVisibility();
                        if (hud.IsVisible)
                        {
                            hud.SetTransientMessage("hud visible");
                        }
                    }
                    break;
                case VrActionId.SelectPreviousCommand:
                    hud?.SelectPreviousCommand();
                    break;
                case VrActionId.SelectNextCommand:
                    hud?.SelectNextCommand();
                    break;
                case VrActionId.ExecuteSelectedCommand:
                    ExecuteSelectedCommand();
                    break;
                case VrActionId.CenterButton:
                    CenterButtonInFrontOfHead();
                    break;
                case VrActionId.ReplayButtonPress:
                    ReplayButtonPress();
                    break;
            }
        }

        void ExecuteCommand(TerminalCommand command)
        {
            switch (command.action)
            {
                case VrTerminalCommandId.CenterButton:
                    CenterButtonInFrontOfHead();
                    break;
                case VrTerminalCommandId.PressButton:
                    ReplayButtonPress();
                    break;
                case VrTerminalCommandId.ToggleHud:
                    ExecuteAction(VrActionId.ToggleHud);
                    break;
                case VrTerminalCommandId.StatusSnapshot:
                    var snapshot = BuildButtonSummary();
                    Debug.Log($"[QuestVrInputManager] {snapshot}", this);
                    hud?.SetTransientMessage($"status: {snapshot}");
                    break;
            }
        }

        void ReplayButtonPress()
        {
            ResolveReferences(forceRefresh: false);

            if (buttonAnimationTester == null)
            {
                hud?.SetTransientMessage("press_button failed: no animation tester");
                return;
            }

            buttonAnimationTester.StopAndReset();
            buttonAnimationTester.PlayPressed();
            _buttonPressCount++;
            hud?.SetTransientMessage("press_button executed");
            hud?.RefreshImmediately();
        }

        void ResolveReferences(bool forceRefresh)
        {
            if (!autoResolveReferences && !forceRefresh)
            {
                return;
            }

            if (hud == null)
            {
                hud = GetComponentInChildren<QuestVrOverlayHud>(true);
            }

            if (headTransform == null || forceRefresh)
            {
                var cameraRig = FindAnyObjectByType<OVRCameraRig>();
                if (cameraRig != null)
                {
                    headTransform = cameraRig.centerEyeAnchor;
                }
            }

            if ((headTransform == null || forceRefresh) && Camera.main != null)
            {
                headTransform = Camera.main.transform;
            }

            if (buttonTransform == null || forceRefresh)
            {
                var buttonObject = GameObject.Find(ButtonObjectName);
                if (buttonObject != null)
                {
                    buttonTransform = buttonObject.transform;
                }
            }

            if ((buttonAnimationTester == null || forceRefresh) && buttonTransform != null)
            {
                buttonAnimationTester = buttonTransform.GetComponent<BigRedButtonAnimationTester>();
            }
        }

        bool WasPressed(ActionBinding binding)
        {
            return WasControllerPressed(binding.controllerButton) || WasKeyboardPressed(binding.keyboardKey);
        }

        bool WasControllerPressed(VrControllerButtonId button)
        {
            switch (button)
            {
                case VrControllerButtonId.RightPrimaryButtonA:
                    return OVRInput.GetDown(OVRInput.RawButton.A);
                case VrControllerButtonId.RightSecondaryButtonB:
                    return OVRInput.GetDown(OVRInput.RawButton.B);
                case VrControllerButtonId.RightIndexTrigger:
                    return OVRInput.GetDown(OVRInput.RawButton.RIndexTrigger);
                case VrControllerButtonId.RightThumbstickUp:
                    return WasRightThumbstickVerticalPressed(true);
                case VrControllerButtonId.RightThumbstickDown:
                    return WasRightThumbstickVerticalPressed(false);
                case VrControllerButtonId.RightThumbstickClick:
                    return OVRInput.GetDown(OVRInput.RawButton.RThumbstick);
                case VrControllerButtonId.LeftPrimaryButtonX:
                    return OVRInput.GetDown(OVRInput.RawButton.X);
                case VrControllerButtonId.LeftSecondaryButtonY:
                    return OVRInput.GetDown(OVRInput.RawButton.Y);
                case VrControllerButtonId.LeftIndexTrigger:
                    return OVRInput.GetDown(OVRInput.RawButton.LIndexTrigger);
                default:
                    return false;
            }
        }

        bool WasRightThumbstickVerticalPressed(bool positiveDirection)
        {
            var thumbstick = OVRInput.Get(OVRInput.Axis2D.SecondaryThumbstick);
            var vertical = thumbstick.y;

            if (!_rightThumbstickVerticalArmed)
            {
                if (Mathf.Abs(vertical) <= thumbstickRearmThreshold)
                {
                    _rightThumbstickVerticalArmed = true;
                }

                return false;
            }

            var pressed = positiveDirection
                ? vertical >= thumbstickPressThreshold
                : vertical <= -thumbstickPressThreshold;

            if (pressed)
            {
                _rightThumbstickVerticalArmed = false;
                return true;
            }

            return false;
        }

        bool WasKeyboardPressed(KeyCode keyCode)
        {
            if (keyCode == KeyCode.None)
            {
                return false;
            }

#if ENABLE_INPUT_SYSTEM
            var keyboard = Keyboard.current;
            if (keyboard != null && TryMapInputSystemKey(keyCode, out var inputKey))
            {
                return keyboard[inputKey].wasPressedThisFrame;
            }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(keyCode);
#else
            return false;
#endif
        }

#if ENABLE_INPUT_SYSTEM
        static bool TryMapInputSystemKey(KeyCode keyCode, out Key key)
        {
            if (keyCode >= KeyCode.A && keyCode <= KeyCode.Z)
            {
                key = Key.A + (keyCode - KeyCode.A);
                return true;
            }

            if (keyCode >= KeyCode.Alpha0 && keyCode <= KeyCode.Alpha9)
            {
                key = Key.Digit0 + (keyCode - KeyCode.Alpha0);
                return true;
            }

            switch (keyCode)
            {
                case KeyCode.UpArrow:
                    key = Key.UpArrow;
                    return true;
                case KeyCode.DownArrow:
                    key = Key.DownArrow;
                    return true;
                case KeyCode.LeftArrow:
                    key = Key.LeftArrow;
                    return true;
                case KeyCode.RightArrow:
                    key = Key.RightArrow;
                    return true;
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    key = Key.Enter;
                    return true;
                case KeyCode.Space:
                    key = Key.Space;
                    return true;
                case KeyCode.Escape:
                    key = Key.Escape;
                    return true;
                default:
                    key = Key.None;
                    return false;
            }
        }
#endif

        string BuildButtonSummary()
        {
            if (buttonTransform == null)
            {
                return "missing";
            }

            var summary = new StringBuilder(96);
            summary.Append("ready");
            summary.Append("  presses ");
            summary.Append(_buttonPressCount);

            if (TryGetButtonBounds(out var bounds))
            {
                summary.Append("  ");
                summary.Append(bounds.size.y.ToString("0.00"));
                summary.Append("m tall");
            }

            if (headTransform != null)
            {
                summary.Append("  ");
                summary.Append(Vector3.Distance(headTransform.position, buttonTransform.position).ToString("0.00"));
                summary.Append("m away");
            }

            return summary.ToString();
        }

        void EnsureReasonableButtonScale()
        {
            if (buttonTransform == null || !TryGetButtonBounds(out var bounds))
            {
                return;
            }

            if (bounds.size.y <= 0.0001f)
            {
                return;
            }

            var scaleFactor = targetButtonHeight / bounds.size.y;
            buttonTransform.localScale *= scaleFactor;
        }

        bool TryGetButtonBounds(out Bounds bounds)
        {
            bounds = default;
            if (buttonTransform == null)
            {
                return false;
            }

            var renderers = buttonTransform.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                return false;
            }

            bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            return true;
        }

        static List<ActionBinding> BuildDefaultBindings()
        {
            return new List<ActionBinding>
            {
                new()
                {
                    controllerButton = VrControllerButtonId.RightPrimaryButtonA,
                    keyboardKey = KeyCode.C,
                    action = VrActionId.CenterButton,
                    label = "center_button"
                },
                new()
                {
                    controllerButton = VrControllerButtonId.RightSecondaryButtonB,
                    keyboardKey = KeyCode.H,
                    action = VrActionId.ToggleHud,
                    label = "toggle_hud"
                },
                new()
                {
                    controllerButton = VrControllerButtonId.RightThumbstickUp,
                    keyboardKey = KeyCode.UpArrow,
                    action = VrActionId.SelectPreviousCommand,
                    label = "command_up"
                },
                new()
                {
                    controllerButton = VrControllerButtonId.RightThumbstickDown,
                    keyboardKey = KeyCode.DownArrow,
                    action = VrActionId.SelectNextCommand,
                    label = "command_down"
                },
                new()
                {
                    controllerButton = VrControllerButtonId.RightIndexTrigger,
                    keyboardKey = KeyCode.Return,
                    action = VrActionId.ExecuteSelectedCommand,
                    label = "execute_command"
                }
            };
        }

        static List<TerminalCommand> BuildDefaultCommands()
        {
            return new List<TerminalCommand>
            {
                new()
                {
                    command = "center_button",
                    description = "place the button in front of the viewer",
                    action = VrTerminalCommandId.CenterButton
                },
                new()
                {
                    command = "press_button",
                    description = "play the imported press animation once",
                    action = VrTerminalCommandId.PressButton
                },
                new()
                {
                    command = "toggle_hud",
                    description = "show or hide the overlay",
                    action = VrTerminalCommandId.ToggleHud
                },
                new()
                {
                    command = "status",
                    description = "log the current button placement snapshot",
                    action = VrTerminalCommandId.StatusSnapshot
                }
            };
        }

        static string GetControllerButtonLabel(VrControllerButtonId button)
        {
            return button switch
            {
                VrControllerButtonId.RightPrimaryButtonA => "A",
                VrControllerButtonId.RightSecondaryButtonB => "B",
                VrControllerButtonId.RightIndexTrigger => "R Trigger",
                VrControllerButtonId.RightThumbstickUp => "R Stick Up",
                VrControllerButtonId.RightThumbstickDown => "R Stick Down",
                VrControllerButtonId.RightThumbstickClick => "R Stick Click",
                VrControllerButtonId.LeftPrimaryButtonX => "X",
                VrControllerButtonId.LeftSecondaryButtonY => "Y",
                VrControllerButtonId.LeftIndexTrigger => "L Trigger",
                _ => "-"
            };
        }

        static string GetKeyboardLabel(KeyCode keyCode)
        {
            return keyCode switch
            {
                KeyCode.None => "-",
                KeyCode.UpArrow => "Up",
                KeyCode.DownArrow => "Down",
                KeyCode.LeftArrow => "Left",
                KeyCode.RightArrow => "Right",
                KeyCode.Return => "Enter",
                KeyCode.KeypadEnter => "Enter",
                _ => keyCode.ToString()
            };
        }

        static string GetBindingLabel(ActionBinding binding)
        {
            var controller = GetControllerButtonLabel(binding.controllerButton);
            var keyboard = GetKeyboardLabel(binding.keyboardKey);
            return $"{controller} / {keyboard}";
        }

        static string EscapeRichText(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;");
        }

        static string FormatHudColumn(string value, int width)
        {
            var safeValue = string.IsNullOrWhiteSpace(value) ? "-" : value;
            return safeValue.Length >= width
                ? safeValue + "  "
                : safeValue.PadRight(width);
        }
    }
}
