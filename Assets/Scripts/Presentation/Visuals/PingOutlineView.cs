using System.Collections.Generic;
using EPOOutline;
using UnityEngine;
using CoreDawn.Pings;
using Ping = CoreDawn.Pings.Ping;   // UnityEngine.Ping과 충돌
using CoreDawn.Sim;

namespace CoreDawn.Visuals
{
    /// <summary>
    /// 핑의 아웃라인 표현 — 대상이 있는 핑이 오면 그 오브젝트에 EPO 아웃라인을 켜고, 끝나면 이전 상태로 되돌린다.
    ///
    /// 되돌리는 이유: 같은 Outlinable을 다른 시스템도 쓴다 — 몬스터는 근접 표시가, 드롭 아이템은
    /// 조준 표시가 켜고 끈다. 핑이 끝났다고 무조건 끄면 그쪽 표시가 꺼지고, 색을 남겨 두면 그쪽 색이 바뀐다.
    /// 그래서 핑이 시작될 때의 (켜짐, 색)을 기억했다가 그대로 돌려놓는다.
    ///
    /// 위치 핑(대상 없음)은 여기서 그리지 않는다 — 그건 HUD 마커의 일이다(다음 구독자).
    /// </summary>
    public class PingOutlineView : MonoBehaviour
    {
        [Header("종류별 색 — 기본값은 드롭 아이템 아웃라인과 같은 계열")]
        [SerializeField] Color lookColor     = new(0.9539399f, 0.9996342f, 1.498039f, 1f);
        [SerializeField] Color tutorialColor = new(1.5f, 1.2f, 0.3f, 1f);
        [SerializeField] Color alertColor    = new(1.6f, 0.35f, 0.3f, 1f);
        [SerializeField] Color markerColor   = new(0.4f, 1.2f, 1.5f, 1f);

        [SerializeField, Range(0f, 1f)] float dilateShift = 0.6f;
        [SerializeField, Range(0f, 1f)] float blurShift = 0.1f;

        struct Held
        {
            public Outlinable Outline;
            public bool WasEnabled;
            public Color PrevColor;
        }

        readonly Dictionary<Ping, Held> held = new();

        void OnEnable()
        {
            PingService.Raised += OnRaised;
            PingService.Expired += OnExpired;

            // 늦게 켜졌으면 이미 살아 있는 핑을 따라잡는다
            var active = PingService.Active;
            for (int i = 0; i < active.Count; i++) OnRaised(active[i]);
        }

        void OnDisable()
        {
            PingService.Raised -= OnRaised;
            PingService.Expired -= OnExpired;

            foreach (var kv in held) Restore(kv.Value);
            held.Clear();
        }

        void OnRaised(Ping ping)
        {
            if (ping == null || !ping.HasTarget || held.ContainsKey(ping)) return;

            var o = EpoOutlines.Ensure(ping.Target);
            if (o == null) return;

            var h = new Held { Outline = o, WasEnabled = o.enabled, PrevColor = o.OutlineParameters.Color };
            held[ping] = h;

            EpoOutlines.Style(o, ColorOf(ping.Kind), dilateShift, blurShift);
            o.enabled = true;
        }

        void OnExpired(Ping ping)
        {
            if (ping == null || !held.TryGetValue(ping, out var h)) return;
            held.Remove(ping);
            Restore(h);
        }

        static void Restore(Held h)
        {
            if (h.Outline == null) return;   // 대상이 파괴됐다 — 되돌릴 것이 없다
            h.Outline.OutlineParameters.Color = h.PrevColor;
            h.Outline.enabled = h.WasEnabled;
        }

        Color ColorOf(PingKind kind) => kind switch
        {
            PingKind.Tutorial => tutorialColor,
            PingKind.Alert    => alertColor,
            PingKind.Marker   => markerColor,
            _                 => lookColor,
        };
    }
}
