using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using CoreDawn.Combat;
using CoreDawn.Sim;
using CoreDawn.Entities;
using CoreDawn.Factory;
using CoreDawn.Interaction;
using CoreDawn.ResourceNodes;
using CoreDawn.Save;
using CoreDawn.Data;

namespace CoreDawn.Worlds
{
    /// <summary>
    /// 맵 데이터를 씬의 실물로 세운다 — 광맥·둥지·밤 진입로.
    ///
    /// <b>왜 씬에 미리 놓지 않는가.</b> 이것들의 위치와 성질은 전부 맵이 정한다
    /// (<see cref="MapDataSO"/>의 nodes·nests·nightSpawnPoints). 씬에 박아두면 맵을 갈아끼울 때
    /// 배치가 따라오지 않아 "새 맵인데 옛 둥지가 서 있는" 상태가 된다. 맵이 곧 배치이므로,
    /// 배치도 맵에서 만들어져야 한다.
    ///
    /// 생성물은 월드 아래 <c>Spawned</c> 노드에 모은다 — 지형(Terrain (Generated))과 같은 원칙으로,
    /// 다시 만들 때 통째로 지우고 새로 세운다.
    /// </summary>
    public static class WorldPopulator
    {
        const string RootName = "Spawned";


        /// <summary>
        /// 맵대로 세우고, 밤 웨이브의 진입로를 담은 제공자를 돌려준다.
        ///
        /// 배치물은 전부 여기서 세운다 — 씬에는 굳히지 않는다(5a-4c). 코어는 FactoryBootstrap.AutoPlaceCore가
        /// 맵의 core 칸에 세운다(GameBootstrap이 주입). 에디터 미리보기는 WorldPreviewDrawer가 GameObject 없이 그린다.
        /// </summary>
        public static NightSpawnPointProvider Populate(World world, Transform battleRoot)
        {
            if (world == null || world.Map == null) return null;

            // 씬에는 배치물이 없다(5a-4c: 굳히기 퇴역 — 씬은 지형·조명만, 배치의 정본은 맵). 에디터 미리보기는 WorldPreviewDrawer가 그린다.
            var old = world.transform.Find(RootName);
            if (old != null) Debug.LogError($"[WorldPopulator] 씬에 옛 굳힌 배치물 루트('{RootName}')가 남아 있습니다 — 지우세요(런타임이 다시 세웁니다).", old);
            if (!Managers.PackAssets.IsReady) Debug.LogError("[WorldPopulator] 팩 자원 preload 전에 배치하려 했습니다 — 부팅 씬(Boot)을 거쳐야 합니다(GameBootstrap이 우회시킨다).", world);

            var root = new GameObject(RootName).transform;
            root.SetParent(world.transform, false);

            int nodes = PlaceNodes(world, root);
            int nests = PlaceNests(world, root);
            int startingDrops = PlaceStartingDrops(world, root);
            var provider = PlaceNightSpawns(world, root, battleRoot);
            // 나무는 광맥·둥지 다음에 세운다 — 겹친 나무가 그것들을 밀어내면 안 된다.
            int trees = PlaceTrees(world, root);

            Debug.Log($"[WorldPopulator] '{world.Map.Id}' 배치 — 광맥 {nodes} · 둥지 {nests} · " +
                      $"시작 아이템 {startingDrops} · 밤 진입로 {(provider != null ? provider.SpawnPoints.Count : 0)} · " +
                      $"나무 {trees}", world);
            return provider;
        }

        /// <summary>런타임에 갓 세운 나무를 심에 잇는다(굳어 있지 않은 씬 경로).</summary>
        static void ConnectTree(BuildingView view, EntityDef def, Vector2Int cell)
            => PlacementBridge.PlaceExisting(def, cell, 0, view);

        // ── 시작 드롭 아이템 ───────────────────────────────────────

