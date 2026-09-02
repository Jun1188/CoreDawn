using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using CoreDawn.FPS;
using CoreDawn.Inputs;
using CoreDawn.Worlds;
using CoreDawn.Data;
using InputEvent = CoreDawn.Inputs.InputEvent;
using CoreDawn.Sim;
using CoreDawn.Save;

namespace CoreDawn.UI
{
    /// <summary>
    /// 월드 맵 오버레이 (M키) — GameData 에디터 맵 탭과 같은 그림을 게임 안에서 그대로 보여준다:
    /// 지형(지면·강·절벽)과 배치물(나무·광맥·둥지·밤 진입로)은 타일 텍스처에 굽고,
    /// 그 위에 코어(초록 3×3)와 <b>플레이어 위치</b>(코어와 같은 사각 아이콘을 절반 크기·분홍으로)를
    /// 실시간으로 찍는다. 좌표 라벨은 마커 옆에 따라다닌다.
    ///
    /// 열기 = PlayerController(ToggleMap, Global 맵의 M) → GameScreens.OpenWorldMap().
    /// 닫기 = 이 팝업이 상위 우선순위에서 M을 가로채거나, ESC(UIPopup 기본 동작).
    ///
    /// 다른 UITK 패널과 달리 씬에 미리 실어 두지 않는다 — UXML 없이 코드로만 그리는 화면이라
    /// 처음 열릴 때 스스로 만든다. PanelSettings는 씬의 다른 UIDocument(GameUI)에서 빌린다.
    ///
    /// 빌드 의존성 주의: MapDataSO의 타일은 TileAt으로만 읽는다 — EditorTiles는 에디터 전용이다.
    /// </summary>
    public class WorldMapPanelView : UITKPopup
    {
        // ── 에디터 맵 탭(GdMapTab)과 같은 팔레트 — 두 그림이 다르면 에디터가 거짓말이 된다 ──
        static readonly Color32 GroundC = new(0x3E, 0x6B, 0x45, 255);
        static readonly Color32 RiverC  = new(0x1B, 0x4A, 0x6B, 255);
        static readonly Color32 CliffC  = new(0x3A, 0x2A, 0x2A, 255);
        static readonly Color32 TreeC   = new(0x4F, 0xBF, 0x6A, 255);
        static readonly Color32 NestC   = new(0xFF, 0x5D, 0x73, 255);
        static readonly Color32 NightC  = new(0xE8, 0xA5, 0x4B, 255);
        static readonly Color32 IronC   = new(0xE8, 0xA5, 0x4B, 255);
        static readonly Color32 CopperC = new(0x4F, 0xD8, 0xE0, 255);
        static readonly Color32 CrystalC= new(0xB4, 0x8C, 0xFF, 255);
        static readonly Color   CoreC   = new Color32(0x5D, 0xD3, 0x9E, 255);
        static readonly Color   PlayerC = new Color32(0xFF, 0x6E, 0xC7, 255);
        static readonly Color   EdgeC   = new Color32(0x2E, 0x42, 0x66, 255);
        static readonly Color   BgC     = new Color32(0x08, 0x0D, 0x16, 255);

        /// <summary>플레이어 마커 한 변(칸) — 코어(3칸)의 절반.</summary>
        const float PlayerMarkCells = 1.5f;

        static WorldMapPanelView cached;

        World world;
        Transform playerT;

        // ── UI 요소 ──
        VisualElement backdrop, mapBox, coreMark, playerMark;
        Image mapImg;
        Label coordLabel, titleLabel;
        Texture2D mapTex;
        IVisualElementScheduledItem tick;
        float k;   // 칸당 픽셀 — 백드롭 크기가 잡힌 뒤 계산된다

        // ───────────────────────── 열기 ─────────────────────────

        /// <summary>맵을 연다. World/UI가 준비되지 않은 씬이면 false — 호출부가 알린다.</summary>
        public static bool TryOpen()
        {
            if (cached == null)
                cached = FindFirstObjectByType<WorldMapPanelView>(FindObjectsInactive.Include);
            if (cached == null) cached = Create();
            if (cached == null) return false;

            if (!cached.isActiveAndEnabled)
                cached.gameObject.SetActive(true);
            return true;
        }

