using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using CoreDawn.Inventories;
using CoreDawn.Sim;
using CoreDawn.Data;   // PortDefinition·BeltShape·Direction — 5a-2f에서 공장과 함께 Sim으로 옮긴다

namespace CoreDawn.Factory
{
    /// <summary>
    /// 배치된 건물 — 심 엔티티에 붙는 모듈(plain C#, MonoBehaviour 아님).
    /// EntityDef(팩 정의, 불변·공유) = 설계도, BuildingModule = 실물 (각자 독립적 상태).
    ///
    /// 이 모듈이 건물 데이터의 원본(source of truth)이다 — Def/Origin/회전/버퍼/연결/행동/IsRemoved.
    /// HP·편·번호는 모듈이 아니라 <see cref="Owner"/>(심 엔티티)의 것이다: 둥지는 건물이면서 스포너이고
    /// 코어는 건물이면서 목표라, "건물"을 상속으로 만들면 곧 다이아몬드가 되므로 조합으로 붙인다.
    /// 씬 표현은 BuildingView(MonoBehaviour)가 껍데기로 맡고, 이 건물이 제거되면 FactorySystem.Removed를 타고 껍데기도 함께 정리된다.
    /// (순수 C#인 이유: 씬·프레임 없이 돌리는 헤드리스 시뮬레이션·테스트가 가능해야 한다)
    ///
    /// 연결 목록(InputConnections/OutputConnections)은 BuildingGraph가 채우고,
    /// 행동(IBuildingBehavior)은 정의의 모듈 조합으로 <see cref="BuildingBehaviors"/>가 고른다.
    /// 정의에서 무엇을 읽을지는 정의 타입이 말한다 — 크기·철거·비용은 <see cref="Building"/>, 포트는 Ports, 버퍼는 Inventory.
    /// </summary>
    public class BuildingModule : EntityModule, IDamageInterceptor, IFootprint
    {
        public readonly FactorySystem Factory;

        // 불변 데이터 (생성 이후 변경 안 됨)

        /// <summary>이 건물의 정의(팩 json). 다른 모듈 정의는 Def.Get&lt;XModuleDef&gt;()로 읽는다.</summary>
        public EntityDef Def { get; }
        /// <summary>건물 값 — 크기·배치 가능·철거·아군 공격·위협 시드·메뉴 순서·비용. 정의의 Building 모듈.</summary>
        public BuildingModuleDef Building { get; }
        public Vector2Int Origin { get; }
        public int RotationSteps { get; }

        /// <summary>이 건물이 자기 엔티티를 만들었는가(배치가 새로 만듦) — 제거 때 엔티티도 같이 지운다.
        /// false면 남의 엔티티(둥지 등)에 얹힌 것이라 건물 모듈만 떨어진다.</summary>
        public bool OwnsEntity { get; }

        // 인스턴스별 포트 형상 (벨트 커브 등). null이면 정의의 회전 포트 사용.
        public PortDefinition[] PortOverride { get; }

        /// <summary>
        /// 벨트 모양 (직선/커브L/커브R) — 배치 시 결정되는 인스턴스 상태. 벨트가 아닌 건물에는 의미가 없다.
        ///
        /// 포트는 PortOverride로도 알 수 있지만 커브 메시 프리팹은 모양으로만 고를 수 있어서
        /// (BeltDataSO.PrefabFor), 세이브가 이 값을 그대로 되살릴 수 있게 여기 남겨둔다.
        /// </summary>
        public BeltShape Shape { get; }

        // 런타임 상태 — 입력/출력 버퍼 분리 (슬롯 기반, 플레이어 인벤토리와 같은 모델)
        public ItemContainer Input  { get; private set; }
        public ItemContainer Output { get; private set; }

        /// <summary>FactorySystem.Remove가 설정. 제거 후 큐/힙에 남은 참조를 걸러낸다.</summary>
        public bool IsRemoved { get; set; }

        // 연결 목록 — BuildingGraph가 OnPlaced/OnRemoved 시 수정
        public readonly List<BuildingConnection> InputConnections  = new();
        public readonly List<BuildingConnection> OutputConnections = new();

        IBuildingBehavior _behavior;

