namespace CoreDawn.Sim
{
    /// <summary>
    /// 사망 드롭 — 정의(<see cref="LootModuleDef"/>)를 드는 얇은 모듈. 무엇을 떨굴지는 심이 알고,
    /// 바닥에 실물을 뿌리는 것(월드 좌표·프리팹)은 게임(LootSpawner)이 <c>EntityWorld.Died</c>를 듣고 한다.
    /// </summary>
    public sealed class LootModule : EntityModule
    {
        public LootModuleDef Def { get; }
        public LootModule(LootModuleDef def) { Def = def; }
    }
}
