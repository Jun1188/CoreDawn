using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;
using CoreDawn.Data;
using CoreDawn.Managers;
using CoreDawn.Sim;

namespace CoreDawn.Title
{
    /// <summary>
    /// 타이틀 3D 배경 — 레퍼런스 <c>main.js</c> 이식. 흘러가는 랜덤 벨트 위 아이템, 돌리 카메라, 그리고 "게임 시작" 시퀀스
    /// (화면 밖부터 직진으로 재생성 → 분배기 → 오른쪽 분기 벨트 → 우주선 착륙 → 도킹 → 이륙).
    /// 모델·재질은 팩의 belt·splitter·core 정의(view.model)에서 읽는다 — 게임과 같은 glb·FactoryColor.
    /// 메뉴(<see cref="UI.TitleScreenView"/>)는 <see cref="TryGetLinkTarget"/>·<see cref="ShipAnchorsSorted"/>로 와이어 끝점을 묻고,
    /// <see cref="StartLaunch"/>·<see cref="ReturnFromDock"/>·<see cref="Takeoff"/>로 시퀀스를 민다.
    /// </summary>
    [DefaultExecutionOrder(50)]
    public sealed class TitleBeltScene : MonoBehaviour
    {
        public const int LinkCount = 3;
        const string BeltId = "coredawn:entity/belt", SplitterId = "coredawn:entity/splitter", CoreId = "coredawn:entity/core";

        [Header("벨트 / 아이템 (칸 단위)")]
        [SerializeField] float beltSpeed = 1f;        // 아이템 속도(칸/초) — 벨트 애니와 동기
        [SerializeField] float beltAnimSpeed = 2f;    // 벨트 모프 클립 배속
        [SerializeField] float itemSize = 0.32f;      // 아이콘 판 가로(칸 대비)
        [SerializeField] float itemThickness = 0.03f; // 판 두께(칸 대비)
        [SerializeField] float itemGap = 1.1f;

        [Header("아이템(팩 id) — 기본은 벨트 위 모두, 나머지는 버튼에 연결된 것(시작·설정·종료 순)")]
        [SerializeField] string neutralItem = "coredawn:item/iron_plate";
        [SerializeField] string[] linkItems = { "coredawn:item/refined_crystal", "coredawn:item/iron_gear", "coredawn:item/beast_core" };
        [SerializeField] float beltYaw = -20f;        // 벨트 전체 기울기(왼쪽 20°)

        [Header("카메라")]
        [SerializeField] Camera cam;
        [SerializeField] float fieldOfView = 38f;
        [SerializeField] Vector3 camOffset = new(-0.3f, 5.2f, -3.2f);   // 시선 목표 기준(칸)
        [SerializeField] float lookShift = -1.3f;     // 시선 목표를 왼쪽으로 밀어 벨트를 우측에
        [SerializeField] float dolly = 0.14f, dollyPeriod = 11f;
        [SerializeField] float launchZoom = 0.62f, dockZoom = 1.1f, launchSpeed = 1.4f;

        [Header("발사")]
        [SerializeField] int branchTiles = 4;         // 분배기 → 우주선 분기 벨트 칸수
        [SerializeField] float coreTiles = 3.2f;      // 우주선 길이(칸)

        [Header("빛 / 색")]
        [SerializeField] Light sun;
        [SerializeField] Color background = new Color32(0x07, 0x0d, 0x1a, 255);
        [SerializeField] Color sunColor = new Color32(0xff, 0xf4, 0xe2, 255);
        [SerializeField] float sunIntensity = 1.3f;
        [SerializeField] Color ambientSky = new Color32(0xdf, 0xf3, 0xff, 255);
        [SerializeField] Color ambientEquator = new Color32(0x8f, 0xb8, 0xd8, 255);
        [SerializeField] Color ambientGround = new Color32(0x2a, 0x3a, 0x5c, 255);
        [SerializeField, Range(0f, 3f)] float ambientIntensity = 0.45f;   // 1.2 면 아이템까지 하얗게 뜬다(실측)

