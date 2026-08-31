using Newtonsoft.Json;

namespace CoreDawn.Sim
{
    /// <summary>
    /// 둥지 — 복구 일수. 스폰 포인트의 자리·보스 유무는 맵(NestSpec)이, 보스·방어자의 종류(프리팹)는 아직 뷰가 갖는다(5a-3 카탈로그로).
    /// 런타임 <see cref="NestModule"/>이 자리의 파괴/복구·둥지의 파괴/복구·무적 규칙·보스 사망 감지를 심에서 끝낸다.
    /// </summary>
    public sealed class NestModuleDef : EntityModuleDef
    {
        [JsonProperty("bossRecoveryDays")] public int BossRecoveryDays = 2;
        [JsonProperty("nestRecoveryDays")] public int NestRecoveryDays = 3;

        public override EntityModule Create(Entity entity) => new NestModule(this);
    }
}
