using System.Collections;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace TheBigRedButtonInstitute
{
    public class BigRedButtonAnimationTester : MonoBehaviour
    {
        [SerializeField] Animation legacyAnimation;
        [SerializeField] Animator animator;
        [SerializeField] AnimationClip pressedClip;
        [SerializeField] bool playOnStart = true;
        [SerializeField] bool loop = true;
        [SerializeField, Min(0f)] float startDelay = 0.5f;
        [SerializeField, Min(0f)] float pauseBetweenLoops = 1.25f;
        [SerializeField] KeyCode replayKey = KeyCode.Space;

        PlayableGraph _graph;
        AnimationClipPlayable _clipPlayable;
        Coroutine _playbackLoop;
        bool _graphInitialized;

        void Reset()
        {
            legacyAnimation = GetComponentInChildren<Animation>(true);
            animator = GetComponentInChildren<Animator>(true);
        }

        void Awake()
        {
            if (legacyAnimation == null)
            {
                legacyAnimation = GetComponentInChildren<Animation>(true);
            }

            if (animator == null)
            {
                animator = GetComponentInChildren<Animator>(true);
            }

            if (legacyAnimation == null && animator == null)
            {
                animator = gameObject.AddComponent<Animator>();
            }

            ApplyLegacyAnimationDefaults();
            if (pressedClip != null)
            {
                EnsureLegacyClipRegistered();
                SampleClip(0f);
            }
        }

        void OnEnable()
        {
            ApplyLegacyAnimationDefaults();
            if (!playOnStart || !HasAnimation())
            {
                return;
            }

            _playbackLoop = StartCoroutine(PlaybackLoop());
        }

        void Update()
        {
            if (!Application.isPlaying || !HasAnimation() || replayKey == KeyCode.None)
            {
                return;
            }

            if (WasReplayPressed())
            {
                RestartLoop();
            }
        }

        void OnDisable()
        {
            if (_playbackLoop != null)
            {
                StopCoroutine(_playbackLoop);
                _playbackLoop = null;
            }

            StopPlayback(resetPose: true);
            DisposeGraph();
        }

        public void Configure(Animation targetAnimation, AnimationClip animationClip)
        {
            legacyAnimation = targetAnimation;
            animator = null;
            pressedClip = animationClip;
            playOnStart = true;
            loop = true;
            startDelay = 0.5f;
            pauseBetweenLoops = 1.25f;
            replayKey = KeyCode.Space;
            EnsureLegacyClipRegistered();
            ApplyLegacyAnimationDefaults();
        }

        public void Configure(Animator targetAnimator, AnimationClip animationClip)
        {
            legacyAnimation = null;
            animator = targetAnimator;
            pressedClip = animationClip;
            playOnStart = true;
            loop = true;
            startDelay = 0.5f;
            pauseBetweenLoops = 1.25f;
            replayKey = KeyCode.Space;
        }

        public void ConfigurePlayback(bool autoPlay, bool shouldLoop, float initialDelay = 0.5f, float loopPause = 1.25f, KeyCode replayHotkey = KeyCode.None)
        {
            playOnStart = autoPlay;
            loop = shouldLoop;
            startDelay = Mathf.Max(0f, initialDelay);
            pauseBetweenLoops = Mathf.Max(0f, loopPause);
            replayKey = replayHotkey;
        }

        public void RestartLoop()
        {
            if (!isActiveAndEnabled || !HasAnimation())
            {
                return;
            }

            if (_playbackLoop != null)
            {
                StopCoroutine(_playbackLoop);
            }

            StopPlayback(resetPose: true);
            _playbackLoop = StartCoroutine(PlaybackLoop());
        }

        public void StopAndReset()
        {
            if (_playbackLoop != null)
            {
                StopCoroutine(_playbackLoop);
                _playbackLoop = null;
            }

            StopPlayback(resetPose: true);
        }

        public void PlayPressed()
        {
            if (legacyAnimation != null)
            {
                EnsureLegacyClipRegistered();
                legacyAnimation.Stop();

                if (!TryGetLegacyClipName(out var clipName))
                {
                    Debug.LogError("No legacy animation state is available for the big red button test.", this);
                    return;
                }

                var state = legacyAnimation[clipName];
                if (state == null)
                {
                    Debug.LogError($"Legacy animation state '{clipName}' is missing on the big red button.", this);
                    return;
                }

                state.time = 0f;
                state.speed = 1f;
                state.wrapMode = WrapMode.Once;
                legacyAnimation.Play(clipName);
                return;
            }

            if (pressedClip == null)
            {
                Debug.LogError("No animation clip is assigned for the big red button test.", this);
                return;
            }

            if (animator == null)
            {
                Debug.LogError("No Animator is assigned for the big red button test.", this);
                return;
            }

            EnsureGraph();
            _clipPlayable.SetTime(0);
            _clipPlayable.SetSpeed(1d);
            _graph.Play();
        }

        IEnumerator PlaybackLoop()
        {
            if (startDelay > 0f)
            {
                yield return new WaitForSeconds(startDelay);
            }

            do
            {
                PlayPressed();
                var clipLength = GetAnimationLength();
                if (clipLength > 0f)
                {
                    yield return new WaitForSeconds(clipLength);
                }
                else
                {
                    yield return null;
                }

                StopPlayback(resetPose: true);

                if (!loop)
                {
                    break;
                }

                if (pauseBetweenLoops > 0f)
                {
                    yield return new WaitForSeconds(pauseBetweenLoops);
                }
            }
            while (isActiveAndEnabled);

            _playbackLoop = null;
        }

        void EnsureGraph()
        {
            if (_graphInitialized)
            {
                return;
            }

            _graph = PlayableGraph.Create($"{name}_PressedTest");
            _graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);

            _clipPlayable = AnimationClipPlayable.Create(_graph, pressedClip);
            _clipPlayable.SetApplyFootIK(false);
            _clipPlayable.SetApplyPlayableIK(false);
            _clipPlayable.SetDuration(pressedClip.length);

            var output = AnimationPlayableOutput.Create(_graph, "PressedAnimation", animator);
            output.SetSourcePlayable(_clipPlayable);
            _graphInitialized = true;
        }

        void StopPlayback(bool resetPose)
        {
            if (legacyAnimation != null)
            {
                legacyAnimation.Stop();
            }

            if (_graphInitialized)
            {
                _graph.Stop();
            }

            if (resetPose)
            {
                SampleClip(0f);
            }
        }

        void SampleClip(float time)
        {
            var clip = GetAnimationClip();
            if (clip == null)
            {
                return;
            }

            var target = legacyAnimation != null
                ? legacyAnimation.gameObject
                : animator != null
                    ? animator.gameObject
                    : gameObject;

            clip.SampleAnimation(target, Mathf.Clamp(time, 0f, clip.length));
        }

        void DisposeGraph()
        {
            if (!_graphInitialized)
            {
                return;
            }

            _graph.Destroy();
            _graphInitialized = false;
        }

        void EnsureLegacyClipRegistered()
        {
            if (legacyAnimation == null || pressedClip == null)
            {
                return;
            }

            if (legacyAnimation.GetClip(pressedClip.name) == null)
            {
                legacyAnimation.AddClip(pressedClip, pressedClip.name);
            }

            legacyAnimation.clip = legacyAnimation.GetClip(pressedClip.name);
        }

        void ApplyLegacyAnimationDefaults()
        {
            if (legacyAnimation == null)
            {
                return;
            }

            EnsureLegacyClipRegistered();
            legacyAnimation.playAutomatically = false;
            legacyAnimation.wrapMode = WrapMode.Once;
            legacyAnimation.Stop();

            if (!TryGetLegacyClipName(out var clipName))
            {
                return;
            }

            var state = legacyAnimation[clipName];
            if (state == null)
            {
                return;
            }

            state.wrapMode = WrapMode.Once;
            state.speed = 0f;
        }

        bool HasAnimation()
        {
            return GetAnimationClip() != null || TryGetLegacyClipName(out _);
        }

        AnimationClip GetAnimationClip()
        {
            if (pressedClip != null)
            {
                return pressedClip;
            }

            if (legacyAnimation == null)
            {
                return null;
            }

            if (legacyAnimation.clip != null)
            {
                return legacyAnimation.clip;
            }

            foreach (AnimationState state in legacyAnimation)
            {
                return state.clip;
            }

            return null;
        }

        float GetAnimationLength()
        {
            var clip = GetAnimationClip();
            return clip != null ? clip.length : 0f;
        }

        bool TryGetLegacyClipName(out string clipName)
        {
            clipName = null;

            if (legacyAnimation == null)
            {
                return false;
            }

            if (pressedClip != null && legacyAnimation.GetClip(pressedClip.name) != null)
            {
                clipName = pressedClip.name;
                return true;
            }

            if (legacyAnimation.clip != null)
            {
                clipName = legacyAnimation.clip.name;
                return true;
            }

            foreach (AnimationState state in legacyAnimation)
            {
                clipName = state.name;
                return true;
            }

            return false;
        }

        bool WasReplayPressed()
        {
#if ENABLE_INPUT_SYSTEM
            var keyboard = Keyboard.current;
            if (keyboard != null && TryMapReplayKey(out var inputSystemKey))
            {
                return keyboard[inputSystemKey].wasPressedThisFrame;
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(replayKey);
#else
            return false;
#endif
        }

#if ENABLE_INPUT_SYSTEM
        bool TryMapReplayKey(out Key inputSystemKey)
        {
            if (replayKey >= KeyCode.A && replayKey <= KeyCode.Z)
            {
                inputSystemKey = Key.A + (replayKey - KeyCode.A);
                return true;
            }

            if (replayKey >= KeyCode.Alpha0 && replayKey <= KeyCode.Alpha9)
            {
                inputSystemKey = Key.Digit0 + (replayKey - KeyCode.Alpha0);
                return true;
            }

            switch (replayKey)
            {
                case KeyCode.Space:
                    inputSystemKey = Key.Space;
                    return true;
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    inputSystemKey = Key.Enter;
                    return true;
                case KeyCode.Escape:
                    inputSystemKey = Key.Escape;
                    return true;
                case KeyCode.LeftArrow:
                    inputSystemKey = Key.LeftArrow;
                    return true;
                case KeyCode.RightArrow:
                    inputSystemKey = Key.RightArrow;
                    return true;
                case KeyCode.UpArrow:
                    inputSystemKey = Key.UpArrow;
                    return true;
                case KeyCode.DownArrow:
                    inputSystemKey = Key.DownArrow;
                    return true;
                default:
                    inputSystemKey = Key.None;
                    return false;
            }
        }
#endif
    }
}
