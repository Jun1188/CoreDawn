namespace CoreDawn.Sim
{
    /// <summary>
    /// 소리 한 종 — 팩 <c>sounds</c> 섹션 한 항목(id <c>coredawn:sound/…</c>). 게임 값은 없고 표현(view.clips — 변형 클립 묶음,
    /// 재생 때 하나를 무작위로 고른다)뿐이다. 볼륨·공간감은 소리의 성질이 아니라 <b>쓰는 자리</b>의 값이라 여기 없다 —
    /// 쓰는 자리(view.sfx의 SoundUse, 팩 최상위 sfx)가 <c>{sound, volume, spatial}</c>로 적는다(EffectSpec ↔ EffectUse와 같은 구분).
    /// 심은 id의 존재만 안다.
    /// </summary>
    public sealed class SoundDef : Def
    {
    }
}
