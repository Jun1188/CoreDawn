using System;
using System.Collections.Generic;
using UnityEngine;
using CoreDawn.Managers;

namespace CoreDawn.Title
{
    public enum TitlePieceType { Straight, CurveL, CurveR, Splitter, Branch, Ship }

    /// <summary>
    /// 경로 조각 — 경로 위(직선·곡선·분배기)는 경로 거리 <see cref="S0"/>·<see cref="Len"/>을 갖고,
    /// 경로 밖(분기 벨트·우주선)은 <see cref="S0"/>만 정리 기준으로 쓴다.
    /// </summary>
    public sealed class TitlePiece
    {
        public TitlePieceType Type;
        public Vector2 Entry;          // 그룹 로컬(x, z) 입구 점
        public Vector2Int H, HOut;     // 입구·출구 헤딩
        public float S0, Len;
        public GameObject Obj;
        public Animation Anim;
        public bool OnPath => Type != TitlePieceType.Branch && Type != TitlePieceType.Ship;
    }

    /// <summary>타일 템플릿(팩 glb) — 복제하고 재질 슬롯을 꽂고 벨트 루프 클립을 튼다.</summary>
    public sealed class TitleTiles
    {
        public GameObject Straight, CurveL, CurveR, Splitter, Ship;
        public IReadOnlyList<string> BeltMaterials, ShipMaterials;

        public GameObject Instantiate(TitlePieceType type, Transform parent, out Animation anim)
        {
            var template = type switch
            {
                TitlePieceType.CurveL => CurveL,
                TitlePieceType.CurveR => CurveR,
                TitlePieceType.Splitter => Splitter,
                TitlePieceType.Ship => Ship,
                _ => Straight,
            };
            var go = UnityEngine.Object.Instantiate(template, parent);
            go.name = type.ToString();
            go.SetActive(true);
            PackAssets.BindSlots(go, type == TitlePieceType.Ship ? ShipMaterials : BeltMaterials, "TitleBeltScene");
            anim = go.GetComponentInChildren<Animation>(true);
            if (anim != null && type != TitlePieceType.Ship) PlayLoop(anim);
            return go;
        }

        // 벨트 glb 의 유일한 클립(모프 애니)을 무작위 위상으로 반복 — 조각마다 같은 위상이면 벨트가 한 몸처럼 보인다
        static void PlayLoop(Animation anim)
        {
            string name = null;
            foreach (AnimationState st in anim) { name = st.name; break; }
            if (name == null) return;
            var s = anim[name];
            s.wrapMode = WrapMode.Loop;
            anim.wrapMode = WrapMode.Loop;
            anim.Play(name);
            s.time = UnityEngine.Random.value * 3f;
        }
    }

    /// <summary>
    /// 랜덤 S자 벨트 경로 — 레퍼런스 <c>path.js</c> 이식. 헤딩은 격자 방향(N=+Z, E=+X).
    /// 벨트 glb 는 회전 0 에서 +X 로 흐르고 곡선 L/R 은 서쪽에서 받아 북/남으로 뚫려 있다(에디터 실측, BeltGeometry 규약과 같음).
    /// 조각은 그룹(<see cref="Group"/>) 아래에 로컬 좌표로 놓이고, 그룹의 요가 벨트 전체 기울기다.
    /// </summary>
    public sealed class TitleBeltPath
    {
        public static readonly Vector2Int N = new(0, 1), E = new(1, 0), S = new(0, -1), W = new(-1, 0);
        public static Vector2Int LeftOf(Vector2Int h) => new(-h.y, h.x);
        public static Vector2Int RightOf(Vector2Int h) => new(h.y, -h.x);
        /// <summary>+X 로 흐르는 모델을 헤딩 <paramref name="h"/> 로 돌리는 요(도).</summary>
        public static float YawOf(Vector2Int h) => Mathf.Atan2(-h.y, h.x) * Mathf.Rad2Deg;

        readonly TitleTiles tiles;
        public readonly Transform Group;
        public readonly float Tile, R;
        public readonly List<TitlePiece> Pieces = new();

        // 생성기 상태 — 다음 조각의 입구·헤딩·경로 거리, 계획된 조각 타입, 좌우 치우침
        Vector2 genE;
        Vector2Int genH = N;
        float genS;
        readonly List<TitlePieceType> plan = new();
        int lat;

        public TitleBeltPath(TitleTiles tiles, Transform group, float tile)
        {
            this.tiles = tiles;
            Group = group;
            Tile = tile;
            R = tile / 2f;
        }

        public Vector3 ToWorld(Vector2 local) => Group.TransformPoint(new Vector3(local.x, 0f, local.y));

        // 다음 계획: 직진 2~4칸 → 옆으로 한 번 꺾고 0~1칸 → 복귀. lat 로 좌우 치우침을 보정한다
        TitlePieceType NextPlan()
        {
            if (plan.Count == 0)
            {
                int k = 2 + UnityEngine.Random.Range(0, 3);
                for (int i = 0; i < k; i++) plan.Add(TitlePieceType.Straight);
                bool right = UnityEngine.Random.value < 0.62f - lat * 0.35f;
                int run = UnityEngine.Random.value < 0.5f ? 0 : 1;
                lat += (right ? 1 : -1) * (run + 1);
                plan.Add(right ? TitlePieceType.CurveR : TitlePieceType.CurveL);
                for (int i = 0; i < run; i++) plan.Add(TitlePieceType.Straight);
                plan.Add(right ? TitlePieceType.CurveL : TitlePieceType.CurveR);
            }
            var t = plan[0];
            plan.RemoveAt(0);
            return t;
        }

