using Newtonsoft.Json;

namespace CoreDawn.Sim
{
    /// <summary>
    /// 포탑 — 조준 사격. 표적·선회·정렬·리드·쿨다운·탄 소비의 값. 효과는 탄(AmmoConsumer)이 정한다.
    /// 오라(AuraEmitter)는 이것의 파생이 아니라 별개 모듈 — "타워"는 모듈이 아니라 Building + AmmoConsumer + (Turret | AuraEmitter | Trigger)의 조합이다.
    /// </summary>
    public sealed class TurretModuleDef : EntityModuleDef
    {
        [JsonProperty("range")] public float Range = 8f;              // m — 플레이어 총과 같은 단위
        [JsonProperty("minRange")] public float MinRange;             // m — 이보다 가까운 적은 못 겨눈다(박격포의 사각). 0 = 제한 없음
        [JsonProperty("fireRate")] public float FireRate = 1f;        // 발/초
        [JsonProperty("turnSpeed")] public float TurnSpeed = 180f;    // 도/초. 0 = 즉시 조준
        [JsonProperty("aimTolerance")] public float AimTolerance = 5f; // 도 — 이 안에 들어와야 쏜다(좌우만)
        [JsonProperty("preferHighArc")] public bool PreferHighArc;    // 중력탄을 고각으로 — 박격포는 장애물을 넘겨 쏜다
        [JsonProperty("muzzleHeight")] public float MuzzleHeight = 1f; // m — 심의 총구 높이(엔티티 위치 기준)
        [JsonProperty("aimHeight")] public float AimHeight = 0.6f;    // m — 표적 위치(발)에서 겨누는 높이
        [JsonProperty("hitscan")] public bool Hitscan;                // 즉시 판정(레이저·저격) — 리드·탄도 없음

        public override EntityModule Create(Entity entity) => new TurretModule(this);
    }
}
