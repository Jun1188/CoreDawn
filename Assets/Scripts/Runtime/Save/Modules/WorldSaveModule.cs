using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>
/// 월드에 흩어져 있는 것들 — 바닥에 떨어진 아이템, 광맥 재고, 씬에 놓인 상자.
///
/// 공장(Order 20) 다음에 복원된다(Order 30). 광맥의 다음 생산 시각이 심 클럭 기준
/// 절대 시각이라, 공장 모듈이 시계를 되돌린 뒤여야 의미가 맞기 때문이다.
/// </summary>
public class WorldSaveModule : ISaveModule
{
    public string ModuleId => "world";
    public int Order => 30;

    // ── DTO ──────────────────────────────────────────────────────

    public class Dto
    {
        [JsonProperty("drops")] public List<DropDto> Drops = new();
        [JsonProperty("nodes")] public List<NodeDto> Nodes = new();
        [JsonProperty("chests")] public List<ChestDto> Chests = new();
    }

    public class DropDto
    {
        [JsonProperty("item")] public string ItemId;
        [JsonProperty("n")] public int Amount;
        [JsonProperty("pos")] public Vector3 Position;
    }

    public class NodeDto
    {
        /// <summary>광맥은 움직이지 않으므로 점유 칸이 곧 신원이다.</summary>
        [JsonProperty("cell")] public Vector2Int Origin;

        [JsonProperty("stock")] public int CurrentStock;
        [JsonProperty("nextAt")] public float NextProduceAt;
        [JsonProperty("extracted")] public int TotalExtracted;
    }

    public class ChestDto
    {
        [JsonProperty("path")] public string ScenePath;
        [JsonProperty("items")] public SaveContainerDto Container;
    }

    // ── 저장 ──────────────────────────────────────────────────────

    public object Capture()
    {
        var dto = new Dto();

        foreach (var d in Object.FindObjectsByType<DroppedItem>(FindObjectsSortMode.None)
                                .Where(d => d != null && d.item != null && d.amount > 0)
                                .OrderBy(d => d.transform.position.x)
                                .ThenBy(d => d.transform.position.z)
                                .ThenBy(d => d.transform.position.y))
        {
            dto.Drops.Add(new DropDto
            {
                ItemId = SaveRefs.IdOf(d.item),
                Amount = d.amount,
                Position = d.transform.position,
            });
        }

        foreach (var n in Object.FindObjectsByType<ResourceNode>(FindObjectsSortMode.None)
                                .Where(n => n != null)
                                .OrderBy(n => n.Origin.x).ThenBy(n => n.Origin.y))
        {
            dto.Nodes.Add(new NodeDto
            {
                Origin = n.Origin,
                CurrentStock = n.CurrentStock,
                NextProduceAt = n.NextProduceAt,
                TotalExtracted = n.TotalExtracted,
            });
        }

        foreach (var inv in Object.FindObjectsByType<Inventory>(FindObjectsSortMode.None)
                                  .Where(i => i != null)
                                  .OrderBy(i => SaveScenePath.Of(i)))
        {
            dto.Chests.Add(new ChestDto
            {
                ScenePath = SaveScenePath.Of(inv),
                Container = SaveContainerDto.From(inv.Container),
            });
        }

        return dto;
    }

    // ── 복원 ──────────────────────────────────────────────────────

    public void Restore(JToken data)
    {
        var dto = SaveJson.FromToken<Dto>(data);
        if (dto == null) return;

        RestoreDrops(dto);
        RestoreNodes(dto);
        RestoreChests(dto);
    }

    /// <summary>
    /// 바닥 아이템은 신원이랄 것이 없으므로 전부 치우고 저장된 대로 다시 뿌린다.
    /// (개별로 짝을 맞추려 해봐야 위치가 물리로 조금씩 흔들려 있어 소용이 없다)
    /// </summary>
    static void RestoreDrops(Dto dto)
    {
        foreach (var existing in Object.FindObjectsByType<DroppedItem>(FindObjectsSortMode.None))
            if (existing != null) Object.Destroy(existing.gameObject);

        foreach (var d in dto.Drops)
        {
            var item = SaveRefs.Item(d.ItemId);
            if (item == null || d.Amount <= 0) continue;

            // 던지는 힘 없이(Vector3.zero) 스폰한 뒤 좌표를 정확히 맞춘다 —
            // 저장된 위치는 이미 바닥에 안착한 결과라 다시 튀어오르면 안 된다
            var spawned = DroppedItem.Spawn(item, d.Amount, d.Position, Vector3.zero);
            if (spawned != null) spawned.transform.position = d.Position;
        }
    }

    static void RestoreNodes(Dto dto)
    {
        var byCell = new Dictionary<Vector2Int, ResourceNode>();
        foreach (var n in Object.FindObjectsByType<ResourceNode>(FindObjectsSortMode.None))
            if (n != null) byCell[n.Origin] = n;

        foreach (var saved in dto.Nodes)
        {
            if (!byCell.TryGetValue(saved.Origin, out var node))
            {
                Debug.LogWarning($"[Save] 광맥 {saved.Origin} 이 씬에 없어 재고를 복원하지 못했습니다.");
                continue;
            }
            node.RestoreState(saved.CurrentStock, saved.NextProduceAt, saved.TotalExtracted);
        }
    }

    static void RestoreChests(Dto dto)
    {
        var byPath = new Dictionary<string, Inventory>();
        foreach (var inv in Object.FindObjectsByType<Inventory>(FindObjectsSortMode.None))
        {
            if (inv == null) continue;
            byPath[SaveScenePath.Of(inv)] = inv;
        }

        foreach (var saved in dto.Chests)
        {
            if (saved?.Container == null) continue;

            if (!byPath.TryGetValue(saved.ScenePath, out var inv))
            {
                Debug.LogWarning($"[Save] 상자 '{saved.ScenePath}' 를 씬에서 찾지 못했습니다 — " +
                                 "오브젝트 이름이나 부모가 바뀌면 내용물을 되돌릴 수 없습니다.");
                continue;
            }
            saved.Container.ApplyTo(inv.Container);
        }
    }
}
