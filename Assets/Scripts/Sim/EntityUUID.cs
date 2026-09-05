using System;

namespace CoreDawn.Sim
{
    /// <summary>
    /// 엔티티의 정체성 — 128비트 UUID(v4). 만드는 쪽이 조율 없이 찍는다.
    ///
    /// 카운터 대신 UUID인 이유(2026-08-29 결정): "발급자가 하나"라는 가정을 버리기 위해서다.
    /// 서버 권위 멀티플레이에서 클라이언트가 예측으로 먼저 만드는 것(건물 고스트·투사체), 구조물 붙여넣기·세이브 병합·
    /// 서버 간 이동·모드 도구가 만든 엔티티는 모두 다른 발급자의 것인데, 카운터면 매번 번호를 다시 매기고
    /// 세이브 안의 참조(소유자·표적)까지 따라 고쳐야 한다. UUID는 그대로 간다.
    /// 세션용 정수 핸들(패킷에 매 틱 실리는 것)은 넷코드 라이브러리가 따로 주므로 여기서 카운터를 겹쳐 두지 않는다.
    /// 플레이어(프로필)도 Guid라 타입이 하나로 통일된다.
    ///
    /// 세이브에는 문자열("N", 32자)로 적는다 — 5단계(SharpNBT)에서 엔티티 레코드에 실린다.
    /// 기본값(<see cref="None"/>, Guid.Empty)은 곧 무효라 초기화를 빠뜨린 필드가 조용히 남의 엔티티를 가리키지 않는다.
    /// 로그에는 앞 8자리만 찍는다(<see cref="ToString"/>) — 한 월드 안에서 구분하기엔 충분하다.
    /// (이름이 EntityId가 아닌 이유: Unity 6의 UnityEngine.EntityId와 겹친다.)
    /// </summary>
    public readonly struct EntityUUID : IEquatable<EntityUUID>, IComparable<EntityUUID>
    {
        public readonly Guid Value;

        public static readonly EntityUUID None = default;

        public EntityUUID(Guid value) => Value = value;

        /// <summary>새 정체성. 조율 없이 어디서든(서버·클라이언트 예측·도구) 부를 수 있다.</summary>
        public static EntityUUID New() => new EntityUUID(Guid.NewGuid());

        public bool IsNone => Value == Guid.Empty;

        /// <summary>세이브용 표기("N", 32자 hex). 되돌리기는 <see cref="TryParse"/>.</summary>
        public string ToSaveString() => Value.ToString("N");

        public static bool TryParse(string s, out EntityUUID id)
        {
            if (Guid.TryParse(s, out var g)) { id = new EntityUUID(g); return true; }
            id = None;
            return false;
        }

        public bool Equals(EntityUUID other) => Value.Equals(other.Value);
        public override bool Equals(object obj) => obj is EntityUUID other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public int CompareTo(EntityUUID other) => Value.CompareTo(other.Value);

        public static bool operator ==(EntityUUID a, EntityUUID b) => a.Value == b.Value;
        public static bool operator !=(EntityUUID a, EntityUUID b) => a.Value != b.Value;

        public override string ToString() => IsNone ? "#none" : "#" + Value.ToString("N").Substring(0, 8);
    }
}
