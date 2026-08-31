namespace CoreDawn.Sim
{
    /// <summary>
    /// 무기 소지자(플레이어) — 총마다의 탄창 상태와 재장전·연사 판정을 심에 둔다. 값은 없다(총의 수치는 GunDef, 탄창은 Inventory).
    /// 행동이 있으므로 모듈이다(정체성 마커가 아니다): "지금 쏠 수 있나"의 판정과 탄 소비가 여기서 일어난다.
    /// </summary>
    public sealed class WeaponModuleDef : EntityModuleDef
    {
        public override EntityModule Create(Entity entity) => new WeaponModule();
    }
}
