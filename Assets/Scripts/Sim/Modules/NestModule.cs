using System;
using System.Collections.Generic;
using UnityEngine;

namespace CoreDawn.Sim
{
    /// <summary>둥지 스폰 포인트 한 곳의 심 상태 — 자리·보스 유무·보스 엔티티·파괴 여부. 파괴는 영구다.</summary>
    public sealed class NestPoint
    {
        public Vector3 Position { get; internal set; }
        /// <summary>이 자리에 보스가 서는가(맵의 spawnPoints[].boss). 보스 종류(프리팹)는 아직 뷰의 것.</summary>
        public bool HasBoss { get; internal set; }
        /// <summary>지금 이 자리에 붙어 있는 보스. 없거나 죽었으면 null 또는 !IsAlive.</summary>
        public Entity Boss { get; internal set; }
        public bool IsDestroyed { get; internal set; }

        public bool BossAlive => Boss != null && Boss.IsAlive;
    }

    /// <summary>
    /// 둥지 — 심 모듈. 스폰 포인트의 파괴, 둥지 자체의 파괴, 그리고 <b>무적 규칙</b>(스폰 포인트가 하나라도 살아 있으면 피해를 받지 않는다)을
    /// <see cref="IDamageInterceptor"/>로 심 안에서 끝낸다. 보스의 <b>죽음</b>은 엔티티 이벤트로 듣는다.
    /// <b>복구는 없다</b> — 보스를 잡은 자리는 영구히 비고, 부순 둥지는 다시 서지 않으며 웨이브의 출구에서 빠진다(자극만 남긴다 — WaveSystem).
    ///
    /// 보스를 <b>세우는</b> 것은 아직 뷰다 — 프리팹(뷰 카탈로그)·종류(NestView.bossId)가 뷰 저작이라, 모듈은 <see cref="BossNeeded"/>로 "이 자리에 보스가 필요하다"고만
    /// 말하고 뷰가 세워 <see cref="BindBoss"/>로 잇는다(5a-3 카탈로그 뒤에는 심이 직접 세운다). 낮 방어 스폰의 시점·자리(플레이어 거리·화면 가림)는
    /// 뷰에 남아 있다 — 가림 판정이 PhysX라 5단계 과제.
    /// </summary>
    public sealed class NestModule : EntityModule, IDamageInterceptor
    {
        public NestModuleDef Def { get; }

        readonly List<NestPoint> _points = new List<NestPoint>();
        public IReadOnlyList<NestPoint> Points => _points;

        public bool IsDestroyed { get; private set; }

        /// <summary>스폰 포인트가 하나라도 살아 있으면 둥지는 무적이다 — 보스(들)를 먼저 잡아야 한다.</summary>
        public bool IsInvulnerable
        {
            get
            {
                foreach (var p in _points) if (!p.IsDestroyed) return true;
                return false;
            }
        }

        public bool HasConfiguredPoints => _points.Count > 0;
        public bool HasLivePoint { get { foreach (var p in _points) if (!p.IsDestroyed) return true; return false; } }

        /// <summary>(자리 번호) — 이 자리에 보스가 필요하다. 뷰가 세우고 <see cref="BindBoss"/>로 잇는다.</summary>
        public event Action<int> BossNeeded;
        /// <summary>(자리 번호) — 보스가 죽어 자리가 파괴됐다(영구).</summary>
        public event Action<int> PointDestroyed;
        public event Action Destroyed;

        public NestModule(NestModuleDef def) { Def = def ?? throw new ArgumentNullException(nameof(def)); }

        protected internal override void OnAttach()
        {
            Owner.Health?.AddInterceptor(this);
            Owner.Died += OnOwnerDied;
        }

        protected internal override void OnDetach()
        {
            Owner.Health?.RemoveInterceptor(this);
            Owner.Died -= OnOwnerDied;
            foreach (var p in _points) p.Boss = null;
        }

        // ── 무적 규칙 ──
        public float Intercept(float amount, Entity source) => IsInvulnerable ? 0f : amount;

        // ── 구성 (배치 때 한 번: 맵 스펙 또는 프리팹의 포인트) ──

        /// <summary>스폰 포인트를 정한다 — 자리·보스 유무. 이미 있던 자리는 상태(파괴·보스)를 지키고 위치만 옮긴다.</summary>
        public void ConfigurePoints(IReadOnlyList<(Vector3 position, bool hasBoss)> points)
        {
            for (int i = 0; i < points.Count; i++)
            {
                if (i < _points.Count) { _points[i].Position = points[i].position; _points[i].HasBoss = points[i].hasBoss; }
                else _points.Add(new NestPoint { Position = points[i].position, HasBoss = points[i].hasBoss });
            }
            while (_points.Count > points.Count) { _points[^1].Boss = null; _points.RemoveAt(_points.Count - 1); }
        }

        // ── 보스 ──

        /// <summary>뷰가 세운 보스를 자리에 잇는다. 죽으면 자리가 파괴된다(이벤트로 듣는다).</summary>
        public void BindBoss(int index, Entity boss)
        {
            if (index < 0 || index >= _points.Count) throw new ArgumentOutOfRangeException(nameof(index));
            var p = _points[index];
            p.Boss = boss;
            if (boss != null)
            {
                if (!boss.IsAlive) { OnBossDied(index); return; }
                boss.Died += _ => OnBossDied(index);
            }
        }

        public void ClearBoss(int index)
        {
            if (index < 0 || index >= _points.Count) return;
            _points[index].Boss = null;
        }

        /// <summary>보스가 서야 하는데 없는 자리마다 <see cref="BossNeeded"/> — 시작 때 뷰가 부른다.</summary>
        public void RequestMissingBosses()
        {
            for (int i = 0; i < _points.Count; i++)
            {
                var p = _points[i];
                if (p.IsDestroyed || !p.HasBoss || p.BossAlive) continue;
                BossNeeded?.Invoke(i);
            }
        }

        void OnBossDied(int index)
        {
            var p = _points[index];
            if (p.IsDestroyed) return;
            p.IsDestroyed = true;
            PointDestroyed?.Invoke(index);
        }

        // ── 둥지 파괴 (영구) ──

        void OnOwnerDied(Entity _)
        {
            if (IsDestroyed) return;
            IsDestroyed = true;
            Destroyed?.Invoke();
        }

        // ── 세이브 복원 ──
        public void RestoreState(bool isDestroyed) => IsDestroyed = isDestroyed;

        public void RestorePoint(int index, bool destroyed)
        {
            if (index < 0 || index >= _points.Count) return;
            _points[index].IsDestroyed = destroyed;
        }
    }
}