        // 시작 잔해가 흩어지는 범위 — 코어 중심에서 몇 칸까지인가. 1.5칸이 3×3 코어의 가장자리다
        // (cellSize 4 기준 중심 6~12m = 코어 벽에 바싹 붙어 시작). 링을 좁힐 때는 아래 MinGap과
        // 수용량이 맞물린다 — 자리가 모자라면 12회 재시도를 소진한 잔해가 조용히 생략된다.
        const float StartDropMinRing = 1.5f;
        const float StartDropMaxRing = 3f;

        /// <summary>같은 아이템은 이 거리 안에서 스택으로 합쳐진다(트리거 센서) — 그보다 넉넉히 띄운다.</summary>
        const float StartDropMinGap = 3.5f;

        /// <summary>
        /// 시작 잔해를 코어 주변에 흩는다.
        ///
        /// 사양은 맵 데이터(<c>MapDataSO.startItems</c> — "무엇을 몇 개")다. 위치는 여기서 코어 둘레에 정한다.
        /// 복원 중이면 아예 만들지 않는다 — 저장된 바닥 아이템을 곧 되살릴 참이고, 그것이 이 잔해의 현재 상태다.
        ///
        /// 흩는 방식: 낱개로 쪼개 원주를 균등 분할하고 각도·반지름에 지터를 준다. 무작위지만
        /// 뭉치지 않는다 — 순수 난수는 반드시 몇 개가 붙고, 붙으면 스택으로 합쳐져 하나가 된다.
        /// </summary>
        static int PlaceStartingDrops(World world, Transform root)
        {
            // 1) 사양 — 맵 데이터(MapDataSO.startItems: 무엇을 몇 개). 씬 마커(DroppedItem 프리팹 인스턴스)는 퇴역(5a-4c).
            var items = new List<ItemDef>();
            if (world.Map.startItems != null)
                foreach (var spec in world.Map.startItems)
                {
                    var def = SaveRefs.Item(spec.itemId);
                    if (def == null) { Debug.LogError($"[WorldPopulator] 시작 잔해 '{spec.itemId}'이 팩에 없습니다.", world); continue; }
                    for (int k = 0; k < spec.amount; k++) items.Add(def);
                }
            int total = items.Count;
            if (total <= 0) return 0;
            if (SaveLoadContext.IsRestoring) return 0;

            // 2) 코어 중심 — 코어는 3×3이라 원점 칸에서 1.5칸이 한가운데다(World의 기즈모와 같은 식)
            Vector3 center = world.CellToWorld(world.Map.core)
                           + new Vector3(1.5f, 0f, 1.5f) * world.CellSize;

            var placedPoints = new List<Vector3>(total);
            float step = 360f / total;
            float baseAngle = Random.Range(0f, 360f);

            for (int i = 0; i < total; i++)
            {
                // 자기 몫의 각도 구간 안에서만 흔든다(±40%) — 이웃과 겹칠 여지를 남기지 않는다
                float angle = baseAngle + step * i + Random.Range(-step * 0.4f, step * 0.4f);

                // 지형(강·절벽)이나 이웃과의 간격 때문에 실패하면 조금씩 비틀어 다시 시도
                for (int attempt = 0; attempt < 12; attempt++)
                {
                    float radius = Random.Range(StartDropMinRing, StartDropMaxRing) * world.CellSize;
                    float rad = (angle + attempt * 6f) * Mathf.Deg2Rad;
                    Vector3 pos = center + new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad)) * radius;

                    if (!IsOpenGround(world, pos)) continue;
                    if (TooClose(placedPoints, pos)) continue;

                    // 살짝 띄워 떨어뜨린다 — 지형이 칸마다 조금씩 높낮이가 있어 파묻히지 않게
                    var drop = DroppedItem.Spawn(items[i], 1, pos + Vector3.up * 0.5f, Vector3.zero);
                    if (drop == null) break;

                    drop.transform.SetParent(root, true);
                    placedPoints.Add(pos);
                    break;
                }
            }

