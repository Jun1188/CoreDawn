using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using CoreDawn.Combat;
using CoreDawn.FPS;
using CoreDawn.Inventories;
using CoreDawn.Managers;
using CoreDawn.Save;
using CoreDawn.UI;
using CoreDawn.Sim;
using CoreDawn.Factory;
using CoreDawn.Data;

namespace CoreDawn.Factory
{
    /// <summary>
    /// 입력 버퍼(=플레이어 납품 + 벨트 자동 투입 공용 창구)로 자원을 받는다.
    /// 요구량이 전부 채워지면 소비하고 GameManager.AdvanceTier로 다음 티어를 해금한다.
    ///
    /// 요구에 없는 것이 들어오면 거절하지 않고 소각해 <b>보호막</b>으로 바꾼다 —
    /// 잘못 흘려보낸 자원이 벨트 위에 영원히 처박히는 대신 방어력이 되어 돌아온다.
    /// 단계가 오른 뒤 쓸모가 없어진 잔여물도 같은 경로로 사라진다.
    ///
    /// 투입 경로는 둘인데 반응 경로는 하나로 통일한다:
    ///   - 벨트가 TryAdd로 채우면 Building.TryPushOutput이 이미 Sim.MarkDirty를 호출 → 다음 틱에 Tick.
    ///   - 플레이어가 인벤토리 UI로 직접 넣으면(TryPutAt/TryExchangeAt) 벨트 경로를 거치지 않으므로,
    ///     ItemContainer.Changed 이벤트를 구독해 그 자리에서 Sim.MarkDirty를 걸어 같은 결과를 만든다.
    /// </summary>
    public class CoreBehavior : IBuildingBehavior, IInteractiveBehavior, ISaveableBehavior, IDamageInterceptor
    {
        readonly BuildingModule _b;
        readonly CoreDataSO _so;

        public CoreBehavior(BuildingModule b, CoreDataSO so)
        {
            _b = b;
            _so = so;
            _b.Input.SingleStackPerType = true; // 한 아이템이 슬롯 전부를 독점 못하게 (Assembler와 동일 이유)
            _b.Input.Changed += () => _b.Factory.MarkDirty(_b); // 수동 투입도 Tick 재평가 트리거
            RefreshAcceptFilter();
        }

        /// <summary>플레이어 상호작용/UI가 바인딩할 컨테이너 — 벨트도 여기로 들어온다.</summary>
        public ItemContainer Container => _b.Input;

        /// <summary>수리 단계 목록을 읽기 위한 UI용 접근자.</summary>
        public CoreDataSO Data => _so;

        /// <summary>현재 진행 중인 단계 인덱스. tiers.Length와 같으면 전 단계 완료.</summary>
        public int CurrentTierIndex => TierIndex;

        /// <summary>전체 수리 단계 수.</summary>
        public int TierCount => _so.tiers != null ? _so.tiers.Length : 0;

        int TierIndex => GameManager.Instance != null ? GameManager.Instance.UnlockedTier : 0;

        public bool HasNextTier => _so.tiers != null && TierIndex < _so.tiers.Length;

        CoreTierDefinition CurrentTier => HasNextTier ? _so.tiers[TierIndex] : null;

        public string InteractPrompt => HasNextTier ? "코어에 자원 납품" : "코어 (최고 티어 달성)";

        public void Interact(PlayerController player)
        {
            // 씬에 UITK 코어 패널이 있으면 그쪽을 연다. 없으면 기존 uGUI 화면으로 —
            // "UITK 먼저, uGUI 폴백" 정책은 GameScreens가 소유한다 — 여기 있던 원조 패턴의 일반화.
            GameScreens.OpenCore(this);
        }

        /// <summary>현재 티어 요구 아이템별 (아이템, 필요량, 현재량) — 진행률 UI용.</summary>
        public IReadOnlyList<(ItemDataSO item, int required, int current)> GetProgress()
        {
            var list = new List<(ItemDataSO, int, int)>();
            var reqs = CurrentTier?.requirements;
            if (reqs == null) return list;
            foreach (var r in reqs) list.Add((r.item, r.amount, _b.Input.CountOf(r.item)));
            return list;
        }

