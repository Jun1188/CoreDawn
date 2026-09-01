using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;
using CoreDawn.Entities;
using CoreDawn.Sim;
using CoreDawn.FPS;
using CoreDawn.Factory;
using CoreDawn.UI;
using CoreDawn.Data;
using CoreDawn.Visuals;

namespace CoreDawn.Combat
{
    /// <summary>
    /// 발사 한 번의 명세 — 발사체가 어떻게 날고(속도·수명·사거리), 명중 시 무엇을 하는가(효과 목록).
    /// 배율(공격 버프·발사기 배율)은 발사 시점에 목록에 구워져 확정된다:
    /// 총알이 날아가는 동안 버프가 끝나도 발사 때 배율이 유지된다.
    /// </summary>
    public readonly struct ProjectileShot
    {
        public readonly float Speed;
        public readonly float Lifetime;
        public readonly float Range;
        public readonly int TargetMask;        // 효과를 적용할 레이어. 0 = 판정 없는 연출탄 — 스윕도 생략(트레이서)
        public readonly Effect[] Effects;      // 명중 시 무슨 일이 일어나는가 — 심 효과, 배율이 이미 구워진 최종 목록
        public readonly EntityView Source;         // 발사자 — 효과 출처이자 자기 명중 무시 기준
        public readonly float Gravity;         // 낙하 가속 — 0이면 직선탄, >0이면 포물선 (탄약의 성질)
        public readonly float ExplosionRadius; // 착탄 폭발 반경 — 0이면 단일 명중, >0이면 착탄점 Pulse
        public readonly FireMode Mode;         // 전달 방식 — 발사기(GunDef·TurretModuleDef)가 정한다
        public readonly GameObject Prefab;     // 탄 외형(탄약의 bulletPrefab) — Projectile은 판정하는 몸, Hitscan은 트레이서 연출
        public readonly int Pierce;            // 추가 관통 대상 수 — 0이면 첫 대상에서 멈춤 (탄약의 성질)
        public readonly Vector3 Muzzle;        // 연출 출발점(총구) — FromMuzzle일 때만 유효
        public readonly bool FromMuzzle;       // true면 탄이 총구에서 출발해 조준선 1m 지점에 합류 후 직진 (FPS 뷰모델용)
        public readonly GameObject HitEffect;  // 착탄/폭발 지점에서 재생할 파티클 (탄약의 hitEffectPrefab)

        public ProjectileShot(float speed, float lifetime, float range,
                              Effect[] effects, int targetMask, EntityView source,
                              float gravity = 0f, float explosionRadius = 0f,
                              FireMode mode = FireMode.Projectile, GameObject prefab = null,
                              int pierce = 0, Vector3? muzzle = null, GameObject hitEffect = null)
        {
            Speed = speed;
            Lifetime = lifetime;
            Range = range;
            Effects = effects;
            TargetMask = targetMask;
            Source = source;
            Gravity = gravity;
            ExplosionRadius = explosionRadius;
            Mode = mode;
            Prefab = prefab;
            Pierce = pierce;
            FromMuzzle = muzzle.HasValue;
            Muzzle = muzzle ?? default;
            HitEffect = hitEffect;
        }
    }
}