        public bool Ready { get; private set; }
        public bool LoadFailed { get; private set; }
        /// <summary>모델 로드 진행(읽은 수, 전체) — 타이틀 씬을 바로 재생했을 때(Boot 를 안 거침) 로딩 상자가 보여준다.</summary>
        public (int done, int total) LoadProgress { get; private set; }
        /// <summary>지금 읽는 파일 이름. 다 읽으면 "READY".</summary>
        public string Loading { get; private set; } = "INIT";
        public bool Launching => lActive;
        public bool Arrived => lArrived;
        public bool Docked => lDocked;
        /// <summary>뷰가 정한다 — 설정이 열렸거나 전환 중이면 연결 아이템을 흐리게.</summary>
        public bool DimOthers { get; set; }
        public Camera Cam => cam;

        public event Action ArrivedEvent, DockedEvent;

        TitleTiles tiles;
        TitleBeltPath path;
        TitleItems items;
        Transform group, itemsRoot;
        float tile = 1f, beltTop;
        float camS;
        Vector3 camPos, camTgt;
        bool camInited;
        readonly TitleItem[] links = new TitleItem[LinkCount];

        // 발사 상태
        bool lActive, lArrived, lDocked;
        float lZoom = 1f, lLen, dockTimer = -1f;
        TitleItem lItem;
        TitlePiece lPiece;
        Vector2 lCenter;
        Vector2Int lDir;
        GameObject ship;
        Animation coreAnim;
        Vector3[] anchors, anchorsSorted;
        Vector3[] anchorLocal;   // 착륙 완료 자세 기준 앵커(우주선 로컬) — 배치 때 한 번 계산
        /// <summary>디버그 — 마지막으로 잰 우주선 경계(월드, 로컬).</summary>
        public Bounds LastShipBounds { get; private set; }

        /// <summary>우주선 위 앵커 3개(긴 축의 앞·가운데·뒤)를 화면 위→아래 순으로.</summary>
        public Vector3[] ShipAnchorsSorted
        {
            get
            {
                if (anchors == null) return null;
                if (anchorsSorted == null)
                {
                    anchorsSorted = (Vector3[])anchors.Clone();
                    Array.Sort(anchorsSorted, (a, b) => cam.WorldToViewportPoint(b).y.CompareTo(cam.WorldToViewportPoint(a).y));
                }
                return anchorsSorted;
            }
        }

        /// <summary>메인 메뉴 버튼 <paramref name="i"/> 가 가리키는 아이템의 월드 위치. 발사 중에는 0번만 발사 아이템.</summary>
        public bool TryGetLinkTarget(int i, out Vector3 pos)
        {
            var it = lActive ? (i == 0 ? lItem : null) : links[i];
            if (it == null || it.Tr == null) { pos = default; return false; }
            pos = it.Position;
            return true;
        }

        // ───────────────────────── 준비 ─────────────────────────

        async void Start()
        {
            if (cam == null) cam = Camera.main;
            ApplyRenderSettings();

            var db = SimHost.Database;
            if (db == null) { Fail("팩 정의가 없습니다."); return; }
            if (!TryModel(db, BeltId, v => v.Model, out var belt) | !TryModel(db, BeltId, v => v.ModelCurveL, out var curveL)
                | !TryModel(db, BeltId, v => v.ModelCurveR, out var curveR) | !TryModel(db, SplitterId, v => v.Model, out var splitter)
                | !TryModel(db, CoreId, v => v.Model, out var core))
            { Fail("belt·splitter·core 정의의 view.model 을 읽지 못했습니다."); return; }

            var refs = new[] { belt, curveL, curveR, splitter, core };
            var tasks = new Task<GameObject>[refs.Length];
            for (int i = 0; i < refs.Length; i++) tasks[i] = PackAssets.LoadModelAsync(db.Pack, refs[i].File);
            var m = new GameObject[refs.Length];
            LoadProgress = (0, refs.Length);
            try
            {
                for (int i = 0; i < tasks.Length; i++)   // 순서대로 기다리며 진행도만 센다(로드는 이미 전부 시작됨)
                {
                    Loading = System.IO.Path.GetFileName(refs[i].File);
                    m[i] = await tasks[i];
                    LoadProgress = (i + 1, refs.Length);
                }
            }
            catch (Exception e) { Debug.LogException(e, this); m = null; }
            if (this == null) return;
            Loading = "READY";
            if (m == null || Array.Exists(m, x => x == null)) { Fail("glb 를 읽지 못했습니다."); return; }

            tiles = new TitleTiles
            {
                Straight = m[0], CurveL = m[1], CurveR = m[2], Splitter = m[3], Ship = m[4],
                BeltMaterials = belt.Materials, ShipMaterials = core.Materials,
            };
            var bb = BoundsOf(m[0]);
            tile = Mathf.Max(bb.size.x, bb.size.z);
            beltTop = bb.max.y;

            group = new GameObject("BeltPath").transform;
            group.SetParent(transform, false);
            group.localRotation = Quaternion.Euler(0f, beltYaw, 0f);
            itemsRoot = new GameObject("Items").transform;
            itemsRoot.SetParent(transform, false);

            var neutralDef = db.Item(neutralItem);
            if (neutralDef == null) { Fail($"아이템 '{neutralItem}' 이 팩에 없습니다."); return; }
            var linkDefs = new ItemDef[LinkCount];
            for (int i = 0; i < LinkCount; i++)
            {
                string id = i < linkItems.Length ? linkItems[i] : null;
                linkDefs[i] = string.IsNullOrEmpty(id) ? null : db.Item(id);
                if (linkDefs[i] == null) Debug.LogError($"[TitleBeltScene] 연결 아이템 {i} '{id}' 이 팩에 없습니다 — 기본 아이템으로 둡니다.", this);
            }

            path = new TitleBeltPath(tiles, group, tile);
            items = new TitleItems(itemsRoot, tile, beltTop, tile * itemSize, tile * itemThickness, neutralDef, linkDefs);
            camS = tile * 6f;
            path.Ensure(camS + tile * 22f);
            RenderSettings.fogStartDistance = tile * 11f;
            RenderSettings.fogEndDistance = tile * 24f;
            Ready = true;
        }