        public void OnAfterPlaced() => RefreshAcceptFilter(); // 배치 시점에 이미 진행된 티어 반영(재시작 등)

        // ── 수리 확인 (SCR-01b)
        //
        // 마지막 부품을 넣자마자 수리가 시작되면 플레이어가 준비할 틈이 없다. 특히 마지막 단계는
        // 곧바로 최종 방어전이라, 확인창을 거쳐야 한다. 그래서 요구 충족과 실제 진행을 갈라 둔다.

        bool _ready;

        /// <summary>요구가 전부 채워졌는가 — UI가 "납품" 버튼을 "수리 시작"으로 바꾸는 신호.</summary>
        public bool IsReadyToRepair => _ready;

        /// <summary>준비 상태가 바뀔 때만 발화. UI가 매 프레임 폴링하지 않게 한다.</summary>
        public event System.Action ReadyChanged;

        public void Tick(float dt)
        {
            // 요구 밖 자원부터 태운다 — 그래야 그것들이 잡고 있던 슬롯이 풀린 상태에서
            // 이번 단계의 충족 여부를 본다. 순서가 뒤집히면 쓰레기 한 칸이 요구 부품을
            // 못 들어오게 막은 채로 "미충족" 판정이 나 한 틱씩 계속 헛돈다.
            BurnSurplus();

            var reqs = CurrentTier?.requirements;
            if (reqs == null) { SetReady(false); return; }

            foreach (var r in reqs)
                if (_b.Input.CountOf(r.item) < r.amount) { SetReady(false); return; } // 아직 미충족

            SetReady(true);

            // 확인창을 띄울 UI가 없는 씬에서는 예전처럼 즉시 진행한다.
            // 그러지 않으면 UITK 패널이 아직 안 들어간 씬에서 코어 진행이 영영 멈춘다 —
            // Interact가 이미 쓰고 있는 "씬 내용이 경로를 결정한다" 방침과 같다.
            if (!CorePanelView.ExistsInScene()) TryStartRepair();
        }

        /// <summary>
        /// 확인창의 "수리 시작"이 호출. 요구를 소비하고 다음 단계를 연다.
        /// 호출 시점에 요구가 다시 미달일 수 있으므로(벨트가 도로 빼갔다든지) 여기서 한 번 더 검사한다.
        /// </summary>
        public bool TryStartRepair()
        {
            var tier = CurrentTier;          // 진행하면 CurrentTier가 다음 단계를 가리키므로 먼저 잡는다
            var reqs = tier?.requirements;
            if (reqs == null) return false;

            foreach (var r in reqs)
                if (_b.Input.CountOf(r.item) < r.amount) { SetReady(false); return false; }

            // 진행을 기록할 곳이 없으면 시작하지 않는다. 여기서 그냥 밀고 나가면 부품만
            // 먹고 단계는 그대로라 매 틱 같은 일이 반복된다 — 헤드리스 심/테스트 씬처럼
            // GameManager가 없는 환경이 실제로 있다.
            var gm = GameManager.Instance;
            if (gm == null) { SetReady(false); return false; }

            foreach (var r in reqs) _b.Input.TryConsume(r.item, r.amount);

            gm.AdvanceTier(TierIndex + 1);
            ApplyMaxHpBonus(tier.maxHpBonus);
            SetReady(false);
            RefreshAcceptFilter();

            // 단계가 오르면 요구 목록이 통째로 바뀐다 — 방금까지 부품이던 것이 잔여물이 된다.
            // 다음 틱까지 미루지 않고 그 자리에서 태운다: 안 그러면 새 단계의 부품이
            // 들어올 슬롯을 옛 단계의 재고가 한 틱 동안 막는다.
            BurnSurplus();

            _b.NotifyUpstream(); // 자리 비었으니 막혀있던 상류(벨트) 재개
            return true;
        }

