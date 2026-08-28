using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 핑의 단일 창구 — 찍는 쪽과 그리는 쪽을 여기서 떼어 놓는다.
///
///   찍는 쪽  플레이어 입력(T)·튜토리얼·시스템 알림·(나중에) 네트워크 수신 → <see cref="Raise"/>
///   그리는 쪽  아웃라인·HUD 마커·미니맵·소리·(나중에) 네트워크 송신 → <see cref="Raised"/>/<see cref="Expired"/> 구독
///
/// 둘 다 서로를 모른다. 튜토리얼이 "코어를 가리켜라"고 할 때 아웃라인 코드를 부르지 않고
/// 핑을 하나 올릴 뿐이며, 그 핑을 어떻게 보여줄지는 구독자마다 정한다.
///
/// 정적 서비스인 이유는 CrowdSystem·RecipeRewardUnlockService와 같다 — 씬 배선 없이 어디서든
/// 부를 수 있어야 하고, 상태(활성 핑 목록)는 씬을 넘어 살아도 된다. 만료 처리를 위해
/// 첫 핑 때 숨은 러너를 하나 만든다.
/// </summary>
public static class PingService
{
    /// <summary>기본 지속 시간(초). 호출자가 안 주면 이 값.</summary>
    public const float DefaultDuration = 3f;

    static readonly List<Ping> active = new();
    static readonly List<Ping> expiredBuffer = new();
    static PingServiceRunner runner;
    static int nextId = 1;

    /// <summary>새 핑이 올라왔다.</summary>
    public static event Action<Ping> Raised;

    /// <summary>핑이 끝났다 — 시간이 다 됐거나, 같은 대상에 새 핑이 덮였거나, 지워졌다.</summary>
    public static event Action<Ping> Expired;

    /// <summary>지금 살아 있는 핑들. 새로 붙는 표시(늦게 열린 HUD 등)가 현재 상태를 따라잡을 때 읽는다.</summary>
    public static IReadOnlyList<Ping> Active => active;

    // 도메인 리로드를 끈 환경(Enter Play Mode Options)에서 static이 플레이를 넘어 살아남는 것 방지
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        active.Clear();
        expiredBuffer.Clear();
        runner = null;
        nextId = 1;
        Raised = null;
        Expired = null;
    }

    /// <summary>
    /// 대상을 찍는다. 같은 대상·같은 종류·같은 출처의 핑이 이미 살아 있으면 그것을 끝내고 새로 올린다 —
    /// 연타는 "다시 찍었다"이지 "두 개"가 아니다.
    /// </summary>
    public static Ping Raise(GameObject target, PingKind kind, PingSource source = PingSource.LocalPlayer,
                             float duration = DefaultDuration, string label = null)
    {
        if (target == null) return null;
        return Add(kind, source, target, target.transform.position, label, duration);
    }

    /// <summary>대상 없이 위치를 찍는다 — 집결 지점, "저쪽" 같은 것.</summary>
    public static Ping RaiseAt(Vector3 position, PingKind kind, PingSource source = PingSource.LocalPlayer,
                               float duration = DefaultDuration, string label = null)
        => Add(kind, source, null, position, label, duration);

    /// <summary>핑을 지금 끝낸다. 튜토리얼이 "이제 됐다"고 할 때.</summary>
    public static void Clear(Ping ping)
    {
        if (ping == null || !active.Remove(ping)) return;
        Expired?.Invoke(ping);
    }

    public static void ClearAll()
    {
        expiredBuffer.Clear();
        expiredBuffer.AddRange(active);
        active.Clear();
        foreach (var p in expiredBuffer) Expired?.Invoke(p);
        expiredBuffer.Clear();
    }

    static Ping Add(PingKind kind, PingSource source, GameObject target, Vector3 position, string label, float duration)
    {
        // 같은 대상을 같은 뜻으로 다시 찍었다 → 앞 것을 접고 새로 (지속 시간이 처음부터 다시 간다)
        if (target != null)
            for (int i = active.Count - 1; i >= 0; i--)
                if (active[i].Target == target && active[i].Kind == kind && active[i].Source == source)
                    Clear(active[i]);

        var ping = new Ping(nextId++, kind, source, target, position, label, Time.time, Mathf.Max(0.05f, duration));
        active.Add(ping);
        EnsureRunner();
        Raised?.Invoke(ping);
        return ping;
    }

    /// <summary>만료 처리 — 러너가 매 프레임 부른다. 대상이 파괴된 핑도 여기서 접는다.</summary>
    internal static void Tick(float now)
    {
        if (active.Count == 0) return;

        expiredBuffer.Clear();
        for (int i = active.Count - 1; i >= 0; i--)
        {
            var p = active[i];
            // 대상을 들고 있었는데 파괴됐다 — C# 참조는 남아 있고(is not null) 유니티 비교로는 죽었다(== null).
            // 위치 핑은 참조 자체가 없어(is null) 해당 없음
            bool targetGone = p.Target is not null && p.Target == null;
            if (now >= p.ExpiresAt || targetGone)
            {
                active.RemoveAt(i);
                expiredBuffer.Add(p);
            }
        }
        foreach (var p in expiredBuffer) Expired?.Invoke(p);
        expiredBuffer.Clear();
    }

    static void EnsureRunner()
    {
        if (runner != null || !Application.isPlaying) return;
        var go = new GameObject("PingService (Runtime)") { hideFlags = HideFlags.DontSave };
        UnityEngine.Object.DontDestroyOnLoad(go);
        runner = go.AddComponent<PingServiceRunner>();
    }

    /// <summary>만료 시계 — 서비스가 스스로 만든다. 씬에 두지 말 것.</summary>
    sealed class PingServiceRunner : MonoBehaviour
    {
        void Update() => Tick(Time.time);
    }
}
