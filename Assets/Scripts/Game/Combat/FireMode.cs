using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;
using CoreDawn.Entities;
using CoreDawn.Sim;
using CoreDawn.FPS;
using CoreDawn.Factory;
using CoreDawn.UI;
using CoreDawn.Data;
using CoreDawn.Visuals;

namespace CoreDawn.Combat
{
    /// <summary>
    /// 전달 방식 — 효과를 대상에게 어떻게 전달하는가. 클래스 상속이 아니라 데이터가 정하며,
    /// 총(GunDef)과 타워(TurretModuleDef)가 공용으로 쓴다.
    /// </summary>
    public enum FireMode
    {
        Projectile, // 발사체가 날아가 명중한 하나에게 — 탄속·탄도 있음
        Hitscan,    // 즉시 판정으로 명중한 하나에게 — 속도 무한의 발사 (레이저·저격)
        Aura,       // 원점 반경의 전원에게 (펄스) — 감속 필드 등
        None,       // 전달하지 않음 — 비전투 구조물 (인펜스 같은 순수 장애물)
        Trigger,    // 접촉 기폭 — 발사기가 아니라 덫(지뢰). 판정은 심 TriggerModule, 전달 계층은 쓰지 않는다
    }
}
