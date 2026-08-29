using System;
using UnityEngine;
using CoreDawn.Combat;
using CoreDawn.FPS;
using CoreDawn.UI;
using CoreDawn.Factory;
using CoreDawn.Data;
using CoreDawn.Sim;

namespace CoreDawn.Factory
{
    /// <summary>
    /// 타워의 <b>보급</b> 담당 — 심(플레인 C#) 쪽 절반이다.
    ///
    /// 조준·발사는 씬 쪽 BattleTower가 한다. 심은 씬 좌표도 몬스터도 모르는 헤드리스
    /// 시뮬레이션이라(테스트가 씬 없이 돌아간다) 여기서 타깃을 찾을 수 없고,
    /// 사거리 탐색·총알 발사는 이미 BattleTower/SensorComponent에 있는 것을 재사용하는 편이 낫다.
    ///
    /// 그래서 역할을 이렇게 나눈다:
    ///   심(여기)      — 무엇을 받을지(AcceptFilter), 한 발 소비, 막힌 상류 깨우기
    ///   씬(BattleTower) — 언제 누구를 쏠지, 총알 생성
    ///
    /// 포탑이 입력 포트를 갖는다는 게 핵심이다. 벨트를 연결하면 탄약이 자동 보급되고,
    /// Building.Input 버퍼가 그대로 탄창이 된다 — 공장 배관이 그대로 보급선이 된다.
    /// </summary>
    public class TowerBehavior : IBuildingBehavior, IInteractiveBehavior
    {
        readonly BuildingModule _b;
        readonly AmmoConsumerModuleDef _data;

        public TowerBehavior(BuildingModule b, AmmoConsumerModuleDef data)
        {
            _b = b;
            _data = data;

            // 조립기가 현재 레시피의 재료만 받는 것과 같은 구조.
            // 거절된 push는 상류에 자연스러운 배압으로 전달된다.
            _b.Input.AcceptFilter = Accepts;
        }

        public string InteractPrompt => "탄약함 열기";

        public void Interact(PlayerController player)
        {
            // 보관함 = 입력 버퍼. 벨트가 넣는 곳과 같아서 화면에 보이는 것이 곧 저장소의 전부다.
            GameScreens.OpenContainer(_b.Input);
        }

        public AmmoConsumerModuleDef Def => _data;

        bool Accepts(ItemDef item)
        {
            if (item == null) return false;
            var filter = _data.AmmoFilter;
            if (filter == null || filter.Count == 0) return false;   // 비우면 아무것도 안 받는다
            return filter.Contains(item);
        }

        /// <summary>장전된 탄약이 하나라도 있는가.</summary>
        public bool HasAmmo => _b.Input.HasAny;

        /// <summary>
        /// 한 발 소비하고 그 탄약의 모듈(명중 효과 + 탄도)을 돌려준다. 탄약이 없으면 false.
        /// 소비한 탄이 발사의 전부를 정의한다 — 유탄을 먹인 박격포는 포물선 폭발탄을 쏜다.
        /// 크기 배율(damageMultiplier)은 씬 쪽(BattleTower)이 발사 스펙에 싣는다.
        /// 소비 후 상류를 깨워, 버퍼가 꽉 차 멈춰 있던 벨트가 다시 흐르게 한다.
        /// IsPassive여도 소비는 막지 않는다 — 감속 필드는 펄스마다 에너지 셀을 연료로 태운다.
        /// </summary>
        public bool TryConsumeRound(out AmmoModuleSO round)
        {
            round = null;

            foreach (var (item, n) in _b.Input.Snapshot())
            {
                if (n <= 0 || !_b.Input.TryConsume(item, 1)) continue;

                // 효과·탄도는 탄약(모듈)이 갖고 포탑은 배율·각도만 갖는다
                round = ItemAssets.Of(item)?.GetModule<AmmoModuleSO>();   // 탄의 프리팹·연출은 아직 SO 모듈에
                _b.NotifyUpstream();
                return true;
            }
            return false;
        }

        // 발사 판정은 씬(Update)이 주도하므로 심 틱에서 할 일이 없다 — 그래서 깨울 필요도 없다.
        // 탄약이 벨트로 오든 플레이어가 탄약함에 손으로 넣든, 다음 발사 때 Input에서 바로 꺼낸다.
        public void Tick(float dt) { }

        public void OnAfterPlaced() { }
    }
}
