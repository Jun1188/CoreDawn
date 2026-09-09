using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using UnityEngine.Video;
using CoreDawn.Sound;

namespace CoreDawn.Managers
{
    /// <summary>
    /// mp4 컷신 — <see cref="VideoPlayer"/> 로 RenderTexture 에 그려 AppFlow 오버레이(UI Toolkit)의 <c>cutscene</c> 요소 배경으로 띄운다.
    /// 오버레이 위라 게임 카메라·HUD 를 덮고, 검정 바탕에 비율 유지(scale-to-fit)로 맞는다. 소리는 BGM 믹서 그룹으로(설정의 BGM 볼륨을 따른다).
    /// 파일은 <c>StreamingAssets/cutscenes/</c> 아래 mp4 — 에셋으로 임포트(트랜스코드)하지 않고 URL 로 연다. 파일이 없으면 로그만 남기고 바로 끝난다.
    /// ESC 를 <see cref="SkipHoldSeconds"/> 동안 꾹 누르면 건너뛴다(게임패드 Start 도 같음) — 왼쪽 아래 안내 문구 + 누른 만큼 차는 바. 재생 중 <c>Time.timeScale</c> 은 0 — 뒤에서 이미 시작된 월드(낮 시계·몬스터)가 흐르지 않게.
    /// </summary>
    public static class CutscenePlayer
    {
        /// <summary>첫 새 게임 인트로 — 사용자가 mp4 를 여기에 넣는다.</summary>
        public const string IntroPath = "cutscenes/intro.mp4";

        const float SkipArmSeconds = 0.5f;   // 이 뒤에 안내를 띄운다
        const float SkipHoldSeconds = 1f;    // ESC 를 이만큼 누르면 건너뜀
        const float FadeSeconds = 0.4f;    // .cutscene / .cutscene-skip 의 USS transition 과 같게

        /// <summary>StreamingAssets 상대 경로의 mp4 를 <paramref name="host"/> 안에 틀고 끝(또는 스킵)까지 기다린다. 호스트가 null 이거나 파일이 없으면 즉시 반환.</summary>
        public static IEnumerator Play(MonoBehaviour owner, VisualElement host, string relativePath)
        {
            if (host == null) { Debug.LogWarning("[Cutscene] 오버레이에 'cutscene' 요소가 없습니다 — 건너뜁니다."); yield break; }
            string full = Path.Combine(Application.streamingAssetsPath, relativePath).Replace('\\', '/');   // VideoPlayer.url 은 구분자가 섞이면 못 연다(실측 "cannot play url")
            if (!File.Exists(full)) { Debug.Log($"[Cutscene] '{relativePath}' 가 없어 건너뜁니다 (StreamingAssets/{relativePath} 에 mp4 를 넣으세요)."); yield break; }

            var go = new GameObject("[Cutscene]");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<CutsceneInputBlocker>();   // 재생 중 게임 입력(ESC → 일시정지 등)을 전부 삼킨다 — ESC 홀드는 Keyboard 를 직접 읽는다
            var audio = go.AddComponent<AudioSource>();
            audio.playOnAwake = false;
            if (SoundManager.Instance != null) audio.outputAudioMixerGroup = SoundManager.Instance.BgmGroup;
            var vp = go.AddComponent<VideoPlayer>();
            vp.playOnAwake = false;
            vp.isLooping = false;
            vp.skipOnDrop = true;
            vp.source = VideoSource.Url;
            vp.url = "file://" + full;
            vp.audioOutputMode = VideoAudioOutputMode.AudioSource;
            vp.SetTargetAudioSource(0, audio);
            vp.renderMode = VideoRenderMode.RenderTexture;
            vp.aspectRatio = VideoAspectRatio.FitInside;

            bool failed = false;
            vp.errorReceived += (_, msg) => { Debug.LogError("[Cutscene] " + msg); failed = true; };
            vp.Prepare();
            while (!vp.isPrepared && !failed) yield return null;
            RenderTexture rt = null;
            if (!failed)
            {
                rt = new RenderTexture((int)vp.width, (int)vp.height, 0) { name = "Cutscene RT" };
                rt.Create();
                vp.targetTexture = rt;
                host.style.backgroundImage = Background.FromRenderTexture(rt);
                host.style.display = DisplayStyle.Flex;
                host.AddToClassList("cutscene--on");
                host.pickingMode = PickingMode.Position;

                float prevScale = Time.timeScale;
                Time.timeScale = 0f;
                var skipHint = host.Q("cutscene-skip");
                var skipFill = host.Q("cutscene-skip-fill");
                vp.Play();
                float start = Time.unscaledTime, hold = 0f;
                bool armed = false;
                // 끝 판정: isPlaying 이 꺼지거나(자연 종료) ESC 홀드. 마지막 프레임 근처에서 isPlaying 이 잠깐 true 로 남는 경우가 있어 frame 도 본다.
                while (!failed && (vp.isPlaying || !armed))
                {
                    if (!armed && Time.unscaledTime - start >= SkipArmSeconds) { armed = true; skipHint?.AddToClassList("cutscene-skip--on"); }
                    hold = SkipHeld() ? hold + Time.unscaledDeltaTime : 0f;
                    if (skipFill != null) skipFill.style.width = Length.Percent(Mathf.Clamp01(hold / SkipHoldSeconds) * 100f);
                    if (hold >= SkipHoldSeconds) break;
                    if (vp.frame >= 0 && vp.frameCount > 0 && (ulong)vp.frame >= vp.frameCount - 1) break;
                    yield return null;
                }
                Time.timeScale = prevScale;

                host.RemoveFromClassList("cutscene--on");
                skipHint?.RemoveFromClassList("cutscene-skip--on");
                float until = Time.unscaledTime + FadeSeconds;
                while (Time.unscaledTime < until) yield return null;   // 검정으로 페이드(USS)
                host.style.display = DisplayStyle.None;
                host.pickingMode = PickingMode.Ignore;
                host.style.backgroundImage = StyleKeyword.Null;
            }

            vp.Stop();
            Object.Destroy(go);
            if (rt != null) { rt.Release(); Object.Destroy(rt); }
        }

        /// <summary>컷신 동안 InputManager 라우팅의 최상위(SystemModal)에서 모든 액션을 소비한다 — 실측: ESC 홀드의 첫 프레임이 Fallback 까지 흘러 일시정지가 뒤에서 열렸다.</summary>
        sealed class CutsceneInputBlocker : MonoBehaviour, Inputs.IInputReceiver
        {
            bool registered;
            public int Priority => Inputs.InputPriority.SystemModal;
            public bool IsInputActive => isActiveAndEnabled;
            void Awake() => TryRegister();
            void Update() => TryRegister();   // InputManager 가 나중에 생겨도 붙는다
            void TryRegister()
            {
                if (registered || Inputs.InputManager.Instance == null) return;
                Inputs.InputManager.Instance.Register(this);
                registered = true;
            }
            void OnDestroy() { if (registered && Inputs.InputManager.Instance != null) Inputs.InputManager.Instance.Unregister(this); }
            public bool OnInput(in Inputs.InputEvent e) => true;
        }

        static bool SkipHeld()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.isPressed) return true;
            var pad = Gamepad.current;
            return pad != null && pad.startButton.isPressed;
        }
    }
}
