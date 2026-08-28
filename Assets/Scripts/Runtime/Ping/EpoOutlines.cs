using EPOOutline;
using UnityEngine;

namespace CoreDawn.Pings
{
    /// <summary>
    /// EPO(Easy Performant Outline) 아웃라인을 오브젝트에 붙이는 공용 절차 — 핑·몬스터 근접 표시가 같이 쓴다.
    /// 그리는 쪽(<c>Outliner</c>)은 플레이어 카메라에 있으므로 여기서는 대상 쪽(<see cref="Outlinable"/>)만 다룬다.
    /// </summary>
    public static class EpoOutlines
    {
        /// <summary>
        /// 루트에 Outlinable을 보장한다 — 없으면 만들어 메시 종류 렌더러를 등록한다(파티클·트레일 제외:
        /// 이펙트에 테두리가 번진다). 새로 만든 것은 꺼진 채로 돌려준다 — 켜는 것은 호출자의 판단이다.
        /// </summary>
        public static Outlinable Ensure(GameObject root)
        {
            if (root == null) return null;

            var o = root.GetComponent<Outlinable>();
            if (o != null) return o;

            o = root.AddComponent<Outlinable>();
            o.AddAllChildRenderersToRenderingList(RenderersAddingMode.SkinnedMeshRenderer | RenderersAddingMode.MeshRenderer);
            o.RenderStyle = RenderStyle.Single;
            o.DrawingMode = OutlinableDrawingMode.Normal;
            o.enabled = false;
            return o;
        }

        /// <summary>색·두께·번짐을 한 번에. 렌더 스타일이 Single일 때의 파라미터를 만진다.</summary>
        public static void Style(Outlinable o, Color color, float dilateShift, float blurShift)
        {
            if (o == null) return;
            var p = o.OutlineParameters;
            p.Enabled = true;
            p.Color = color;
            p.DilateShift = dilateShift;
            p.BlurShift = blurShift;
        }
    }
}
