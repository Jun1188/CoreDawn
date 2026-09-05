using UnityEngine;
using CoreDawn.Worlds;

namespace CoreDawn.Navigation
{
    /// <summary>
    /// 길찾기 비용 필드 — 지형·건물 비용의 정본이자 <b>플로우필드와 A*가 함께 보는 한 벌</b>.
    ///
    /// 왜 배열인가: 길찾기는 워커 스레드에서 돈다. 워커는 Unity API(맵 SO 조회, 심 딕셔너리)를
    /// 부를 수 없으므로, 메인 스레드가 그 결과를 미리 여기 떠 두고 워커는 배열만 읽는다.
    /// 덤으로 칸마다 하던 객체 조회가 인덱싱 한 번이 되어 탐색 자체도 빨라진다.
    ///
    /// 갱신은 <see cref="GridManager"/>가 한다 — 처음 한 번 전체를 굽고, 이후 건물이 놓이거나
    /// 부서진 자리만 다시 칠한다. 20만 칸을 매번 훑는 것과 몇 칸만 고치는 것의 차이다.
    /// </summary>
    public class CostField
    {
        public Vector2Int Size { get; private set; }

        /// <summary>진격 비용 — 건물은 "비싼 길"(HP 비례). <see cref="TileRules.Blocked"/> 이상이면 못 간다.</summary>
        public int[] EnterCost = System.Array.Empty<int>();

        /// <summary>보행 가능 — 건물은 막힌 것으로 본다(대각 모서리 판정·A*의 통행 판정).</summary>
        public bool[] Walkable = System.Array.Empty<bool>();

        /// <summary>내용이 바뀔 때마다 오른다 — 워커가 "내가 읽던 필드가 그 사이 갈렸나"를 볼 때 쓴다.</summary>
        public int Version { get; private set; }

        public bool IsReady => EnterCost.Length > 0;

        public void Resize(Vector2Int size)
        {
            int count = Mathf.Max(0, size.x * size.y);
            if (EnterCost.Length != count)
            {
                EnterCost = new int[count];
                Walkable = new bool[count];
            }
            Size = size;
            Version++;
        }

        public void Touch() => Version++;

        /// <summary>
        /// 다른 필드의 내용을 통째로 복사한다 — 워커가 읽을 스냅샷용. 메인은 살아 있는 배열을 칸 단위로 고치므로
        /// (설치·철거·피격마다) 워커에 그대로 주면 한 번의 탐색 안에서 옛/새 값이 섞인다. 복사 뒤 Version은 원본과
        /// 같아지므로 호출자는 <c>snapshot.Version != src.Version</c>일 때만 부르면 된다
        /// (121×121 맵 4등분 234k칸 int+bool = 1.2MB, Array.Copy 0.05ms 실측 — 2026-09-04).
        /// </summary>
        public void CopyFrom(CostField src)
        {
            Resize(src.Size);
            System.Array.Copy(src.EnterCost, EnterCost, EnterCost.Length);
            System.Array.Copy(src.Walkable, Walkable, Walkable.Length);
            Version = src.Version;
        }

        public int Index(Vector2Int cell) => cell.y * Size.x + cell.x;

        public bool InBounds(Vector2Int cell) =>
            cell.x >= 0 && cell.x < Size.x && cell.y >= 0 && cell.y < Size.y;

        /// <summary>이 칸에 발을 들이는 비용. 범위 밖이면 Blocked.</summary>
        public int CostAt(Vector2Int cell) =>
            InBounds(cell) ? EnterCost[Index(cell)] : TileRules.Blocked;

        /// <summary>걸어서 지날 수 있는 칸인가. 범위 밖이면 false.</summary>
        public bool WalkableAt(Vector2Int cell) =>
            InBounds(cell) && Walkable[Index(cell)];
    }
}
