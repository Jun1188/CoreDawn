using System.Collections.Generic;
using UnityEngine;
using CoreDawn.Factory;
using CoreDawn.Managers;
using CoreDawn.Sim;

namespace CoreDawn.Title
{
    public sealed class TitleItem
    {
        public float S;              // 경로 거리
        public Transform Tr;
        public MeshFilter Mf;
        public MeshRenderer Mr;
        public Material FaceMat;     // 아이콘 면 — 흐림(tint)을 위해 아이템마다 복제
        public ItemDef Kind;
        public int Link = -1;        // 연결된 메뉴 버튼 index
        public bool Dim;
        public bool Branch;          // 분기 벨트 위(분배기 → 우주선)
        public float BS;             // 분기 거리
        public Vector3 Position => Tr.position;
    }

    /// <summary>
    /// 벨트 위 아이템 — 레퍼런스 <c>items.js</c>(정육면체)를 인게임 아이템으로 바꾼 것(2026-09-07 사용자 지시).
    /// 게임의 벨트 아이템과 같은 눕힌 아이콘 판(<see cref="ItemSlabMesh"/>)이고, 종류는 팩 아이템 정의:
    /// 기본은 <see cref="neutral"/>(철판), 버튼에 연결되면 그 버튼의 아이템(<see cref="linked"/>: 시작·설정·종료)으로 바뀐다.
    /// 흐림(설정 열림 등)은 면 재질 tint 로.
    /// </summary>
    public sealed class TitleItems
    {
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly Color TintLinked = Color.white;
        static readonly Color TintNeutral = new Color(0.85f, 0.85f, 0.85f, 1f);
        static readonly Color TintDim = new Color(0.45f, 0.45f, 0.45f, 1f);

        readonly Transform parent;
        readonly float tile, beltTop, width, thickness;
        readonly ItemDef neutral;
        readonly ItemDef[] linked;
        public readonly List<TitleItem> List = new();

        /// <param name="width">아이콘 판의 가로 크기(월드)</param>
        /// <param name="thickness">판 두께(월드)</param>
        public TitleItems(Transform parent, float tile, float beltTop, float width, float thickness, ItemDef neutral, ItemDef[] linked)
        {
            this.parent = parent; this.tile = tile; this.beltTop = beltTop; this.width = width; this.thickness = thickness;
            this.neutral = neutral; this.linked = linked;
        }

        public TitleItem Spawn(float s)
        {
            var go = new GameObject("Item");
            go.transform.SetParent(parent, false);
            var it = new TitleItem { S = s, Tr = go.transform, Mf = go.AddComponent<MeshFilter>(), Mr = go.AddComponent<MeshRenderer>() };
            it.Mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            SetKind(it, neutral);
            Tint(it);
            List.Add(it);
            return it;
        }

        // 아이콘 → 판 메시(스프라이트 단위) → 가로가 width 가 되게 스케일. 두께는 월드 값을 스케일로 나눠 스프라이트 단위로
        void SetKind(TitleItem it, ItemDef def)
        {
            if (def == null || it.Kind == def) return;
            it.Kind = def;
            var icon = PackAssets.IconOf(def);
            var probe = ItemSlabMesh.Of(icon, 0.1f);
            if (probe == null) { Debug.LogError($"[TitleItems] '{def.Id}' 아이콘 판을 만들지 못했습니다."); return; }
            float scale = width / Mathf.Max(1e-4f, probe.bounds.size.x);
            it.Mf.sharedMesh = ItemSlabMesh.Of(icon, thickness / scale);
            it.Tr.localScale = Vector3.one * scale;
            if (it.FaceMat != null) Object.Destroy(it.FaceMat);
            it.FaceMat = new Material(ItemSlabMesh.FaceMaterial(icon)) { hideFlags = HideFlags.DontSave };
            it.Mr.sharedMaterials = new[] { it.FaceMat, ItemSlabMesh.SideMaterial() };
        }

        void Tint(TitleItem it)
        {
            if (it.FaceMat == null) return;
            it.FaceMat.SetColor(BaseColor, it.Dim ? TintDim : it.Link >= 0 ? TintLinked : TintNeutral);
        }

        /// <summary>버튼 <paramref name="index"/>에 연결(-1 이면 해제) — 종류와 밝기가 따라 바뀐다.</summary>
        public void SetLink(TitleItem it, int index)
        {
            it.Link = index;
            SetKind(it, index >= 0 && index < linked.Length && linked[index] != null ? linked[index] : neutral);
            Tint(it);
        }

        public void SetDim(TitleItem it, bool dim) { it.Dim = dim; Tint(it); }

        public void Remove(TitleItem it)
        {
            if (it.Tr != null) { Object.Destroy(it.FaceMat); Object.Destroy(it.Tr.gameObject); }
            List.Remove(it);
        }

        public void Clear()
        {
            foreach (var it in List) if (it.Tr != null) { Object.Destroy(it.FaceMat); Object.Destroy(it.Tr.gameObject); }
            List.Clear();
        }

        /// <summary>한 줄 일정 간격 배치 — 앵커 근처 3개가 버튼과 연결된다.</summary>
        public void SeedRow(float anchorS, float gap)
        {
            for (int k = -5; k <= 9; k++) Spawn(anchorS + tile * (-0.6f + k * gap));
        }

        public List<TitleItem> Near(float anchorS)
        {
            var r = new List<TitleItem>();
            foreach (var x in List) if (x.S > anchorS - tile * 0.9f && x.S < anchorS + tile * 1.9f) r.Add(x);
            return r;
        }

        public void Place(TitleItem it, Vector3 p) => it.Tr.position = new Vector3(p.x, beltTop + thickness * 0.5f, p.z);

        // 판은 XZ 에 눕고 아이콘 위쪽이 +Z — 게임과 같이 흐르는 방향을 본다
        public void Place(TitleItem it, Vector3 p, Vector3 q)
        {
            Place(it, p);
            var d = new Vector3(q.x - p.x, 0f, q.z - p.z);
            if (d.sqrMagnitude > 1e-8f) it.Tr.rotation = Quaternion.LookRotation(d, Vector3.up);
        }

        public void SetVisible(TitleItem it, bool visible) { if (it.Mr != null) it.Mr.enabled = visible; }

        public void ResetAll()
        {
            foreach (var x in List) { x.Dim = false; SetLink(x, -1); SetVisible(x, true); }
        }
    }
}