        static WorldMapPanelView Create()
        {
            var world = FindFirstObjectByType<World>();
            if (world == null || world.Map == null) return null;

            // PanelSettings는 씬에 이미 떠 있는 UITK 패널(GameUI 부트스트랩)에서 빌린다 —
            // 테마·스케일 정책이 다른 화면과 저절로 같아진다.
            PanelSettings settings = null;
            foreach (var doc in FindObjectsByType<UIDocument>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (doc.panelSettings != null) { settings = doc.panelSettings; break; }
            if (settings == null) return null;

            var go = new GameObject("WorldMapPanel");
            go.SetActive(false);   // OnEnable(=팝업 열림)은 TryOpen의 SetActive가 정한다
            var newDoc = go.AddComponent<UIDocument>();
            newDoc.panelSettings = settings;
            newDoc.sortingOrder = 100;   // HUD·다른 패널 위에 온다

            var view = go.AddComponent<WorldMapPanelView>();
            view.world = world;
            return view;
        }

        // ───────────────────── 입력 — M으로 닫기 ─────────────────────

        /// <summary>마우스를 쓰지 않는 오버레이 — 커서를 풀지 않아 시점 조작 상태가 유지된다.</summary>
        protected override bool ReleasesCursor => false;

        public override bool OnInput(in InputEvent e)
        {
            if (e.Phase == InputActionPhase.Performed && e.Id == InputActionId.ToggleMap)
            {
                Close();
                return true;
            }
            return base.OnInput(e);   // ESC 닫기 + 모달 소비
        }

        // ───────────────────── UITKPopup 계약 ─────────────────────

        protected override void Bind()
        {
            if (world == null) world = FindFirstObjectByType<World>();
            if (world == null || world.Map == null) { Close(); return; }

            BuildUiOnce();
            mapTex = BakeTexture(world.Map);
            mapImg.image = mapTex;

            Layout();
            UpdatePlayer();
            tick = mapBox.schedule.Execute(UpdatePlayer).Every(100);
        }

        protected override void Unbind()
        {
            tick?.Pause();
            tick = null;
            if (mapTex != null) { Destroy(mapTex); mapTex = null; }
            if (mapImg != null) mapImg.image = null;
        }

        // ───────────────────────── UI 구성 ─────────────────────────

        void BuildUiOnce()
        {
            if (backdrop != null) { Root.Add(backdrop); return; }   // 재활성 — 뼈대는 재사용

            backdrop = new VisualElement { style = {
                position = Position.Absolute, left = 0, right = 0, top = 0, bottom = 0,
                backgroundColor = new Color(0.016f, 0.027f, 0.047f, 0.78f),
                alignItems = Align.Center, justifyContent = Justify.Center } };
            Root.Add(backdrop);

            mapBox = new VisualElement { style = {
                backgroundColor = BgC,
                borderTopWidth = 1, borderBottomWidth = 1, borderLeftWidth = 1, borderRightWidth = 1,
                borderTopColor = EdgeC, borderBottomColor = EdgeC, borderLeftColor = EdgeC, borderRightColor = EdgeC } };
            backdrop.Add(mapBox);

            mapImg = new Image { scaleMode = ScaleMode.StretchToFill, style = {
                position = Position.Absolute, left = 0, right = 0, top = 0, bottom = 0 } };
            mapBox.Add(mapImg);

            coreMark = Mark(CoreC);
            playerMark = Mark(PlayerC);

            coordLabel = new Label { pickingMode = PickingMode.Ignore, style = {
                position = Position.Absolute, fontSize = 13, color = PlayerC,
                unityFontStyleAndWeight = FontStyle.Bold } };
            mapBox.Add(coordLabel);

            titleLabel = new Label { pickingMode = PickingMode.Ignore, style = {
                position = Position.Absolute, top = -26, left = 0, fontSize = 13,
                color = new Color(0.56f, 0.64f, 0.75f, 1f) } };
            mapBox.Add(titleLabel);

            // 창 크기가 바뀌면(해상도 전환 등) 칸당 픽셀을 다시 잡는다
            backdrop.RegisterCallback<GeometryChangedEvent>(_ => { Layout(); UpdatePlayer(); });
        }

        /// <summary>코어와 같은 생김새의 사각 마커 — 채움 + 어두운 테두리.</summary>
        VisualElement Mark(Color fill)
        {
            var v = new VisualElement { pickingMode = PickingMode.Ignore, style = {
                position = Position.Absolute, backgroundColor = fill,
                borderTopWidth = 1.5f, borderBottomWidth = 1.5f, borderLeftWidth = 1.5f, borderRightWidth = 1.5f,
                borderTopColor = BgC, borderBottomColor = BgC, borderLeftColor = BgC, borderRightColor = BgC } };
            mapBox.Add(v);
            return v;
        }

        /// <summary>백드롭 크기에서 칸당 픽셀을 정하고, 맵 상자와 고정 배치물(코어)을 앉힌다.</summary>
        void Layout()
        {
            var map = world.Map;
            var r = backdrop.contentRect;
            if (r.width < 10 || r.height < 10 || map.width <= 0 || map.height <= 0) return;

            k = Mathf.Min(r.width * 0.86f / map.width, r.height * 0.86f / map.height);
            mapBox.style.width = map.width * k;
            mapBox.style.height = map.height * k;

            // 에디터 맵 탭과 같은 방향 — 칸 (0,0)이 왼쪽 위, y는 아래로 자란다
            coreMark.style.left = map.core.x * k;
            coreMark.style.top = map.core.y * k;
            coreMark.style.width = 3 * k;
            coreMark.style.height = 3 * k;

            titleLabel.text = $"{world.Map.displayName}   ·   M / ESC 닫기";
        }

        // ───────────────────── 플레이어 마커 (100ms 틱) ─────────────────────

        void UpdatePlayer()
        {
            if (world == null) return;
            if (playerT == null) playerT = FindFirstObjectByType<PlayerController>()?.transform;

            if (playerT == null || k <= 0f)
            {
                playerMark.style.display = DisplayStyle.None;
                coordLabel.style.display = DisplayStyle.None;
                return;
            }

            float cell = Mathf.Max(0.0001f, world.CellSize);
            Vector3 local = playerT.position - world.Origin;
            float px = local.x / cell, py = local.z / cell;

            float s = PlayerMarkCells * k;
            playerMark.style.display = DisplayStyle.Flex;
            playerMark.style.left = px * k - s * 0.5f;
            playerMark.style.top = py * k - s * 0.5f;
            playerMark.style.width = s;
            playerMark.style.height = s;

            coordLabel.style.display = DisplayStyle.Flex;
            coordLabel.text = $"({Mathf.FloorToInt(px)}, {Mathf.FloorToInt(py)})";
            coordLabel.style.left = px * k + s * 0.5f + 6;
            coordLabel.style.top = py * k - 9;
        }

        // ───────────────────── 타일 텍스처 굽기 ─────────────────────

        /// <summary>
        /// 1타일 = 1픽셀, 포인트 필터 — 에디터 맵 탭과 같은 방식이라 확대해도 칸이 또렷하다.
        /// 배치물(나무·광맥·둥지·밤 진입로)은 움직이지 않으므로 함께 굽는다.
        /// </summary>
        static Texture2D BakeTexture(MapDataSO map)
        {
            int w = map.width, h = map.height;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            var px = new Color32[w * h];

            // 화면은 칸 (0,0)이 왼쪽 위(y 아래로) — 에디터 맵 탭과 같은 방향
            int Idx(int x, int y) => (h - 1 - y) * w + x;

            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    px[Idx(x, y)] = map.TileAt(x, y) switch
                    {
                        MapTile.River => RiverC,
                        MapTile.Cliff => CliffC,
                        _ => GroundC,
                    };

            void Put(int x, int y, Color32 c) { if (map.InBounds(x, y)) px[Idx(x, y)] = c; }

            if (map.trees != null)
                foreach (var t in map.trees) Put(t.x, t.y, TreeC);

            if (map.nodes != null)
                foreach (var n in map.nodes)
                {
                    Put(n.cell.x, n.cell.y, NodeColor(SaveRefs.Item(n.itemId)));   // 광맥은 한 칸짜리
                }

            if (map.nightSpawnPoints != null)
                foreach (var p in map.nightSpawnPoints) Put(p.x, p.y, NightC);

            if (map.nests != null)
                foreach (var n in map.nests) Put(n.cell.x, n.cell.y, NestC);

            tex.SetPixels32(px);
            tex.Apply(false);
            return tex;
        }

        static Color32 NodeColor(ItemDef item) => item != null ? item.Line switch
        {
            ItemLine.Copper  => CopperC,
            ItemLine.Crystal => CrystalC,
            _                => IronC,
        } : IronC;
    }
}
