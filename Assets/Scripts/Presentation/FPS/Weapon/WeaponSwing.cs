using DG.Tweening;
using UnityEngine;

namespace CoreDawn.FPS
{
    /// <summary>
    /// 휘두르기 — 근접무기의 <b>이산</b> 사건을 담당하는 무기 모션 모듈.
    /// 총의 킥백(<see cref="WeaponKickback"/>)이 "뒤로 밀렸다 돌아오는" 스프링이라면, 이쪽은
    /// "당겼다가 크게 그어 내리는" 호(arc)다 — 근접무기에 킥백을 크게 주면 찌르는 것도 베는 것도
    /// 아닌 이상한 반동이 된다.
    ///
    /// <see cref="WeaponStancePose"/>와 같은 구조: 이징이 있는 DOTween 시퀀스로 가중치를 몰고,
    /// 결과는 오프셋으로만 노출한다. 합성은 <see cref="WeaponMotionManager"/>가 한 번에 한다 —
    /// transform을 직접 만지면 스웨이·ADS·킥백과 매 프레임 싸운다.
    ///
    /// 수치는 무기가 준다(<see cref="GunData"/>의 swing* 필드). 여기는 곡선만 안다.
    /// </summary>
    public class WeaponSwing : MonoBehaviour, IWeaponMotionModule
    {
        [Header("구간 비율 (전체 시간을 1로 봤을 때)")]
        [Tooltip("본 스윙(휘두르는 순간)이 차지하는 비율. 짧을수록 채찍처럼 빠르게 벤다. " +
                 "나머지는 되감기·정점 멈춤·복귀가 나눠 갖는다.")]
        [Range(0.15f, 0.8f)] public float strikeRatio = 0.28f;

        [Header("동작 형태")]
        [Tooltip("되감기에서 반대 방향으로 당기는 비율 — 예비 동작의 크기.")]
        [Range(0f, 1f)] public float windupPull = 0.5f;
        [Tooltip("정점에서 멈추는 한 박자(전체 시간 비율). 예비 없는 스윙은 채찍이 아니라 문지르기다.")]
        [Range(0f, 0.15f)] public float windupHold = 0.05f;
        [Tooltip("본 스윙이 목표를 지나치는 배율 — 관성의 팔로스루. 복귀 구간에서 제자리로 풀린다.")]
        [Range(1f, 1.5f)] public float strikeOvershoot = 1.18f;

        [Header("안전 상한")]
        [Tooltip("스윙 회전 누적이 이 값(도)을 넘지 않도록 제한.")]
        public float maxRotation = 90f;

        public Vector3 PositionOffset { get; private set; }
        public Quaternion RotationOffset { get; private set; } = Quaternion.identity;

        // 시퀀스가 몰고 다니는 값 — 오프셋은 이걸 그대로 내보낸다
        private Vector3 _pos;
        private Vector3 _euler;
        private Sequence _seq;
        private bool _flip;   // 좌우 교대용

        /// <summary>
        /// 한 번 휘두른다 — <see cref="WeaponManager"/>가 발사(=휘두름) 순간에 무기 수치로 부른다.
        /// 진행 중에 다시 불리면 지금 자세에서 이어 시작한다(연사 스윙이 뚝 끊겨 보이지 않게).
        /// </summary>
        public void Swing(float duration, Vector3 rotation, Vector3 position, float windup, bool alternate)
        {
            if (duration <= 0f) return;

            // 좌우 교대 — 요(좌우)와 롤(비틀림)만 뒤집는다. 피치까지 뒤집으면 위로 퍼올리는 스윙이 된다.
            if (alternate && _flip)
            {
                rotation = new Vector3(rotation.x, -rotation.y, -rotation.z);
                position = new Vector3(-position.x, position.y, position.z);
            }
            if (alternate) _flip = !_flip;

            rotation = Vector3.ClampMagnitude(rotation, maxRotation);
            // 팔로스루가 상한을 넘지 않게 오버슈트 목표도 따로 죈다
            Vector3 through = Vector3.ClampMagnitude(rotation * strikeOvershoot, maxRotation);

            float windupTime = duration * Mathf.Clamp01(windup);
            float holdTime = duration * windupHold;
            float strikeTime = duration * strikeRatio;
            float returnTime = Mathf.Max(0.01f, duration - windupTime - holdTime - strikeTime);

            _seq?.Kill();
            _seq = DOTween.Sequence().SetUpdate(false);   // 일시정지(timeScale 0) 중에는 멈춘다

            // ① 되감기 — 어깨 뒤로 당기는 예비 동작. 부드럽게 들어가 정점에서 한 박자 멈춘다.
            //    이 멈춤이 다음 구간의 속도를 "터짐"으로 읽히게 만든다.
            if (windupTime > 0.001f)
            {
                _seq.Append(DOTween.To(() => _euler, v => _euler = v, -rotation * windupPull, windupTime).SetEase(Ease.InOutSine));
                _seq.Join(DOTween.To(() => _pos, v => _pos = v, -position * 0.35f, windupTime).SetEase(Ease.InOutSine));
                if (holdTime > 0.001f) _seq.AppendInterval(holdTime);
            }

            // ② 본 스윙 — 시작이 가장 빠르다(명중 판정이 이 순간이다). 관성으로 목표를
            //    지나쳐(팔로스루) 뻗고, 몸은 앞으로 살짝 딸려 나간다(OutBack = 런지 후 제동).
            _seq.Append(DOTween.To(() => _euler, v => _euler = v, through, strikeTime).SetEase(Ease.OutQuart));
            _seq.Join(DOTween.To(() => _pos, v => _pos = v, position, strikeTime).SetEase(Ease.OutBack));

            // ③ 복귀 — 뻗은 팔이 이완되며 제자리로. 급하게 돌아오면 베기가 아니라 튕김으로 보인다.
            _seq.Append(DOTween.To(() => _euler, v => _euler = v, Vector3.zero, returnTime).SetEase(Ease.InOutSine));
            _seq.Join(DOTween.To(() => _pos, v => _pos = v, Vector3.zero, returnTime).SetEase(Ease.InOutSine));
        }

        private void Update()
        {
            PositionOffset = _pos;
            RotationOffset = Quaternion.Euler(_euler);
        }

        // 무기를 집어넣거나 죽었을 때 휘두르던 자세가 굳어 남지 않게 한다
        private void OnDisable()
        {
            _seq?.Kill();
            _seq = null;
            _pos = Vector3.zero;
            _euler = Vector3.zero;
            PositionOffset = Vector3.zero;
            RotationOffset = Quaternion.identity;
        }
    }
}