            return placedPoints.Count;
        }

        /// <summary>맵 위의 지면 칸인가 — 강·절벽·맵 밖에는 떨어뜨리지 않는다.</summary>
        static bool IsOpenGround(World world, Vector3 position)
        {
            var map = world.Map;
            if (map == null) return true;   // 맵 없는 구성(테스트 씬)에서는 제한하지 않는다

            Vector3 local = position - world.Origin;
            var cell = new Vector2Int(Mathf.FloorToInt(local.x / world.CellSize),
                                      Mathf.FloorToInt(local.z / world.CellSize));

            if (cell.x < 0 || cell.y < 0 || cell.x >= map.width || cell.y >= map.height) return false;
            return map.TileAt(cell) == MapTile.Ground;
        }

        static bool TooClose(List<Vector3> points, Vector3 candidate)
        {
            foreach (var p in points)
                if ((p - candidate).sqrMagnitude < StartDropMinGap * StartDropMinGap) return true;
            return false;
        }

        // ── 광맥 ────────────────────────────────────────────────────

        static int PlaceNodes(World world, Transform root)
        {
            var map = world.Map;
            if (map.nodes == null || map.nodes.Length == 0) return 0;
            var boot = FactoryBootstrap.Instance;
            if (boot == null || boot.Factory == null)
            {
                Debug.LogWarning("[WorldPopulator] 공장(FactoryBootstrap)이 없어 광맥을 세우지 못했습니다.", world);
                return 0;
            }
            int placed = 0;
            foreach (var spec in map.nodes)
            {
                if (string.IsNullOrEmpty(spec.itemId)) continue;
                var view = SpawnNodeView(world, root, spec.itemId, spec.cell);
                if (view != null && view.TryAttachAt(spec.cell)) placed++;
            }
            return placed;
        }

        /// <summary>광맥 한 칸의 뷰(프리팹)를 칸 중앙에 세우고 마커(칸)와 자원을 적는다. 심에는 아직 서지 않는다.</summary>
        static ResourceDepositView SpawnNodeView(World world, Transform root, string itemId, Vector2Int cell)
        {
            var item = SaveRefs.Item(itemId);
            if (item == null)
            {
                Debug.LogError($"[WorldPopulator] 광맥({cell.x},{cell.y})의 자원 '{itemId}'이 팩에 없어 세우지 못했습니다.", world);
                return null;
            }
            // 광맥 정의 — Ore 아이템마다 exporter가 만든 entities/<item>_deposit
            var depositDef = FindEntity(item.Id.Split('/').Last() + "_deposit");
            if (depositDef == null)
            {
                Debug.LogError($"[WorldPopulator] 광맥({cell.x},{cell.y})의 정의 '{item.Id.Split('/').Last()}_deposit'가 팩에 없습니다.", world);
                return null;
            }
            // 오브젝트 위치는 칸 중앙. 정의에서 조립한다(모델·콜라이더·ResourceDepositView)
            Vector3 center = world.CellToWorld(cell) + new Vector3(0.5f, 0f, 0.5f) * world.CellSize;
            var go = BuildingAssembler.Build(depositDef, BeltShape.Straight, center, Quaternion.identity, world.CellSize);
            go.transform.SetParent(root, true);
            go.name = $"Node_{PascalKeyOf(item.Id)}_{cell.x}_{cell.y}";
            var view = go.GetComponent<ResourceDepositView>();
            view.Configure(item);
            return view;
        }

        // ── 둥지 ────────────────────────────────────────────────────

        /// <summary>
        /// 뷰가 따로 있는 개체(둥지)의 심 엔티티를 데이터로 만들어 붙인다 — 생성 주체는 월드(populator)다.
        /// 편·HP 정본은 팩 정의(EntityDef.Faction·Health 모듈) — 다른 건물과 같은 규칙.
        /// </summary>
        static void AttachFreshEntity(EntityView view, EntityDef def)
        {
            var entity = SimHost.World.Create(def.Faction, view.transform.position);
            def.Assemble(entity);   // Health·Effects — 정의가 만든다 (둥지 HP 500은 팩 정의의 값)
            view.AttachEntity(entity);
        }

        static int PlaceNests(World world, Transform root)
        {
            var map = world.Map;
            if (map.nests == null || map.nests.Length == 0) return 0;

            var nestDef = FindEntityWith<NestModuleDef>();
            if (nestDef == null)
            {
                Debug.LogWarning("[WorldPopulator] 둥지 정의(Nest 모듈)가 팩에 없어 둥지를 세우지 못했습니다.", world);
                return 0;
            }
            int placed = 0;
            foreach (var spec in map.nests)
            {
                // 정의에서 조립(바위 모델·NestCore 캡슐·NestView); 스폰 포인트는 맵 사양으로 아래서 만든다
                var go = BuildingAssembler.Build(nestDef, BeltShape.Straight, world.CellToWorldCenter(spec.cell), Quaternion.identity, world.CellSize);
                go.transform.SetParent(root, true);
                go.name = $"Nest_{spec.cell.x}_{spec.cell.y}";

                var nest = go.GetComponent<NestView>();
                if (nest != null)
                {
                    // 둥지 엔티티는 심에 먼저 만든다(편·HP는 둥지 데이터, Effects) — 뷰는 받아서 그린다.
                    // 건물 모듈은 뒤의 ConnectPlaced가 이 엔티티를 호스트로 얹는다(둥지 하나에 엔티티 하나).
                    if (nestDef != null) AttachFreshEntity(nest, nestDef);
                    else Debug.LogWarning("[WorldPopulator] 둥지 정의(Nest 모듈)가 팩에 없어 둥지 엔티티를 만들지 못했습니다 — MonsterNest가 폴백으로 세웁니다.", world);

                    nest.Configure(spec.warningRange, spec.triggerRange,
                                   spec.defenseSpawnAmount, spec.defenseSpawnCooldown);
                    ApplySpawnPoints(world, nest, spec);
                    nest.SyncModule();   // 자리·보스 유무를 심 Nest 모듈에 — 상태(파괴·무적)는 심의 것
                }

                ClaimNestCells(nestDef, spec.cell, go);

                // 교전 구역은 값이 있을 때만 붙인다 — 프리팹에 없으면 둥지는 기본 동작을 그대로 쓴다
                if (spec.engageMaxRange > 0f)
                {
                    var zone = go.GetComponent<NestEngagementZone>();
                    if (zone == null) zone = go.AddComponent<NestEngagementZone>();
                    zone.Configure(spec.engageMinRange, spec.engageMaxRange,
                                   spec.chaseRange, spec.leashRange, spec.engageDayOnly);
                }

                placed++;
            }
            return placed;
        }

        /// <summary>
        /// 둥지 데이터(NestDataSO) — BuildingDatabase에서 찾는다. 코어를 찾는 방식과 같은 규칙이라
        /// 씬 배선이 늘지 않는다. 없으면 칸 점유만 건너뛰고 둥지 자체는 그대로 선다.
        /// </summary>
        /// <summary>이 모듈을 가진 엔티티 정의를 팩에서 찾는다(코어·둥지 — 역할이 모듈로 드러나는 것들). 없으면 null.</summary>
        static EntityDef FindEntityWith<T>() where T : EntityModuleDef
        {
            var db = SimHost.Database;
            if (db == null) return null;
            foreach (var e in db.Entities.Values)
                if (e.Has<T>()) return e;
            return null;
        }

        /// <summary>팩 키로 엔티티 정의를 찾는다 — 나무처럼 역할 모듈이 없는 것.</summary>
        static EntityDef FindEntity(string key)
        {
            var db = SimHost.Database;
            return db?.Entity(SimDatabase.IdOf(db.Pack, "entity", key));
        }

        /// <summary>팩 키("iron_ore") → 씬 오브젝트 이름 조각("IronOre") — 구 SO 에셋 이름과 같은 꼴을 유지한다.</summary>
        static string PascalKeyOf(string id)
        {
            string key = id.Substring(id.LastIndexOf('/') + 1);
            var sb = new System.Text.StringBuilder(key.Length);
            bool up = true;
            foreach (char c in key)
            {
                if (c == '_') { up = true; continue; }
                sb.Append(up ? char.ToUpperInvariant(c) : c);
                up = false;
            }
            return sb.ToString();
        }

        /// <summary>
        /// 둥지가 덮는 칸을 팩토리 그리드에 잡아 둔다 — <b>이래야 그 위에 건물이 안 올라간다</b>
        /// (건설 판정은 그리드 점유만 본다).
        ///
        /// 뷰(BuildingEntity)는 만들지 않는다. 씬 위의 둥지는 이미 MonsterNest(Entity)이고,
        /// 한 오브젝트에 Entity가 둘이면 총알이 어느 쪽을 맞혔는지 불확실해지며 몬스터가 자기
        /// 둥지를 목표로 삼는다. 그래서 심에만 넣는다 — FactorySystem.Place는 칸과 그래프만 건드린다.
        ///
        /// 잡은 칸은 <b>영구적이다</b>. 둥지를 부숴도 nestRecoveryDays 뒤에 다시 서므로,
        /// 그 자리는 처음부터 끝까지 건설 금지여야 한다.
        ///
        /// 풋프린트는 둥지 칸을 <b>가운데</b>에 둔다 — 맵은 둥지를 한 점(cell)으로 적는데
        /// 실제 모형은 그보다 크다. 3×3이면 cell을 중심으로 한 칸씩 번진다.
        /// </summary>
        static void ClaimNestCells(EntityDef nestDef, Vector2Int cell, GameObject nestGo)
            => ClaimCells(nestDef, cell, nestGo, "둥지", warnOnOccupied: true);

        /// <summary>
        /// 이 데이터의 풋프린트만큼 팩토리 그리드에 칸을 잡는다. 성공하면 true.
        ///
        /// 풋프린트는 주어진 칸을 <b>가운데</b>에 둔다 — 맵도 심는 쪽도 대상을 한 점(cell)으로
        /// 적는데 실제 모형은 그보다 클 수 있다. 3×3이면 cell을 중심으로 한 칸씩 번진다.
        ///
        /// 겹침을 먼저 확인하는 이유: GridIndex.Add는 덮어쓰기라, 이미 있는 건물 위에 놓으면
        /// 그 건물이 칸을 잃고도 살아 있는 유령이 된다.
        ///
        /// warnOnOccupied — 둥지는 맵이 손으로 찍은 자리라 겹치면 사람이 고쳐야 할 실수지만,
        /// 나무는 수백 그루를 자동으로 심으므로 겹친 그루는 조용히 건너뛰고 총계만 보고한다.
        /// </summary>
        static bool ClaimCells(EntityDef def, Vector2Int cell, GameObject owner,
                               string label, bool warnOnOccupied)
        {
            if (def == null) return false;

            var boot = FactoryBootstrap.Instance;
            if (boot == null || boot.Factory == null)
            {
                Debug.LogWarning($"[WorldPopulator] FactorySim이 아직 없어 {label} 칸을 잡지 못했습니다 — " +
                                 $"{label} 위에 건물이 올라갈 수 있습니다.", owner);
                return false;
            }

            var size = BuildingPorts.RotatedSize(def, 0);
            var origin = cell - new Vector2Int((size.x - 1) / 2, (size.y - 1) / 2);

            for (int dx = 0; dx < size.x; dx++)
                for (int dy = 0; dy < size.y; dy++)
                    if (boot.Factory.Grid.IsOccupied(origin + new Vector2Int(dx, dy)))
                    {
                        if (warnOnOccupied)
                            Debug.LogWarning($"[WorldPopulator] {label} {cell} 의 칸 " +
                                $"{origin + new Vector2Int(dx, dy)} 가 이미 점유되어 있어 칸을 잡지 못했습니다 — " +
                                "맵에서 옮기세요.", owner);
                        return false;
                    }

            // 둥지처럼 스스로 심 엔티티를 가진 개체(MonsterNest)는 그 엔티티에 건물을 얹는다 —
            // 따로 만들면 한 둥지에 엔티티가 둘이 되어 몬스터가 자기 둥지를 목표로 삼는다.
            var hostView = owner != null ? owner.GetComponent<EntityView>() : null;
            boot.Factory.Place(def, origin, 0, host: hostView != null ? hostView.Entity : null);
            return true;
        }

        // ── 나무 ────────────────────────────────────────────────────

        /// <summary>
        /// 맵이 적어 둔 칸에 나무를 세운다 — <b>뷰(BuildingEntity)를 가진 온전한 건물</b>로.
        ///
        /// 칸을 막아야 하는 것이 첫째 이유다: 건설 판정은 그리드 점유만 보므로, 그리드에 들어가지
        /// 않은 나무 위에는 벨트도 포탑도 그대로 올라간다.
        ///
        /// <b>뷰가 필요한 이유는 나무가 부서지기 때문이다.</b> 플레이어가 베어낼 수 있고 다시 자라지
        /// 않으므로, 무엇을 베었는지가 세이브에 남아야 한다. 세이브가 순회하는 목록이
        /// FactoryBootstrap.Buildings = 뷰가 붙은 건물들이라, 뷰가 없으면 벤 나무가 불러오기마다
        /// 되살아난다. 피격 판정(BuildingEntity.ApplyEffects)과 파괴 시 칸 해제도 뷰의 몫이다.
        ///
        /// 복원 중에도 그대로 세운다 — 세이브가 뒤이어 자기에 없는 건물을 지우므로(RemoveUnwanted),
        /// 벤 나무는 그때 사라지고 남은 나무는 "이미 있다"로 재사용된다. 여기서 건너뛰면 세이브가
        /// 나무를 다시 세울 때 프리팹을 모르는 채로(TreeDataSO.prefab 은 비어 있다) 빈 껍데기가 된다.
        ///
        /// 모형·크기·각도는 칸 좌표에서 결정론적으로 뽑는다 — 맵에 그루별로 적어 두면 수백 줄이
        /// 늘어나는데 그 값들은 사람이 고칠 것이 아니고, 해시로 뽑으면 같은 맵은 언제나 같은 숲이 된다.
        /// </summary>
        static int PlaceTrees(World world, Transform root)
        {
            var map = world.Map;
            if (map.trees == null || map.trees.Length == 0) return 0;

            var treeDef = FindEntity(TreeEntityKey);
            if (treeDef == null)
            {
                Debug.LogWarning($"[WorldPopulator] 나무 정의(entities/{TreeEntityKey})가 팩에 없어 나무를 세우지 못했습니다.",
                                 world);
                return 0;
            }
            int variants = Mathf.Max(1, ViewSchema.Of(treeDef).Models().Count);   // view.model 배열 = 변형 목록

            // 심은 <b>런타임에만</b> 필요하다 — 에디터에서 씬에 굳힐 때는 그림만 만들고,
            // 칸을 잡는 것은 플레이가 시작될 때 Connect 가 한다.
            var boot = FactoryBootstrap.Instance;
            if (boot == null || boot.Factory == null)
            {
                Debug.LogWarning("[WorldPopulator] FactorySim이 아직 없어 나무를 세우지 못했습니다.", world);
                return 0;
            }
            if (!Managers.PackAssets.IsReady)
            {
                Debug.LogError("[WorldPopulator] 팩 모델(glb) preload가 끝나기 전에 나무를 세우려 했습니다 — 굳지 않은 씬은 로딩 게이트가 필요합니다(4c 후속). 나무를 세우지 않습니다.", world);
                return 0;
            }

            int placed = 0, skipped = 0;
            foreach (var cell in map.trees)
            {
                if (!map.InBounds(cell)) continue;

                // 칸이 차 있으면 세우지 않는다 — 세워 놓고 칸을 못 잡으면 눈에는 나무가 있는데
                // 그 위에 건물이 올라간다. GridIndex.Add 는 덮어쓰기라 먼저 확인해야 한다.
                if (boot.Factory.Grid.IsOccupied(cell)) { skipped++; continue; }

                TreePose(world, cell, variants, out int pi, out Vector3 pos, out float yaw, out float scale);

                // 나무도 다른 건물처럼 정의에서 조립한다 — 변형(view.model 배열)은 칸에서 결정적으로, 크기 흔들기는 루트 배율에 곱한다
                var go = BuildingAssembler.Build(treeDef, BeltShape.Straight, pos, Quaternion.Euler(0f, yaw, 0f), world.CellSize, pi);
                go.transform.SetParent(root, true);
                go.transform.localScale *= scale;
                go.name = $"Tree_{cell.x}_{cell.y}";
                ConnectTree(go.GetComponent<BuildingView>(), treeDef, cell);
                placed++;
            }

            if (skipped > 0)
                Debug.Log($"[WorldPopulator] 나무 {skipped}그루는 칸이 이미 차 있어 세우지 않았습니다 " +
                          "(코어·광맥·둥지와 겹친 자리).", world);
            return placed;
        }

        /// <summary>나무 엔티티의 팩 키 — 나무는 역할 모듈이 없어(Building·Health·Effects뿐) 이름으로 찾는다. 맵이 종류를 고르게 되면 맵 데이터로 간다.</summary>
        public const string TreeEntityKey = "tree";

        // 나무 한 그루의 생김새를 정하는 값들 — 칸 좌표에서 뽑으므로 같은 맵은 언제나 같은 숲이다
        const float TreeScaleMin = 0.85f, TreeScaleMax = 1.35f;
        /// <summary>칸 중앙에서 흔드는 폭(칸). 0이면 격자에 줄을 서서 숲으로 안 보인다.</summary>
        const float TreeJitter = 0.32f;

        /// <summary>
        /// 그 칸에 설 나무 한 그루의 모형·위치·각도·크기.
        ///
        /// <b>에디터 미리보기와 런타임이 반드시 같은 값을 써야 하므로</b> 계산을 여기 한 곳에 둔다 —
        /// 양쪽에 같은 해시를 베껴 두면 한쪽만 고쳐졌을 때 미리보기가 거짓말을 하기 시작한다.
        /// </summary>
        public static void TreePose(World world, Vector2Int cell, int prefabCount,
                                    out int prefabIndex, out Vector3 position, out float yaw, out float scale)
        {
            prefabIndex = prefabCount > 0 ? Hash(cell.x, cell.y, 41) % prefabCount : 0;
            scale = Mathf.Lerp(TreeScaleMin, TreeScaleMax, Hash(cell.x, cell.y, 43) % 1000 / 1000f);
            yaw = Hash(cell.x, cell.y, 47) % 3600 / 10f;

            float jx = (Hash(cell.x, cell.y, 23) % 1000 / 1000f - 0.5f) * 2f * TreeJitter;
            float jz = (Hash(cell.x, cell.y, 29) % 1000 / 1000f - 0.5f) * 2f * TreeJitter;
            position = world.CellToWorld(cell)
                     + new Vector3((0.5f + jx) * world.CellSize, 0f, (0.5f + jz) * world.CellSize);
            position.y = GroundYAt(world, position);
        }

        /// <summary>
        /// 그 자리의 지면 높이. 지형이 있으면 실제 표면을, 없으면 월드 원점 높이를 쓴다 —
        /// 지형 없는 구성(테스트 씬)에서도 나무가 뜨거나 잠기지 않게.
        /// </summary>
        public static float GroundYAt(World world, Vector3 pos)
        {
            var terrain = world.GetComponentInChildren<Terrain>(true);
            if (terrain == null) return world.Origin.y;
            return terrain.SampleHeight(pos) + terrain.transform.position.y;
        }

        /// <summary>위치를 잘 섞인 값으로 바꾼다 — 좌표를 곱해 더하는 식은 대각선 줄무늬를 만든다.</summary>
        static int Hash(int x, int y, int salt)
        {
            unchecked
            {
                int h = x * 374761393 + y * 668265263 + salt * 1442695041;
                h = (h ^ (h >> 13)) * 1274126177;
                return (h ^ (h >> 16)) & 0x7fffffff;
            }
        }

        /// <summary>
        /// 둥지의 스폰 자리를 맵대로 다시 놓는다. 프리팹에 이미 자식(SpawnPoint_1…)이 있으면
        /// 그것을 옮겨 쓰고, 모자라면 새로 만든다.
        ///
        /// 보스 유무와 종류는 맵(spawnPoints[].boss)이 정한다 — 프리팹은 자리(Transform)만 든다.
        /// </summary>
        static void ApplySpawnPoints(World world, NestView nest, in NestSpec spec)
        {
            if (spec.spawnPoints == null || spec.spawnPoints.Length == 0) return;
            if (nest.spawnPoints == null) nest.spawnPoints = new List<NestView.NestSpawnPoint>();

            nest.SetDefender(spec.defender);

            for (int i = 0; i < spec.spawnPoints.Length; i++)
            {
                var point = spec.spawnPoints[i];
                Vector3 pos = world.CellToWorldCenter(spec.cell + point.offset);

                NestView.NestSpawnPoint slot;
                if (i < nest.spawnPoints.Count && nest.spawnPoints[i] != null && nest.spawnPoints[i].point != null)
                {
                    slot = nest.spawnPoints[i];
                    slot.point.position = pos;
                }
                else
                {
                    var t = new GameObject($"SpawnPoint_{i + 1}").transform;
                    t.SetParent(nest.transform, false);
                    t.position = pos;

                    if (i < nest.spawnPoints.Count && nest.spawnPoints[i] != null) { slot = nest.spawnPoints[i]; slot.point = t; }
                    else { slot = new NestView.NestSpawnPoint { point = t }; nest.spawnPoints.Add(slot); }
                }

                slot.bossId = point.HasBoss ? point.boss : null;   // 자리와 종류 모두 맵이 정한다
            }

            // 맵이 정한 것보다 많으면 남는 자리는 버린다 — 옛 배치가 유령처럼 남지 않게
            if (nest.spawnPoints.Count > spec.spawnPoints.Length)
                nest.spawnPoints.RemoveRange(spec.spawnPoints.Length,
                                             nest.spawnPoints.Count - spec.spawnPoints.Length);
        }

        // ── 밤 진입로 ───────────────────────────────────────────────

        /// <summary>
        /// 밤 웨이브가 들어오는 자리. 빈 Transform이면 충분하므로 프리팹이 필요 없다.
        /// 제공자는 <b>BattleManager 아래</b>에 둔다 — 그쪽이 자기 자식에서 찾기 때문이다.
        /// </summary>
        static NightSpawnPointProvider PlaceNightSpawns(World world, Transform root, Transform battleRoot)
        {
            var map = world.Map;
            if (map.nightSpawnPoints == null || map.nightSpawnPoints.Length == 0) return null;

            var host = battleRoot != null ? battleRoot : root;
            var provider = host.GetComponentInChildren<NightSpawnPointProvider>(true);
            if (provider == null)
            {
                var go = new GameObject("NightSpawnPoints");
                go.transform.SetParent(host, false);
                provider = go.AddComponent<NightSpawnPointProvider>();
            }

            var points = new List<Transform>(map.nightSpawnPoints.Length);
            foreach (var cell in map.nightSpawnPoints)
            {
                var t = new GameObject($"NightSpawn_{cell.x}_{cell.y}").transform;
                t.SetParent(provider.transform, false);
                t.position = world.CellToWorldCenter(cell);
                points.Add(t);
            }

            provider.SetSpawnPoints(points);
            return provider;
        }
    }
}
