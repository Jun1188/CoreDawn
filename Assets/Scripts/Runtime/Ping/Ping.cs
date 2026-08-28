using UnityEngine;

/// <summary>핑의 뜻 — 소비자(아웃라인·HUD 마커·알림)가 표현을 고르는 축.</summary>
public enum PingKind
{
    /// <summary>플레이어가 바라본 것을 찍었다 — "저거".</summary>
    Look = 0,
    /// <summary>위치 표시 — 대상 없이 지점만. 이동·집결.</summary>
    Marker = 1,
    /// <summary>튜토리얼이 가리키는 것 — "여기로 가라 / 이걸 눌러라".</summary>
    Tutorial = 2,
    /// <summary>경고 — 코어 피격, 둥지 출현 같은 시스템 알림.</summary>
    Alert = 3,
}

/// <summary>누가 찍었는가 — 표현(색·소리)과 전파(멀티플레이 송신 여부)를 가른다.</summary>
public enum PingSource
{
    LocalPlayer = 0,
    RemotePlayer = 1,
    System = 2,
}

/// <summary>
/// 핑 한 건 — 불변 스냅샷. 대상이 있으면 <see cref="Target"/>, 위치만이면 null이고 <see cref="Position"/>만 유효하다.
///
/// 대상 참조와 위치를 둘 다 드는 이유: 대상이 파괴되면(몬스터 사망) 참조는 유령이 되지만
/// "거기서 무슨 일이 있었다"는 마커·알림은 남아야 한다. 위치는 찍힌 순간의 스냅샷이다.
/// 멀티플레이 전송·저장 시에는 대상 대신 위치와 라벨을 보내면 된다.
/// </summary>
public sealed class Ping
{
    public readonly int Id;
    public readonly PingKind Kind;
    public readonly PingSource Source;

    /// <summary>찍힌 대상의 루트. 위치 핑이면 null.</summary>
    public readonly GameObject Target;

    /// <summary>찍힌 순간의 월드 위치 — 대상이 사라져도 남는다.</summary>
    public readonly Vector3 Position;

    /// <summary>표시 문구(선택). 알림·마커가 쓴다. null이면 소비자가 대상 이름으로 대신한다.</summary>
    public readonly string Label;

    public readonly float RaisedAt;
    public readonly float Duration;

    public float ExpiresAt => RaisedAt + Duration;
    public bool HasTarget => Target != null;

    internal Ping(int id, PingKind kind, PingSource source, GameObject target, Vector3 position,
                  string label, float raisedAt, float duration)
    {
        Id = id; Kind = kind; Source = source; Target = target; Position = position;
        Label = label; RaisedAt = raisedAt; Duration = duration;
    }

    public override string ToString()
        => $"Ping#{Id} {Kind}/{Source} {(Target != null ? Target.name : Position.ToString())}";
}
