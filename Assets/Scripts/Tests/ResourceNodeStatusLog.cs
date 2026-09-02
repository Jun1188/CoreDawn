using System.Collections;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Serialization;
using CoreDawn.Sim;
using CoreDawn.Factory;
namespace CoreDawn.Tests
{
    /// <summary>
    /// 광맥·채굴기가 실제로 돌고 있는지를 콘솔에 찍는 관찰용 컴포넌트.
    ///
    /// 테스트 하네스와 무관하게 씬의 모든 광맥을 스스로 훑기 때문에, 플레이어가 B키 빌드 메뉴로
    /// 직접 지은 채굴기도 그대로 잡힌다 (자동 설치 같은 건 하지 않는다 — 보기만 한다).
    ///
    /// 로그 규칙:
    ///   · 변화가 있을 때만 찍는다 (가만히 있으면 조용하다 → 로그 폭탄 방지)
    ///   · 멈춰 있으면 그 사유를 한 번만 알린다 (정상 정지인지 고장인지 구분되게)
    ///
    /// "재고가 5/5로 고정"은 멈춘 게 아니라 생산이 채굴보다 빠른 정상 상태다.
    /// 진행 판정은 누적 채굴량(ResourceDepositModule.TotalExtracted)으로 한다.
    /// </summary>
    public class ResourceNodeStatusLog : MonoBehaviour
    {
        [Tooltip("상태를 확인하는 주기(초). 변화가 없으면 로그는 나가지 않는다.")]
        [SerializeField] private float period = 2f;

        // 이름을 'tag'로 두면 Component.tag를 가려 CS0108이 난다 (씬에 저장된 값은 FormerlySerializedAs로 승계)
        [Tooltip("로그 앞에 붙는 머리말.")]
        [FormerlySerializedAs("tag")]
        [SerializeField] private string logPrefix = "[광맥]";

        int  _lastTotal = -1;
        int  _quietRounds;
        bool _warned;

        IEnumerator Start()
        {
            // 다른 Start(FactoryBootstrap·광맥 등록)가 다 돈 뒤에 첫 스냅샷을 잡는다
            yield return null;
            _lastTotal = TotalExtracted();

            var wait = new WaitForSeconds(Mathf.Max(0.1f, period));

            while (true)
            {
                yield return wait;

                int total = TotalExtracted();

                if (total != _lastTotal)
                {
                    Debug.Log($"{logPrefix} 채굴 진행 (+{total - _lastTotal}/{period:0}초) — {StatusLine()}");
                    _lastTotal   = total;
                    _quietRounds = 0;
                    _warned      = false;
                    continue;
                }

                if (++_quietRounds < 3 || _warned) continue;

                Debug.Log($"{logPrefix} 채굴 없음 — {StatusLine()}. {StallReason()}");
                _warned = true;
            }
        }

        static FactorySystem Sim => FactoryBootstrap.Instance != null ? FactoryBootstrap.Instance.Factory : null;
        static int TotalExtracted() => Sim != null ? Sim.Deposits.Sum(d => d.TotalExtracted) : 0;

        /// <summary>광맥별 재고 + 그 위 채굴기 유무를 한 줄로.</summary>
        static string StatusLine()
        {
            var sb = new StringBuilder();

            if (Sim != null) foreach (var n in Sim.Deposits)
            {
                if (sb.Length > 0) sb.Append(" · ");
                sb.Append($"{n.Resource?.Id}@{n.Cell} 누적 {n.TotalExtracted}");
                sb.Append(MinerOn(n) ? " 채굴기O" : " 채굴기X");
            }

            return sb.Length == 0 ? "씬에 광맥이 없습니다" : sb.ToString();
        }

        static string StallReason()
        {
            bool anyMiner = Sim != null && Sim.Deposits.Any(MinerOn);

            return anyMiner
                ? "채굴기는 서 있습니다 → 출력이 막혔습니다(저장고 가득)."
                : "광맥 위에 채굴기가 없습니다 → B키 빌드 메뉴에서 채굴기를 광맥 위에 지으면 시작됩니다.";
        }

        /// <summary>광맥 풋프린트 안에 건물이 서 있는가 (심이 없으면 판단 불가 → false).</summary>
        static bool MinerOn(ResourceDepositModule node)
        {
            var sim = Sim;
            if (sim == null) return false;
            return sim.Grid.GetAt(node.Cell) is { IsRemoved: false } b && b.Def.Has<ExtractorModuleDef>();
        }
    }
}
