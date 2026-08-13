using UnityEngine;

/// <summary>
/// 탄약 모듈 — 이 아이템이 무기·포탑의 탄으로 소모될 수 있고, 어떻게 날아가
/// 명중 시 무슨 일이 일어나는지를 정의한다 (구 AmmoItemSO의 대체).
///
/// 명중 효과를 탄약이 갖고 포탑이 배율을 갖는 이유: 탄약이 강해지면 그 탄을 쓰는
/// 모든 포탑이 함께 강해져야 한다. 포탑의 damageMultiplier는 피해형(Damage·DoT)
/// 항목에만 곱해진다 — 감속 같은 부가 효과는 그대로.
///
/// <b>탄도(속도·중력·폭발·수명·외형)도 탄의 물리적 성질이라 여기에 있다</b> —
/// 발사기는 각도(조준)·연사·배율·소비만 정한다. 같은 유탄은 유탄발사기에서 쏘든
/// 박격포에서 쏘든 같은 속도로 포물선을 그리며 같은 반경으로 터진다.
/// </summary>
public class AmmoModuleSO : ItemModuleSO
{
    [Tooltip("이 탄약 1발이 명중 시 일으키는 일 — 피해도 항목의 하나다: {Damage, 10}.")]
    public EffectEntry[] attackEffects;

    [Header("탄도 (탄의 물리적 성질 — 어느 발사기에서 쏘든 같다)")]
    [Tooltip("탄속(m/s). Hitscan 발사기에서는 무시된다 — 즉시 판정.")]
    public float speed = 50f;

    [Tooltip("낙하 가속(m/s²). 0 = 직선탄, 9.8 = 포물선(유탄). 곡사 조준각은 발사기 몫.")]
    public float gravity;

    [Tooltip("착탄 폭발 반경(m). 0 = 명중한 하나에게만, >0 = 착탄점 반경 전원에게(Pulse). " +
             "수명이 다해도 그 자리에서 터진다.")]
    public float explosionRadius;

    [Tooltip("최대 비행 시간(초). Hitscan에서는 무시된다.")]
    public float lifetime = 3f;

    [Tooltip("탄 외형 프리팹(Bullet 컴포넌트 필수) — 에셋 참조라 json 밖, 인스펙터에서 배선한다. " +
             "Projectile 발사기가 이 탄을 쓸 때만 필요.")]
    public GameObject bulletPrefab;

    /// <summary>피해 항목들의 value 합 — 툴팁 표기용 (전투 계산엔 쓰지 않는다).</summary>
    public float BaseDamage
    {
        get
        {
            float sum = 0f;
            if (attackEffects != null)
                foreach (var e in attackEffects)
                    if (e.effect is DamageEffectSO) sum += e.value;
            return sum;
        }
    }
}
