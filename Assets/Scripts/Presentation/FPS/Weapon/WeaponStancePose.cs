using DG.Tweening;
using UnityEngine;

namespace CoreDawn.FPS
{
    /// <summary>
    /// 자세 포즈 + 충격 연출 — <b>이산</b> 사건을 담당하는 무기 모션 모듈.
    /// (연속 신호인 스웨이/보브는 <see cref="HandSway"/>가 맡는다. 둘을 한 클래스에 섞으면
    ///  "지금 이 흔들림이 어디서 왔는지"를 아무도 추적할 수 없게 된다.)
    ///
    ///  · 달리기 → 총구를 내리고 안쪽으로 접는다 (조준 불가 상태임을 손으로 알린다)
    ///  · 앉기   → 몸쪽으로 당겨 낮춘다
    ///  · 슬라이딩 → 크게 눕히고 뒤로 당긴다
    ///  · 착지/점프/슬라이드 진입 → DOTween 펀치
    ///
    /// 가중치 전환에 DOTween을 쓰는 이유: 이징 곡선이 있어야 "자세를 바꿨다"는 동작이
    /// 감쇠 보간(무한히 수렴하는 곡선)보다 훨씬 또렷하게 읽힌다.
    /// </summary>
    public class WeaponStancePose : MonoBehaviour, IWeaponMotionModule
    {
        [Header("Sprint Pose")]
        [Tooltip("달릴 때의 위치 오프셋(m).")]
        public Vector3 sprintPosition = new Vector3(0.05f, -0.055f, -0.06f);
        [Tooltip("달릴 때의 회전 오프셋(도).")]
        public Vector3 sprintRotation = new Vector3(18f, -22f, 12f);
        public float sprintEnterTime = 0.2f;
        public float sprintExitTime = 0.13f;

        [Header("Crouch Pose")]
        public Vector3 crouchPosition = new Vector3(0.012f, -0.02f, 0.018f);
        public Vector3 crouchRotation = new Vector3(-2.5f, 0f, 1.5f);

        [Header("Slide Pose")]
        public Vector3 slidePosition = new Vector3(0.03f, -0.045f, -0.05f);
        public Vector3 slideRotation = new Vector3(-6f, -10f, 16f);

        [Header("Impact (착지/점프/슬라이드 진입)")]
        [Tooltip("착지 충격 1.0에서의 하강량(m).")]
        public float landDrop = 0.075f;
        [Tooltip("착지 충격 1.0에서의 피치(도).")]
        public float landPitch = 13f;
        public float landAttack = 0.06f;
        public float landRelease = 0.3f;

        [Tooltip("도약 순간 무기가 위로 뜨는 양(m).")]
        public float jumpRise = 0.03f;
        public float jumpAttack = 0.07f;
        public float jumpRelease = 0.22f;

        [Tooltip("슬라이드 진입 시 뒤로 당겨지는 양(m).")]
        public float slidePunchZ = 0.06f;
        public float slidePunchPitch = -9f;

        [Header("ADS")]
        [Tooltip("조준 중 자세 포즈(달리기·앉기·슬라이딩)를 억제하는 비율.")]
        [Range(0f, 1f)] public float aimSuppression = 1f;
        [Tooltip("조준 중 충격 펀치(착지·점프·슬라이드 진입)를 억제하는 비율. " +
                 "자세 포즈와 따로 두는 이유: 1로 완전히 죽이면 조준 중 착지가 아무 감각 없이 지나간다. " +
                 "가늠자가 눈앞에 붙어 있는 상태에서는 같은 오프셋도 몇 배로 크게 읽히므로 대부분을 눌러야 한다.")]
        [Range(0f, 1f)] public float impactAimSuppression = 0.85f;
        [Tooltip("포즈 추종 속도(가중치가 아니라 최종 합성값의 스무딩).")]
        public float sharpness = 14f;

        public Vector3 PositionOffset { get; private set; }
        public Quaternion RotationOffset { get; private set; } = Quaternion.identity;

        private IPlayerMotionProvider _provider;
        private PlayerMotionState Motion => _provider?.Motion;

        private float _sprintWeight;
        private bool _sprintActive;
        private Tween _sprintTween;

        private Vector3 _impactPos;
        private Vector3 _impactEuler;
        private Sequence _impactSeq;

        private Vector3 _pos;
        private Vector3 _euler;