        void AddPiece()
        {
            var type = NextPlan();
            var h = genH; var e = genE; float T = Tile;
            var obj = tiles.Instantiate(type, Group, out var anim);
            obj.transform.localRotation = Quaternion.Euler(0f, YawOf(h), 0f);
            obj.transform.localPosition = new Vector3(e.x + h.x * T / 2f, 0f, e.y + h.y * T / 2f);
            var hOut = h; float len = T; var exit = e + (Vector2)h * T;
            if (type != TitlePieceType.Straight)
            {
                hOut = type == TitlePieceType.CurveL ? LeftOf(h) : RightOf(h);
                len = Mathf.PI * R / 2f;
                exit = e + ((Vector2)h + (Vector2)hOut) * R;
            }
            Pieces.Add(new TitlePiece { Type = type, Entry = e, H = h, HOut = hOut, S0 = genS, Len = len, Obj = obj, Anim = anim });
            genS += len; genE = exit; genH = hOut;
        }

        public void Ensure(float s) { while (genS < s) AddPiece(); }

        /// <summary>경로 뒤쪽 정리. <paramref name="keep"/>(piece) 가 true 면 남긴다.</summary>
        public void Prune(float minS, Func<TitlePiece, bool> keep)
        {
            for (int i = Pieces.Count - 1; i >= 0; i--)
            {
                var q = Pieces[i];
                if (keep != null && keep(q)) continue;
                if (q.S0 + q.Len < minS) { UnityEngine.Object.Destroy(q.Obj); Pieces.RemoveAt(i); }
            }
        }

        /// <summary>벨트 루프 클립 배속 — 0 이면 멈춤(도킹 중).</summary>
        public void SetAnimSpeed(float speed)
        {
            foreach (var p in Pieces)
            {
                if (p.Anim == null || p.Type == TitlePieceType.Ship) continue;
                foreach (AnimationState st in p.Anim) st.speed = speed;
            }
        }

        /// <summary>경로 거리 → 월드 좌표(y=0).</summary>
        public Vector3 PointAt(float s)
        {
            TitlePiece p = null, last = null;
            foreach (var q in Pieces)
            {
                if (!q.OnPath) continue;
                last = q;
                if (s >= q.S0 && s < q.S0 + q.Len) { p = q; break; }
            }
            p ??= last;
            if (p == null) return Group.position;
            float t = Mathf.Clamp01((s - p.S0) / p.Len);
            if (p.Type == TitlePieceType.Straight || p.Type == TitlePieceType.Splitter)
                return ToWorld(p.Entry + (Vector2)p.H * (Tile * t));
            var c = p.Entry + (Vector2)p.HOut * R;
            float th = t * Mathf.PI / 2f;
            return ToWorld(c + R * (-(Vector2)p.HOut * Mathf.Cos(th) + (Vector2)p.H * Mathf.Sin(th)));
        }

        /// <summary><paramref name="index"/> 이후를 잘라내고 그 지점부터 다시 생성한다(진행 방향이 N 이 아니면 복귀 회전 먼저).</summary>
        public void TruncateFrom(int index)
        {
            var first = Pieces[index];
            for (int i = Pieces.Count - 1; i >= index; i--) UnityEngine.Object.Destroy(Pieces[i].Obj);
            Pieces.RemoveRange(index, Pieces.Count - index);
            genE = first.Entry; genH = first.H; genS = first.S0;
            plan.Clear(); plan.AddRange(ReturnPlan(first.H)); lat = 0;
        }

        static IEnumerable<TitlePieceType> ReturnPlan(Vector2Int h)
        {
            if (h == N) yield break;
            yield return h == E ? TitlePieceType.CurveL : TitlePieceType.CurveR;
        }

        /// <summary>랜덤 패턴 재개(옆으로 가던 중이면 북쪽 복귀 먼저).</summary>
        public void ResumeRandom() { plan.Clear(); plan.AddRange(ReturnPlan(genH)); lat = 0; }

        public void ForceStraight(int n) { for (int i = 0; i < n; i++) plan.Add(TitlePieceType.Straight); }

        /// <summary>직선 조각을 분배기로 갈아 끼운다.</summary>
        public void ToSplitter(TitlePiece p)
        {
            var pos = p.Obj.transform.localPosition; var rot = p.Obj.transform.localRotation;
            UnityEngine.Object.Destroy(p.Obj);
            var obj = tiles.Instantiate(TitlePieceType.Splitter, Group, out _);
            obj.transform.localPosition = pos; obj.transform.localRotation = rot;
            p.Obj = obj; p.Anim = null; p.Type = TitlePieceType.Splitter;
        }

        /// <summary>경로 밖 조각 등록(분기 벨트·우주선) — 정리 기준은 <paramref name="s0"/>.</summary>
        public TitlePiece AddAside(TitlePieceType type, GameObject obj, Animation anim, float s0)
        {
            obj.transform.SetParent(Group, false);
            var q = new TitlePiece { Type = type, S0 = s0, Len = Tile, Obj = obj, Anim = anim };
            Pieces.Add(q);
            return q;
        }
    }
}
