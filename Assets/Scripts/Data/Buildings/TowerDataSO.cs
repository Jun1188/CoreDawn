using System;
using UnityEngine;
using CoreDawn.Combat;
using CoreDawn.FPS;
using CoreDawn.UI;
using CoreDawn.Factory;

namespace CoreDawn.Data
{
    /// <summary>
    /// 방어 타워 — 벨트로 탄약을 보급받아 밤 웨이브를 막는다.
    ///
    /// 피해를 배율로 둔 이유: 탄약이 강해지면 그 탄을 쓰는 모든 포탑이 함께 강해져야 한다.
    /// 포탑마다 고정 피해를 박아두면 새 탄약을 추가할 때마다 모든 포탑 수치를 다시 만져야 한다.
    /// 1발의 명중 효과(피해 포함)는 탄약의 <see cref="AmmoModuleSO.attackEffects"/>가 갖는다.
    /// </summary>
    [CreateAssetMenu(fileName = "NewTower", menuName = "Factory/Buildings/Tower")]
    public class TowerDataSO : BuildingDataSO
    {
        [Header("전투")]
        [Tooltip("전달 방식 — Projectile(총알)·Hitscan(즉시 판정, 레이저)·Aura(반경 펄스, 감속 필드).\n" +
                 "어느 방식이든 효과는 소비한 탄약/연료(AmmoModuleSO)가 정의한다.")]
        public FireMode fireMode = FireMode.Projectile;

        // 쏘는 건물은 몬스터에게 위협이라 먼저 노려진다 — 비전투 구조물(인펜스 등)은 일반 건물과 같다.
        // (BuildingDataSO.threatSeedCost의 기본값을 타워에서만 낮춘다)
        private void Reset() => threatSeedCost = 10;

        [Tooltip("탄약 효과 중 피해형(Damage·DoT) 항목에 곱하는 배율 — 감속 같은 부가 효과는 그대로.")]
        public float damageMultiplier = 1f;

        [Tooltip("사거리(m) — 플레이어 총과 같은 단위. 정본은 팩(Turret.range/AuraEmitter.radius/Trigger.radius).")]
        public float range = 8f;

        [Tooltip("최소 사거리 — 이보다 가까운 적은 조준하지 못한다(박격포의 사각). 0 = 제한 없음.")]
        public float minRange;

        [Tooltip("발/초. Aura는 펄스 주기의 역수 (0.2 = 5초마다 펄스).")]
        public float fireRate = 1f;

        [Header("조준 — 탄도(탄속·중력·폭발)는 탄약(AmmoModuleSO)의 성질, 발사기는 각도만 정한다")]
        [Tooltip("총구 위치 오프셋(로컬 높이) — 타워 콜라이더 밖에서 발사되도록.")]
        public float muzzleHeight = 1.2f;

        [Tooltip("중력 있는 탄을 쏠 때 고각 궤적을 고른다 — 박격포는 장애물을 넘겨 쏘고, 직사 발사기는 저각으로 빨리 닿는다.")]
        public bool preferHighArc;

        [Tooltip("포탑 선회 속도(도/초). 0이면 포탑이 없거나 즉시 조준 — 그 경우 조준 대기 없이 바로 쏜다.\n" +
                 "연출이 아니라 밸런스 수치다: 느린 포탑은 목표가 바뀔 때마다 실질 연사가 떨어진다.")]
        public float turnSpeed = 180f;

        [Tooltip("조준 완료로 인정하는 좌우 오차(도). 이 안에 들어와야 발사한다.\n" +
                 "요(좌우)만 본다 — 피치는 탄종의 중력에 따라 달라져서 발사 전에는 확정할 수 없다.")]
        public float aimTolerance = 3f;

        [Header("보급")]
        [Tooltip("이 포탑이 받을 수 있는 탄약·연료. 비우면 아무것도 소비하지 않는다.\n" +
                 "무엇을 먹을 수 있는가가 곧 포탑의 성격이다 — 기본 포탑은 다 받고, 중기관은 고밀도 이상만.")]
        public ItemDataSO[] ammoFilter;

        [Tooltip("무공급 폴백 탄 — 심 없이 씬에 직접 놓인 타워가 무한 사격할 때 가정하는 탄약.\n" +
                 "벨트 보급 타워(심 배치)에서는 쓰이지 않는다 — 그쪽은 실제 소비한 탄이 정의한다.")]
        public ItemDataSO defaultAmmo;


        /// <summary>발사하지 않는 건물인가 — 오라(감속 필드)는 쏘는 대신 펄스한다.</summary>
        public bool IsPassive => fireMode == FireMode.Aura;
    }

    // ─── 행동 ──────────────────────────────────────────────────────

}