        // ── 보호막 (소각 전환)
        //
        // 값의 원본은 여기다. 내구도(HealthComponent)는 씬 껍데기가 갖고 있지만 보호막은
        // 자원 흐름에서 생기는 값이라 자원과 같은 곳 — 심(sim) — 에 둔다.
        // 씬의 BuildingEntity.TakeDamage가 피해를 넘기기 전에 AbsorbDamage로 들른다.

        float _shield;

        /// <summary>현재 보호막. 피해를 내구도보다 먼저 받아낸다.</summary>
        public float Shield => _shield;

        /// <summary>보호막 최대치 — 기본값 + 지금까지 완료한 단계들의 보너스.</summary>
        public float MaxShield
        {
            get
            {
                float max = _so.baseMaxShield;
                var tiers = _so.tiers;
                if (tiers != null)
                    for (int i = 0; i < TierIndex && i < tiers.Length; i++)
                        max += tiers[i].maxShieldBonus;
                return Mathf.Max(0f, max);
            }
        }

        /// <summary>보호막 값이 바뀔 때만 발화 — (현재, 최대). UI가 폴링하지 않게 한다.</summary>
        public event System.Action<float, float> ShieldChanged;

        /// <summary>
        /// 피해를 보호막이 먼저 흡수하고 <b>남은 몫</b>을 돌려준다. 0이면 내구도는 무사하다.
        /// 심을 깨우지 않는다 — 입구가 아무것도 거절하지 않으므로 보호막이 깎였다고 해서
        /// 다시 받을 수 있게 되는 것이 없다.
        /// </summary>
        public float AbsorbDamage(float damage)
        {
            if (damage <= 0f || _shield <= 0f) return damage;

            float absorbed = Mathf.Min(_shield, damage);
            _shield -= absorbed;
            ShieldChanged?.Invoke(_shield, MaxShield);
            return damage - absorbed;
        }

        /// <summary>
        /// 받는 피해 인터셉터 — 보호막이 내구도보다 먼저 맞는다. Building 모듈이 Health.Damage 안에서 부른다
        /// (구 BuildingEntity.ReceiveDamage override). 수렴점이 심이라 몬스터 공격·총알·DoT 전부 보호막을 거친다.
        /// </summary>
        public float Intercept(float amount, Entity source) => AbsorbDamage(amount);

        /// <summary>이 아이템이 지금 단계의 요구 목록에 있는가. 없으면 소각 대상이다.</summary>
        bool IsRequired(ItemDataSO item)
        {
            var reqs = CurrentTier?.requirements;
            return reqs != null && System.Array.Exists(reqs, r => r != null && r.item == item);
        }

        /// <summary>
        /// 입력 버퍼에서 요구 목록에 없는 것을 전부 태워 보호막으로 바꾼다. 태운 개수를 돌려준다.
        ///
        /// 보호막 최대치를 넘는 분은 그대로 소멸한다. 안 태우고 남겨두면 그 물건이 슬롯을
        /// 영구히 점거해 다음 단계 부품이 못 들어오는 교착이 되고, 코어가 아무것도 거절하지
        /// 않는 이상 뒤에서 계속 밀려들어오기까지 한다. 상한은 방어력이 무한히 오르는 것을
        /// 막는 장치이지 자원을 보관해 주겠다는 약속이 아니다.
        ///
        /// 마지막 단계까지 끝낸 코어는 요구 목록이 없으므로 들어오는 모든 것이 보호막이 된다.
        /// </summary>
        int BurnSurplus()
        {
            if (!_so.burnSurplusIntoShield || !_b.Input.HasAny) return 0;

            float max = MaxShield;
            int burned = 0;

            foreach (var (item, n) in _b.Input.Snapshot()) // 사본이라 순회 중 소비해도 안전
            {
                if (item == null || n <= 0 || IsRequired(item)) continue;
                if (!_b.Input.TryConsume(item, n)) continue;

                _shield = Mathf.Clamp(_shield + _so.ShieldValueOf(item) * n, 0f, max);
                burned += n;
            }

            if (burned > 0)
            {
                ShieldChanged?.Invoke(_shield, max);
                _b.NotifyUpstream(); // 자리가 비었다 — 막혀 있던 벨트 재개
            }
            return burned;
        }