        public BuildingModule(FactorySystem factory, EntityDef def, Vector2Int origin, int rotSteps,
            PortDefinition[] portOverride = null, BeltShape shape = BeltShape.Straight, bool ownsEntity = true)
        {
            if (def == null) throw new System.ArgumentNullException(nameof(def));
            Building = def.Get<BuildingModuleDef>()
                ?? throw new System.ArgumentException($"'{def.Id}'에 Building 모듈 정의가 없습니다 — 건물이 아닙니다.", nameof(def));

            Factory       = factory;
            Def           = def;
            Origin        = origin;
            RotationSteps = rotSteps;
            PortOverride  = portOverride;
            Shape         = shape;
            OwnsEntity    = ownsEntity;

            // 그릇(Input/Output)과 행동은 OnAttach에서 — 엔티티의 Inventory 모듈(정의가 만든다)이 있어야 한다
        }

        /// <summary>사람이 읽는 이름 — 정의의 displayName, 없으면 id.</summary>
        public string DisplayName => string.IsNullOrEmpty(Def.DisplayName) ? Def.Id : Def.DisplayName;

        /// <summary>맵의 코어인가 — 밤 웨이브의 최종 목표, 파괴 = 게임오버. 정의(Core 모듈)가 정한다(뷰의 플래그가 아니다).</summary>
        public bool IsCore => Def.Has<CoreModuleDef>();

        /// <summary>벨트인가 — 세그먼트 병합·길찾기·튜토리얼이 묻는다. 정의(Conveyor 모듈)가 정한다.</summary>
        public bool IsConveyor => Def.Has<ConveyorModuleDef>();

        /// <summary>회전이 반영된 점유 크기(칸).</summary>
        public Vector2Int Size => BuildingPorts.RotatedSize(Building, RotationSteps);

        /// <summary>
        /// 점유 풋프린트의 월드 사각형(XZ 평면). y는 호출자가 안다(뷰 높이 등).
        ///
        /// 모델(콜라이더)이 아니라 <b>차지한 칸</b>이 기준이다. 콜라이더는 메시마다 조각조각 흩어져 있어서
        /// 하나만 집으면 건물의 일부만 가리키고(코어의 안테나 한 짝), 전부 합쳐도 모델일 뿐 풋프린트는 아니다.
        /// 길을 막는 것도, 몬스터가 다가와 때리는 것도 풋프린트이므로 시각화·목표·거리의 기준은 전부 여기여야 한다.
        /// </summary>
        public void WorldRect(float y, out Vector3 min, out Vector3 max)
            => Factory.Geometry.RectOf(Origin, Size, y, out min, out max);

        /// <summary>점에서 풋프린트 경계까지의 XZ 거리. 안에 있으면 0.</summary>
        public float DistanceTo(Vector3 from)
        {
            WorldRect(from.y, out var min, out var max);
            return GridGeometry.DistanceToRect(from, min, max);
        }

        /// <summary>
        /// 받는 피해 규칙 — 체력이 깎이기 전에 심에서 거른다(구 BuildingView.ApplyEffects/ReceiveDamage override).
        /// ① <b>아군의 공격</b>이 통하지 않는 건물(Building.IsAttackable=false)은 적이 아닌 출처의 피해를 흘린다.
        ///    몬스터의 공격은 이 값과 무관하다 — 밤 웨이브가 무엇을 노리는지는 목표 선정(threatSeedCost)이 정하고,
        ///    정한 목표는 실제로 부술 수 있어야 한다.
        ///    출처를 모르는 피해(null)는 아군으로 본다 — 실제 공격자는 모두 자신을 넘기므로 출처 없음 = 누구의 공격도 아님.
        /// ② 행동이 인터셉터면(코어의 보호막) 남은 몫을 그쪽에 넘긴다.
        /// </summary>
        // 받는 피해 체인 등록 — 아군 무시·행동 인터셉터(코어 보호막)는 Health의 체인에서 돈다
        protected internal override void OnAttach()
        {
            // 그릇은 엔티티의 Inventory 모듈(정의가 만든다). 없는 건물(나무·둥지·울타리)은 0칸 그릇 — 아무것도 받지 않는다
            var inventory = Owner.Get<InventoryModule>();
            Input  = inventory?.Input  ?? new ItemContainer(0);
            Output = inventory?.Output ?? new ItemContainer(0);
            // 행동은 그릇·정의가 갖춰진 뒤에 — 조립기는 생성자에서 입력 필터를 건다
            _behavior = BuildingBehaviors.Create(this);
            // 제작 설비(조립기·제련로 — 행동이 사라진 건물): 옛 AssemblerBehavior 생성자가 걸던 그릇 정책의 공통화
            var crafter = Owner.Get<CrafterModule>();
            if (_behavior == null && crafter != null)
            {
                Input.SingleStackPerType = true;            // 한 재료가 입력 슬롯 전부를 독점하는 데드락 방지
                Input.AcceptFilter = crafter.IsIngredient;  // 입력 버퍼는 현재 레시피의 재료만
                crafter.Delivered += FlushOutputs;          // 완성품을 넣는 즉시 밀어야 같은 틱에 다음 1회가 시작될 자리가 난다
                var first = crafter.Recipes.FirstOrDefault();
                if (crafter.Recipe == null && first != null && RecipeDatabaseSO.IsUnlocked(first)) crafter.SetRecipe(first);
            }
            // 행동 없이 모듈이 스스로 걷는 건물(포탑·오라·지뢰…): 그릇이 바뀌면 다음 틱에 다시 판단한다.
            // 손으로 넣고 빼는 경로는 벨트를 안 거쳐 아무도 깨워 주지 않는다(옛 행동들이 하나씩 달던 구독의 공통화)
            if (_behavior == null && !IsConveyor) { Input.Changed += WakeSelf; Output.Changed += WakeSelf; }
            Owner.Health?.AddInterceptor(this);
        }
        protected internal override void OnDetach() => Owner.Health?.RemoveInterceptor(this);

