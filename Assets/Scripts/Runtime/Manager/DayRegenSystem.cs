using UnityEngine;
using CoreDawn.DayTime;
using CoreDawn.Entities;
using CoreDawn.Worlds;
using CoreDawn.Factory;
using CoreDawn.Sim;

namespace CoreDawn.Managers
{
    /// <summary>
    /// 낮 시간 회복 — 로직만, 배선 없음.
    ///   플레이어: 코어에서 20칸 안에 있으면 0.5초마다 1 회복 (거점 근처가 안전지대라는 신호).
    ///   코어:     1초마다 10 회복 (밤에 깎인 체력이 다음 밤 전까지 서서히 돌아온다).
    /// 밤에는 아무것도 하지 않는다 — 회복은 낮의 보상이다.
    ///
    /// 참조는 전부 저빈도 재탐색으로 잡는다(1초에 한 번). 코어는 파괴·재건될 수 있고
    /// 플레이어는 부활로 갈아끼워질 수 있어, 시작 시점 캐시는 믿을 수 없다.
    /// </summary>
    public class DayRegenSystem : MonoBehaviour
    {
        [Header("플레이어")]
        [Tooltip("한 번에 회복되는 양.")]
        [SerializeField] float playerHealAmount = 1f;
        [Tooltip("회복 간격(초).")]
        [SerializeField] float playerHealInterval = 0.5f;
        [Tooltip("코어에서 이 거리(칸) 안에 있어야 회복된다.")]
        [SerializeField] float playerHealRangeCells = 20f;

        [Header("코어")]
        [SerializeField] float coreHealAmount = 10f;
        [SerializeField] float coreHealInterval = 1f;

        float _playerTimer, _coreTimer, _rescanTimer;
        Player _player;
        Building _core;
        World _world;

        Health CoreHealth => _core?.Owner?.Health;

        void Update()
        {
            var tm = TimeManager.Instance;
            if (tm == null || tm.Phase != DayPhase.Day)
            {
                _playerTimer = _coreTimer = 0f;   // 밤을 건너뛰고 아침에 몰아서 받지 못하게
                return;
            }

            _rescanTimer -= Time.deltaTime;
            if (_rescanTimer <= 0f) { _rescanTimer = 1f; Rescan(); }

            _coreTimer += Time.deltaTime;
            if (_coreTimer >= coreHealInterval)
            {
                _coreTimer -= coreHealInterval;
                var coreHp = CoreHealth;
                if (coreHp != null && !coreHp.IsDead && coreHp.CurrentHealth < coreHp.MaxHealth)
                    coreHp.Heal(coreHealAmount);
            }

            _playerTimer += Time.deltaTime;
            if (_playerTimer >= playerHealInterval)
            {
                _playerTimer -= playerHealInterval;
                if (_player == null || _player.IsDead || _core == null) return;

                float cell = _world != null ? _world.CellSize : 2f;
                float range = playerHealRangeCells * cell;
                bool nearCore = (_player.transform.position - _core.Owner.Position).sqrMagnitude <= range * range;
                if (nearCore && _player.Health.CurrentHealth < _player.Health.MaxHealth)
                    _player.Health.Heal(playerHealAmount);
            }
        }

        void Rescan()
        {
            if (_player == null || _player.IsDead) _player = FindFirstObjectByType<Player>();
            if (_world == null) _world = FindFirstObjectByType<World>();
            if (_core == null || _core.IsRemoved || CoreHealth == null || CoreHealth.IsDead)
            {
                _core = null;
                var boot = FactoryBootstrap.Instance;
                if (boot == null || boot.Factory == null) return;
                foreach (var b in boot.Factory.Buildings)
                    if (b.IsCore && b.Owner?.Health != null && !b.Owner.Health.IsDead) { _core = b; break; }
            }
        }
    }
}