        /// <summary>
        /// 수리로 늘어난 내구도. 확인창이 "코어 내구도 +1,500"이라 적는 그 수치와 같은 출처다 —
        /// 같은 값을 UI와 로직이 따로 들고 있으면 반드시 어긋나고, 어긋난 쪽이 UI면 플레이어가 속는다.
        ///
        /// 최대치만 올리고 그만큼 회복시킨다(전체 회복 아님). 수리는 선체를 덧대는 일이지
        /// 이미 난 상처를 없던 일로 만드는 게 아니다 — 밤에 깎인 체력이 공짜로 돌아오면
        /// 방어를 못해도 코어가 버티게 된다.
        /// </summary>
        void ApplyMaxHpBonus(int bonus)
        {
            if (bonus <= 0) return;

            // 체력의 원본은 심 엔티티(Owner.Health)다 — 뷰를 거치지 않으니 헤드리스 테스트에서도 같은 값이다.
            var hp = _b.Owner?.Health;
            if (hp == null) return;

            hp.SetMaxHealth(hp.MaxHealth + bonus, refill: false);
            hp.Heal(bonus);
        }

        void SetReady(bool on)
        {
            if (_ready == on) return;
            _ready = on;
            ReadyChanged?.Invoke();
        }

        /// <summary>
        /// 입구 규칙. 소각이 켜져 있으면 코어는 아무것도 거절하지 않는다 — 요구 부품은 쌓이고
        /// 나머지는 태워진다. 보호막이 가득이어도 마찬가지로 받아서 태운다(초과분은 소멸).
        ///
        /// 가득일 때 거절해 배압을 거는 안도 있지만 택하지 않았다. 그러면 잘못 연결된 벨트 하나가
        /// 코어 앞에서 정체를 만들고 그 정체가 상류 기계까지 stall시킨다 — 소각의 목적이
        /// "잘못 흘려보낸 자원이 라인을 막지 않게 하는 것"인데 정작 코어가 막는 꼴이 된다.
        /// 대신 보호막 상한이 방어력 무한 증가를 막는다. 초과분 소멸은 라인을 잘못 깐 대가다.
        ///
        /// 필터가 매번 현재 단계를 다시 읽으므로 단계가 올라도 다시 걸 필요는 없다. 그래도
        /// 배치·진화 시점에 한 번씩 부르는 것은 남겨 둔다 — 입구 규칙의 유일한 설치 지점이라는
        /// 사실이 호출부에서 보이는 편이 낫다.
        /// </summary>
        void RefreshAcceptFilter()
        {
            _b.Input.AcceptFilter = item =>
                item != null && (_so.burnSurplusIntoShield || IsRequired(item));
        }

        // ── 세이브 ────────────────────────────────────────────────────
        //
        // 티어는 GameManager가 갖고 있고(progress 모듈), 최대 보호막은 티어에서 계산되며,
        // 납품 재고는 입력 컨테이너에 들어 있다. 그래서 여기서 따로 챙길 것은 두 개뿐이다.

        public class SaveState
        {
            [JsonProperty("shield")] public float Shield;
            [JsonProperty("ready")] public bool Ready;
        }

        public object CaptureState() => new SaveState { Shield = _shield, Ready = _ready };

        public void RestoreState(JToken state)
        {
            var s = SaveJson.FromToken<SaveState>(state);
            if (s == null) return;

            _shield = Mathf.Clamp(s.Shield, 0f, MaxShield);
            _ready = s.Ready;   // SetReady를 쓰지 않는다 — 복원은 사건이 아니라 상태 이전이다

            ShieldChanged?.Invoke(_shield, MaxShield);
            RefreshAcceptFilter();
        }
    }
}
