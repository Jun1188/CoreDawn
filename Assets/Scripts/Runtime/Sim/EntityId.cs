using System;

namespace CoreDawn.Sim
{
    /// <summary>
    /// 엔티티의 정체성 — 월드가 발급하는 64비트 단조 증가 번호. 한 번 쓴 번호는 다시 쓰지 않는다.
    ///
    /// 참조(object) 대신 번호로 말하는 이유: 세이브·네트워크·모딩은 메모리 주소를 옮길 수 없고 번호는 옮길 수 있다.
    /// 마크식 UUID(128비트)를 쓰지 않는 이유: 그건 엔티티가 서버·차원·계정 사이를 넘나들 때 필요한 것이고,
    /// 이 게임의 월드 엔티티는 한 월드 안에서만 살며 발급자가 하나(서버 권위)라 카운터로 충돌이 없다.
    /// 플레이어(프로필)만 Guid를 쓴다 — 세션·서버를 넘어 같은 사람임을 보장해야 하는 건 그뿐이다.
    ///
    /// 0은 "없음"(<see cref="None"/>)이다 — 기본값이 곧 무효라 초기화를 빠뜨린 필드가 조용히 남의 엔티티를 가리키지 않는다.
    /// </summary>
    public readonly struct EntityId : IEquatable<EntityId>, IComparable<EntityId>
    {
        public readonly ulong Value;

        public static readonly EntityId None = default;

        public EntityId(ulong value) => Value = value;

        public bool IsNone => Value == 0;

        public bool Equals(EntityId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is EntityId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public int CompareTo(EntityId other) => Value.CompareTo(other.Value);

        public static bool operator ==(EntityId a, EntityId b) => a.Value == b.Value;
        public static bool operator !=(EntityId a, EntityId b) => a.Value != b.Value;

        public override string ToString() => IsNone ? "#none" : "#" + Value;
    }
}