        public float Intercept(float amount, Entity source)
        {
            if (!Building.IsAttackable)
            {
                bool hostile = source != null && Owner != null && source.Faction.IsHostileTo(Owner.Faction);
                if (!hostile) return 0f;
            }
            return _behavior is IDamageInterceptor i ? i.Intercept(amount, source) : amount;
        }

        /// <summary>회전/모양이 적용된 실제 포트 목록. BuildingGraph가 이걸 사용한다.</summary>
        public PortDefinition[] GetEffectivePorts() => PortOverride ?? BuildingPorts.Rotated(Def, RotationSteps);

        /// <summary>
        /// 이 건물이 지정한 월드 칸에서 지정 방향을 향하는 포트를 갖고 있는가.
        /// 순수 기하 질의다 — 연결 성립 규칙(입출력 짝) 자체는 BuildingGraph가 소유하므로
        /// 여기에 다시 짓지 말 것. 포트 시각화가 "이미 맞물린 자리"를 걸러낼 때 쓴다.
        /// </summary>
        /// <param name="isInput">null이면 입출력을 가리지 않는다.</param>
        public bool HasPortAt(Vector2Int cell, Direction dir, bool? isInput = null)
        {
            var ports = GetEffectivePorts();
            if (ports == null) return false;
            foreach (var p in ports)
            {
                if (p == null || p.Direction != dir) continue;
                if (Origin + p.LocalOffset != cell) continue;
                if (isInput.HasValue && p.IsInput != isInput.Value) continue;
                return true;
            }
            return false;
        }

        /// <summary>BuildingGraph.OnPlaced() 완료 후 호출 — 연결이 확정된 뒤 초기화.</summary>
        public void OnAfterConnected() => _behavior?.OnAfterPlaced();

        /// <summary>FactorySystem이 이 건물이 깨어 있는 틱에 호출. 행동이 있으면 행동이(과도기), 없으면 모듈 공통 틱.</summary>
        public void Tick(float dt)
        {
            if (_behavior != null) { _behavior.Tick(dt); return; }
            TickModules(dt);
        }

        void WakeSelf() => Factory.MarkDirty(this);

