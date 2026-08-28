using System;
using UnityEngine;

namespace CoreDawn.Entities
{
    /// <summary>
    /// 최대 체력 씨드 — 프리팹 인스펙터에 인라인 직렬화되는 값 홀더. 런타임 체력은 여기 없다.
    ///
    /// 구 HealthComponent(체력·사망 로직)는 심 <see cref="CoreDawn.Sim.Health"/>로 갔다(리팩토링 2단계).
    /// 클래스·필드 이름을 그대로 둔 이유: 몬스터·둥지·타워 프리팹이 <c>health.maxHealth</c> 경로로 값을 들고 있어서
    /// 형이나 이름을 바꾸면 그 값이 전부 날아간다. 3·4단계에서 최대 체력이 데이터(SO)로 옮겨가면 이 클래스도 사라진다.
    /// </summary>
    [Serializable]
    public class HealthComponent
    {
        [SerializeField] private float maxHealth = 100f;

        public float MaxHealth => maxHealth;
    }
}