        static bool TryModel(SimDatabase db, string id, Func<EntityViewDef, IReadOnlyList<ModelRef>> pick, out ModelRef model)
        {
            model = null;
            var def = db.Entity(id);
            if (def == null) { Debug.LogError($"[TitleBeltScene] 정의 '{id}' 가 팩에 없습니다."); return false; }
            var list = pick(ViewSchema.Entity(def));
            if (list == null || list.Count == 0 || string.IsNullOrEmpty(list[0].File)) { Debug.LogError($"[TitleBeltScene] '{id}': view.model 이 비었습니다."); return false; }
            model = list[0];
            return true;
        }

        void Fail(string why)
        {
            LoadFailed = true;
            Ready = true;   // 메뉴는 3D 없이도 떠야 한다 — 로딩 가림막을 내린다
            Debug.LogError($"[TitleBeltScene] 타이틀 배경을 세우지 못했습니다 — {why}", this);
        }

        void ApplyRenderSettings()
        {
            if (cam != null)
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = background;
                cam.fieldOfView = fieldOfView;
                cam.nearClipPlane = 0.1f;
                cam.farClipPlane = 200f;
            }
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = background;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = ambientSky * ambientIntensity;
            RenderSettings.ambientEquatorColor = ambientEquator * ambientIntensity;
            RenderSettings.ambientGroundColor = ambientGround * ambientIntensity;
            if (sun == null)
            {
                var go = new GameObject("Sun");
                go.transform.SetParent(transform, false);
                sun = go.AddComponent<Light>();
                sun.type = LightType.Directional;
                sun.shadows = LightShadows.Soft;
            }
            sun.color = sunColor;
            sun.intensity = sunIntensity;
        }