        private void Awake() => _provider = GetComponentInParent<IPlayerMotionProvider>();

        private void OnEnable()
        {
            if (Motion == null) return;
            Motion.Landed += OnLanded;
            Motion.Jumped += OnJumped;
            Motion.SlideStarted += OnSlideStarted;
        }

        private void OnDisable()
        {
            if (Motion != null)
            {
                Motion.Landed -= OnLanded;
                Motion.Jumped -= OnJumped;
                Motion.SlideStarted -= OnSlideStarted;
            }
            _sprintTween?.Kill();
            _impactSeq?.Kill();
            _sprintTween = null;
            _impactSeq = null;
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            var m = Motion;
            if (m == null) return;

            float aim = Mathf.Clamp01(m.AimWeight);
            float gate = 1f - aimSuppression * aim;

            // 달리기 가중치는 이징 곡선으로 — "무기를 내렸다/올렸다"가 또렷하게 읽힌다
            bool wantSprint = m.IsSprinting && m.AimWeight < 0.3f;
            if (wantSprint != _sprintActive)
            {
                _sprintActive = wantSprint;
                _sprintTween?.Kill();
                _sprintTween = DOTween
                    .To(() => _sprintWeight, v => _sprintWeight = v, wantSprint ? 1f : 0f,
                        wantSprint ? sprintEnterTime : sprintExitTime)
                    .SetEase(wantSprint ? Ease.OutCubic : Ease.OutQuad)
                    .SetLink(gameObject);
            }

            float slideW = Mathf.Clamp01(m.SlideWeight);
            // 슬라이딩 중에는 앉기 포즈가 중복되지 않도록 뺀다
            float crouchW = Mathf.Clamp01(m.CrouchWeight) * (1f - slideW);
            float sprintW = _sprintWeight * (1f - slideW);

            Vector3 targetPos = (sprintPosition * sprintW + crouchPosition * crouchW + slidePosition * slideW) * gate;
            Vector3 targetEuler = (sprintRotation * sprintW + crouchRotation * crouchW + slideRotation * slideW) * gate;

            _pos = MotionSpring.Damp(_pos, targetPos, sharpness, dt);
            _euler = MotionSpring.Damp(_euler, targetEuler, sharpness, dt);

            // 충격 펀치에도 조준 억제를 건다. 여기서(합성 시점에) 거는 이유는 펀치 도중 조준을
            // 시작/해제해도 그 순간의 조준 상태를 따라가야 하기 때문 — 펀치를 시작할 때 진폭에
            // 곱해 두면 점프 직후 조준한 경우 이미 커진 오프셋이 그대로 눈앞에서 흔들린다.
            float impactGate = 1f - impactAimSuppression * aim;

            PositionOffset = _pos + _impactPos * impactGate;
            RotationOffset = Quaternion.Euler(_euler + _impactEuler * impactGate);
        }

        // ── 사건 ────────────────────────────────────────────────────────────
        private void OnLanded(float impact)
        {
            impact = Mathf.Clamp01(impact);
            Punch(new Vector3(0f, -landDrop * impact, 0f), new Vector3(landPitch * impact, 0f, 0f), landAttack, landRelease);
        }

        private void OnJumped(float launchSpeed)
            => Punch(new Vector3(0f, jumpRise, 0f), new Vector3(-jumpRise * 90f, 0f, 0f), jumpAttack, jumpRelease);

        private void OnSlideStarted(float entrySpeed)
            => Punch(new Vector3(0f, 0f, -slidePunchZ), new Vector3(slidePunchPitch, 0f, 0f), 0.07f, 0.3f);

        private void Punch(Vector3 pos, Vector3 euler, float attack, float release)
        {
            _impactSeq?.Kill();

            var seq = DOTween.Sequence();
            seq.Append(DOTween.To(() => _impactPos, v => _impactPos = v, pos, attack).SetEase(Ease.OutQuad));
            seq.Join(DOTween.To(() => _impactEuler, v => _impactEuler = v, euler, attack).SetEase(Ease.OutQuad));
            seq.Append(DOTween.To(() => _impactPos, v => _impactPos = v, Vector3.zero, release).SetEase(Ease.OutBack));
            seq.Join(DOTween.To(() => _impactEuler, v => _impactEuler = v, Vector3.zero, release).SetEase(Ease.OutBack));
            seq.SetLink(gameObject);

            _impactSeq = seq;
        }
    }
}