        /// <summary>
        /// 모듈 공통 틱 — 행동이 하던 공장 어댑터 일을 한 곳에서: 밀린 출력부터 밀어내고, 걷는 모듈(ISteppable)을 전부 한 걸음,
        /// 산출이 늘었으면 다시 밀어내고, 입력이 줄었으면(재료·탄 소비) 막혀 있던 상류를 깨우고, 모듈이 원하는 시각에 깨움을 예약한다.
        /// 모듈은 공장을 모른다 — 그릇의 개수 변화만으로 무엇이 일어났는지 안다.
        /// </summary>
        void TickModules(float dt)
        {
            FlushOutputs();
            int inBefore = Input.Total, outBefore = Output.Total;
            float wake = 0f;
            bool stepped = false;
            var modules = Owner.Modules;
            for (int i = 0; i < modules.Count; i++)
                if (modules[i] is ISteppable s) { stepped = true; wake = Mathf.Max(wake, s.Step(Factory.Now, dt)); }
            if (IsRemoved) return;                       // 지뢰처럼 걷다가 스스로 죽은 건물
            if (!stepped) PumpPassThrough();             // 걷는 모듈이 없으면 통과 보관소다
            if (Output.Total > outBefore) FlushOutputs();
            if (Input.Total < inBefore) NotifyUpstream();
            if (wake > 0f) Factory.ScheduleWake(this, wake);
        }

        /// <summary>
        /// 통과 보관소(걷는 모듈 없이 그릇·포트만 있는 건물 — 보관소·드론 포트)의 펌핑(구 StorageBehavior).
        /// 보관함이 곧 입력 버퍼라 출력 버퍼를 거치지 않고 하류로 곧장 민다 — 옮겨 놓고 밀기에 실패하면
        /// 그 물건이 플레이어가 못 여는 버퍼에 갇힌다. 못 민 만큼은 보관함에 그대로 남는다
        /// (저장소는 라인을 막는 마개가 아니라 완충 장치다). 포트 없는 건물(나무·둥지·울타리·탄약함)은 밀 곳이 없어 그냥 지나간다.
        /// </summary>
        void PumpPassThrough()
        {
            foreach (var (item, count) in Input.Snapshot())
            {
                int moved = 0;
                while (moved < count && TryPushOutput(item)) moved++;
                if (moved > 0) Input.TryConsume(item, moved);   // 상류 깨움은 공통 틱이 그릇 변화로 한다
            }
        }

        /// <summary>행동 객체 조회 (레시피 지정 등 외부 설정용). 행동 없는 건물(나무·둥지·울타리)은 null.</summary>
        public IBuildingBehavior Behavior => _behavior;

        // 출력 라운드로빈 커서 — 다음에 먼저 밀어볼 출력 연결 인덱스.
        // 세이브하지 않는다: 로드 뒤 0에서 다시 돌기 시작해도 분배는 곧 고르게 되고, 잃는 것이 없다.
        int _nextOut;

        /// <summary>
        /// 출력 버퍼의 아이템을 연결된 다음 건물로 Push.
        /// 성공하면 수신 건물을 Dirty 마킹 → 다음 틱에 처리됨.
        ///
        /// 출력이 여럿이면 <b>라운드로빈</b>으로 돌아가며 민다 (분배기와 같은 규칙).
        /// 예전에는 목록 첫 연결이 받는 한 그쪽으로만 갔다 — 저장소처럼 출구가 넷인 건물에서
        /// 먼저 이은 라인이 전부를 독식하고 나머지 셋은 첫 라인이 막힐 때만 받았다.
        /// 가득 찬 출구는 건너뛰므로 한쪽이 막혀도 나머지로 계속 흐른다.
        /// </summary>
        public bool TryPushOutput(ItemDef item)
        {
            var conns = OutputConnections;
            int n = conns.Count;
            for (int i = 0; i < n; i++)
            {
                var c = conns[(_nextOut + i) % n];
                if (!c.To.Input.TryAdd(item)) continue;   // 가득 찬 출구 — 다음 출구로
                Factory.MarkDirty(c.To);
                _nextOut = (_nextOut + i + 1) % n;        // 다음 아이템은 그 다음 출구부터
                return true;
            }
            return false; // 모든 출력 막힘
        }

        /// <summary>출력 버퍼의 아이템을 하류가 받는 만큼 전부 배출. 행동들의 공용 루틴.</summary>
        public void FlushOutputs()
        {
            foreach (var (item, count) in Output.Snapshot())
                for (int k = 0; k < count && TryPushOutput(item); k++)
                    Output.TryConsume(item);
        }

        /// <summary>
        /// 이 건물의 입력 버퍼에 자리가 생겼음을 상류에 알린다.
        /// 출력이 막혀 정지(stall)해 있던 상류 건물이 다음 틱에 재시도한다.
        /// </summary>
        public void NotifyUpstream()
        {
            foreach (var c in InputConnections)
                Factory.MarkDirty(c.From);
        }
    }
}