        static Bounds BoundsOf(GameObject go)
        {
            bool first = true; Bounds b = default;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds);
            }
            return b;
        }

        // ───────────────────────── 프레임 ─────────────────────────

        void Update()
        {
            if (!Ready || LoadFailed || path == null || items == null) return;   // path/items 가 비면(플레이 중 도메인 리로드) 조용히 쉰다
            float dt = Mathf.Min(Time.deltaTime, 0.05f), time = Time.time;
            float speedK = lActive ? launchSpeed : 1f;
            float v = beltSpeed * tile * speedK;
            bool paused = lArrived;   // 도킹 중엔 벨트 흐름 정지
            if (!paused) camS += v * dt;

            path.Ensure(camS + tile * 22f);
            path.Prune(camS - tile * 6f, q => (lActive || lDocked) && (q.Type == TitlePieceType.Branch || q.Type == TitlePieceType.Ship || q.Type == TitlePieceType.Splitter));
            path.SetAnimSpeed(paused ? 0f : beltAnimSpeed * speedK);

            if (items.List.Count == 0) items.SeedRow(camS, itemGap);
            foreach (var it in items.List.ToArray())
            {
                if (it.Branch)   // 분기 벨트 위(분배기 → 우주선)
                {
                    it.BS = Mathf.Min(it.BS + v * dt, lLen);
                    // 분기 방향을 보게 — 위치만 옮기면 북쪽을 본 채 동쪽으로 간다(2026-09-07 사용자 지적)
                    items.Place(it, path.ToWorld(lCenter + (Vector2)lDir * it.BS), path.ToWorld(lCenter + (Vector2)lDir * (it.BS + 0.02f)));
                    if (it.BS >= lLen && !lArrived) OnArrive();
                    continue;
                }
                if (!paused) it.S += v * dt;
                if (it.S > camS + tile * 12f || it.S < camS - tile * 8f) { items.Remove(it); continue; }
                if (lActive && it == lItem && it.S >= lPiece.S0 + lPiece.Len / 2f) { it.Branch = true; it.BS = 0f; continue; }
                items.Place(it, path.PointAt(it.S), path.PointAt(it.S + 0.02f));
            }

            UpdateLinks();
            UpdateCamera(dt, time);

            if (dockTimer >= 0f)
            {
                dockTimer -= dt;
                if (dockTimer < 0f) { lDocked = true; DockedEvent?.Invoke(); }
            }
        }

        // 버튼 ↔ 아이템 링크: 앵커 근처의 앞선 아이템부터 위쪽 버튼에(선 교차 방지).
        // 발사 중에는 재배정·재정렬을 멈춘다 — 발사 아이템이 분기로 빠지면 순서가 바뀌어 다른 아이템의 종류까지 갈아끼워졌다
        // (레퍼런스는 색만 바뀌어 눈에 안 띄었지만 여기서는 크리스탈·기어·코어가 이동 중에 변한다, 2026-09-07 사용자 지적)
        void UpdateLinks()
        {
            bool dimOthers = DimOthers || lActive;
            int keep = lActive ? 0 : 1;   // 흐리게 하지 않는 자리 — 발사 아이템 / 설정 아이템
            for (int i = 0; i < links.Length; i++)
            {
                var it = links[i];
                if (it != null && !items.List.Contains(it)) { it.Link = -1; links[i] = null; it = null; }
                if (it != null && i != keep && it.Dim != dimOthers) items.SetDim(it, dimOthers);
                if (lActive) continue;
                if (it == null)
                {
                    TitleItem cand = null;
                    foreach (var x in items.Near(camS)) if (x.Link < 0 && (cand == null || x.S > cand.S)) cand = x;
                    if (cand != null) { items.SetLink(cand, i); links[i] = cand; }
                }
            }
            if (lActive) return;
            var sorted = new List<TitleItem>();
            foreach (var l in links) if (l != null) sorted.Add(l);
            sorted.Sort((a, b) => b.S.CompareTo(a.S));
            for (int i = 0; i < links.Length; i++)
            {
                links[i] = i < sorted.Count ? sorted[i] : null;
                if (links[i] != null && links[i].Link != i) items.SetLink(links[i], i);   // 앞선 아이템 = 위쪽 버튼 — 종류도 따라간다
            }
        }

        void UpdateCamera(float dt, float time)
        {
            Vector3 look;
            if (lArrived && anchors != null) look = new Vector3(anchors[1].x, 0f, anchors[1].z);
            else
            {
                var focus = new List<TitleItem>();
                if (lActive) { if (lItem != null) focus.Add(lItem); }
                else foreach (var l in links) if (l != null) focus.Add(l);
                if (focus.Count > 0)
                {
                    var sum = Vector3.zero;
                    foreach (var f in focus) sum += f.Position;
                    look = sum / focus.Count; look.y = 0f;
                }
                else look = path.PointAt(camS + tile * 0.6f);
            }
            float targetZoom = lArrived ? dockZoom : lActive ? launchZoom : 1f;
            lZoom += (targetZoom - lZoom) * Mathf.Min(1f, dt * 1.2f);
            float dist = lActive ? lZoom : 1f + dolly * Mathf.Sin(time * Mathf.PI * 2f / dollyPeriod);
            look.x += lookShift * tile * dist;
            var want = look + camOffset * (tile * dist);
            if (!camInited) { camPos = want; camTgt = look; camInited = true; }
            float k = 1f - Mathf.Exp(-dt * 2.2f);
            camPos = Vector3.Lerp(camPos, want, k);
            camTgt = Vector3.Lerp(camTgt, look, k);
            cam.transform.position = camPos;
            cam.transform.LookAt(camTgt);
            if (sun != null)
            {
                sun.transform.position = camTgt + new Vector3(6f, 12f, -4f) * (tile * 0.6f);
                sun.transform.LookAt(camTgt);
            }
        }

        // ───────────────────────── 게임 시작 시퀀스 ─────────────────────────

        /// <summary>화면 밖 첫 조각부터 북쪽 직진으로 재생성 → 분배기 → 오른쪽 분기 벨트 → 우주선 착륙. 시작 못 하면 false.</summary>
        public bool StartLaunch()
        {
            if (!Ready || LoadFailed || lActive || links[0] == null) return false;
            var it = links[0];
            var pcs = path.Pieces;

            bool Offscreen(TitlePiece q)
            {
                foreach (float s in new[] { q.S0 + 0.001f, q.S0 + q.Len - 0.001f })
                {
                    var p = path.PointAt(s); p.y = beltTop;
                    var vp = cam.WorldToViewportPoint(p);
                    if (vp.z > 0f && Mathf.Abs(vp.x * 2f - 1f) <= 1.15f && Mathf.Abs(vp.y * 2f - 1f) <= 1.15f) return false;
                }
                return true;
            }

            int cut = -1;
            for (int idx = 0; idx < pcs.Count && cut < 0; idx++)
            {
                var q = pcs[idx];
                if (q.S0 < it.S + tile * 1.2f) continue;
                bool all = true;
                for (int j = idx; j < pcs.Count; j++)
                {
                    var r = pcs[j];
                    if (r.S0 < q.S0 || Offscreen(r)) continue;
                    all = false; break;
                }
                if (all) cut = idx;
            }
            float cutS = cut >= 0 ? pcs[cut].S0 : it.S + tile * 5f;
            if (cut >= 0) path.TruncateFrom(cut);
            path.ForceStraight(16);
            path.Ensure(it.S + tile * 16f);

            TitlePiece p = null;
            foreach (var q in pcs) if (q.Type == TitlePieceType.Straight && q.H == TitleBeltPath.N && q.S0 >= cutS) { p = q; break; }
            if (p == null) return false;

            lActive = true; lItem = it; lPiece = p;

            // 분배기 + 오른쪽 분기 벨트
            path.ToSplitter(p);
            var c = p.Entry + (Vector2)p.H * (tile / 2f);
            var d = TitleBeltPath.RightOf(p.H);
            lCenter = c; lDir = d;
            for (int k = 1; k <= branchTiles; k++)
            {
                var obj = tiles.Instantiate(TitlePieceType.Straight, group, out var anim);
                obj.transform.localPosition = new Vector3(c.x + d.x * tile * k, 0f, c.y + d.y * tile * k);
                obj.transform.localRotation = Quaternion.Euler(0f, TitleBeltPath.YawOf(d), 0f);
                path.AddAside(TitlePieceType.Branch, obj, anim, p.S0);
            }
            lLen = tile * (branchTiles + 0.8f);
            PlaceShip(c, d, p);
            return true;
        }

        // 우주선: 착륙 포즈 기준으로 크기·위치를 맞춘 뒤 착륙 클립을 처음부터
        void PlaceShip(Vector2 c, Vector2Int d, TitlePiece p)
        {
            ship = tiles.Instantiate(TitlePieceType.Ship, group, out coreAnim);
            var ce = c + (Vector2)d * (tile * (branchTiles + 1.6f));
            ship.transform.localPosition = new Vector3(ce.x, 0f, ce.y);
            // 레퍼런스 yawOf(d)+π: 긴 축(X)이 분기 방향과 직각으로 서고 모델 -Z 가 벨트 쪽을 본다 — 유니티(+X 가 d 를 보는 YawOf)로는 +90°
            ship.transform.localRotation = Quaternion.Euler(0f, TitleBeltPath.YawOf(d) + 90f, 0f);
            ship.transform.localScale = Vector3.one;
            path.AddAside(TitlePieceType.Ship, ship, coreAnim, p.S0);
            if (coreAnim == null) { Debug.LogError("[TitleBeltScene] core.glb 에 Animation 이 없습니다 — 착륙 연출 생략.", this); return; }

            var land = FindClip(coreAnim, "land");
            if (land == null) return;
            var st = coreAnim[land];
            st.wrapMode = WrapMode.ClampForever;
            coreAnim.wrapMode = WrapMode.ClampForever;
            coreAnim.Play(land);
            st.time = st.length - 0.01f;
            coreAnim.Sample();

            var bb = ShipBounds();
            float scale = tile * coreTiles / Mathf.Max(bb.size.x, bb.size.z, 1e-3f);
            ship.transform.localScale = Vector3.one * scale;
            bb = ShipBounds();
            var target = path.ToWorld(ce);
            ship.transform.position += new Vector3(target.x - bb.center.x, -bb.min.y, target.z - bb.center.z);

            // 앵커 3개(긴 축 = 로컬 X 의 앞/중앙/뒤, 높이는 꼭대기의 85%)는 착륙 완료 자세로 — 도착 시점엔 아직 내려오는 중이라
            // 그때 자세로 재면 공중을 가리킨다(레퍼런스도 바인드 자세 경계를 써서 착륙 지점을 가리킨다). 길이는 정한 값(coreTiles)
            bb = ShipBounds();
            anchorLocal = new Vector3[3];
            float[] ks = { -0.3f, 0f, 0.3f };
            for (int i = 0; i < 3; i++)
            {
                var a = bb.center + ship.transform.right * (ks[i] * tile * coreTiles);
                a.y = bb.max.y * 0.85f;
                anchorLocal[i] = ship.transform.InverseTransformPoint(a);
            }

            st.time = 0f;
            coreAnim.Sample();
        }

        static string FindClip(Animation anim, string contains)
        {
            string first = null;
            foreach (AnimationState s in anim)
            {
                first ??= s.name;
                if (s.name.IndexOf(contains, StringComparison.OrdinalIgnoreCase) >= 0) return s.name;
            }
            return first;
        }

        void OnArrive()
        {
            lArrived = true;
            if (lItem != null) items.SetVisible(lItem, false);
            ArrivedEvent?.Invoke();

            // 우주선 위 3개 앵커 — 배치 때 착륙 자세로 계산해 둔 로컬 좌표를 월드로(우주선 루트는 움직이지 않는다)
            anchors = new Vector3[3];
            for (int i = 0; i < 3; i++) anchors[i] = anchorLocal != null ? ship.transform.TransformPoint(anchorLocal[i]) : ship.transform.position;
            anchorsSorted = null;
            dockTimer = 0.7f;
        }

        /// <summary>뒤로가기(뷰가 서브 메뉴 소멸 뒤 부른다) — 카메라를 아이템 흐름으로 복귀, 분기·우주선은 뒤로 흘러가며 정리.</summary>
        public void ReturnFromDock()
        {
            lDocked = false; lArrived = false; lActive = false; dockTimer = -1f;
            anchors = null; anchorsSorted = null; anchorLocal = null; coreAnim = null; lPiece = null; ship = null;
            if (lItem != null) { items.Remove(lItem); lItem = null; }
            foreach (var q in path.Pieces) if (q.Type == TitlePieceType.Branch || q.Type == TitlePieceType.Ship) q.S0 = camS;
            var rest = new List<TitleItem>();
            foreach (var x in items.List) if (!x.Branch) rest.Add(x);
            rest.Sort((a, b) => a.S.CompareTo(b.S));
            if (rest.Count > 0) camS = rest[rest.Count / 2].S - tile * 0.5f;
            items.ResetAll();
            for (int i = 0; i < links.Length; i++) links[i] = null;
            if (items.Near(camS).Count < 3) { items.Clear(); items.SeedRow(camS, itemGap); }
            path.ResumeRandom();
            path.Ensure(camS + tile * 22f);
        }

        /// <summary>새 게임 — 이륙 클립 재생. 페이드·씬 전환은 뷰가.</summary>
        public void Takeoff()
        {
            if (coreAnim == null) return;
            var off = FindClip(coreAnim, "take");
            if (off == null) return;
            coreAnim.Stop();
            coreAnim[off].wrapMode = WrapMode.ClampForever;
            coreAnim.Play(off);
        }

        /// <summary>우주선 렌더러 경계(월드). BakeMesh 정점은 리그 공간 단위(길이 55)라 못 쓴다(2026-09-07 실측) — 렌더러 경계가 실제 크기(길이 2.8)를 준다.</summary>
        Bounds ShipBounds()
        {
            var b = BoundsOf(ship);
            LastShipBounds = b;
            return b;
        }
    }
}
