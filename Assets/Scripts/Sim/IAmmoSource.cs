using System;

namespace CoreDawn.Sim
{
    /// <summary>
    /// 발사기(포탑·오라·기폭)가 "쏠 수 있나 · 이번 발은 무엇인가"를 묻는 곳. 발사기는 효과를 모른다 — 탄이 정한다.
    ///
    /// 구현 둘: <see cref="AmmoConsumerModule"/>(탄창에서 한 발 소비, 배율)과 <see cref="FixedAmmoModule"/>(자기 정의의 탄 —
    /// 무한, 소비 없음; 지뢰·연료 없는 오라). 발사기는 <c>Owner.Get&lt;IAmmoSource&gt;()</c>로 찾는다(모듈 조회는 인터페이스도 본다).
    /// </summary>
    public interface IAmmoSource
    {
        /// <summary>지금 쏠 탄이 있는가. 고정 탄은 항상 true.</summary>
        bool HasAmmo { get; }

        /// <summary>다음 발의 탄 성질 — 소비하지 않고 본다(곡사 여부·탄속을 미리 알아야 조준할 수 있다). 없으면 false.</summary>
        bool TryPeek(out AmmoModuleDef ammo, out ItemDef round);

        /// <summary>한 발 꺼낸다 — 소비형은 탄창이 줄고, 고정형은 그대로. round는 탄 아이템(고정 탄이면 null — 뷰의 프리팹 조회용).</summary>
        bool TryTake(out AmmoModuleDef ammo, out ItemDef round);

        /// <summary>탄의 효과 목록에 발사기 배율·소유자 버프를 구운 최종 목록. 발사 시점에 확정된다.</summary>
        Effect[] Bake(AmmoModuleDef ammo);

        /// <summary>한 발 소비했다 — 공장 행동이 듣고 상류를 깨운다. 고정 탄은 발화하지 않는다.</summary>
        event Action<ItemDef> Consumed;
    }
}
