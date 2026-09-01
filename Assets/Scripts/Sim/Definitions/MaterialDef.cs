namespace CoreDawn.Sim
{
    /// <summary>
    /// 재질 한 종 — 팩 <c>materials</c> 섹션 한 항목(id <c>coredawn:material/…</c>). 게임 값은 없고 표현(view)뿐이다:
    /// <c>shader</c>(내장 셰이더 이름 — 셰이더는 코드처럼 게임에 내장), <c>textures{프로퍼티: {file, linear}}</c>(팩 png), <c>colors</c>·<c>vectors</c>·<c>floats</c>·<c>keywords</c>·<c>renderQueue</c>·<c>tags</c>.
    /// 모델(glb)은 재질 슬롯(인덱스)만 갖고, 정의의 <c>view.model[i].materials[슬롯]</c>이 이 id를 가리킨다. 심은 id의 존재만 안다.
    /// </summary>
    public sealed class MaterialDef : Def
    {
    }
}
