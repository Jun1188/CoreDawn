using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>
/// 플레이어 — 서 있는 자리, 보고 있는 방향, 체력, 소지품, 들고 있는 무기.
///
/// 맨 마지막에 복원된다(Order 50). BattleManager가 Start에서 PlayerController에 Player 엔티티를
/// 런타임으로 붙이면서 최대 체력을 설정하고 체력을 가득 채우는데, 그보다 먼저 복원하면 덮어써진다.
/// </summary>
public class PlayerSaveModule : ISaveModule
{
    public string ModuleId => "player";
    public int Order => 50;

    // ── DTO ──────────────────────────────────────────────────────

    public class Dto
    {
        [JsonProperty("pos")] public Vector3 Position;

        /// <summary>몸의 좌우 방향. 상하는 카메라 리그가 따로 갖는다.</summary>
        [JsonProperty("yaw")] public float Yaw;

        [JsonProperty("pitch")] public float Pitch;

        [JsonProperty("hpMax")] public float HpMax;
        [JsonProperty("hpCur")] public float HpCurrent;

        [JsonProperty("hotbar")] public SaveContainerDto Hotbar;
        [JsonProperty("main")] public SaveContainerDto Main;
        [JsonProperty("craftIn")] public SaveContainerDto CraftingInput;
        [JsonProperty("craftOut")] public SaveContainerDto CraftingOutput;

        [JsonProperty("hotbarIndex")] public int HotbarIndex;

        /// <summary>창을 연 채로 저장했을 때 마우스에 들려 있던 스택.</summary>
        [JsonProperty("carried")] public SaveStackDto Carried;

        /// <summary>무기별 장전 탄수 — WeaponManager.weapons 배열 순서.</summary>
        [JsonProperty("ammo")] public List<int> Ammo = new();
    }

    // ── 저장 ──────────────────────────────────────────────────────

    public object Capture()
    {
        var controller = Object.FindFirstObjectByType<PlayerController>();
        if (controller == null) return null;

        var dto = new Dto
        {
            Position = controller.transform.position,
            Yaw = controller.transform.eulerAngles.y,
            Pitch = controller.CameraRig != null ? controller.CameraRig.Pitch : 0f,
            HotbarIndex = HotbarController.Instance != null ? HotbarController.Instance.CurrentHotbarIndex : 0,
        };

        var entity = controller.GetComponent<Player>();
        if (entity != null)
        {
            dto.HpMax = entity.Health.MaxHealth;
            dto.HpCurrent = entity.Health.CurrentHealth;
        }

        var holder = PlayerInventoryHolder.Instance;
        if (holder != null)
        {
            dto.Hotbar = SaveContainerDto.From(holder.HotbarContainer);
            dto.Main = SaveContainerDto.From(holder.MainContainer);
            dto.CraftingInput = SaveContainerDto.From(holder.CraftingInputContainer);
            dto.CraftingOutput = SaveContainerDto.From(holder.CraftingOutputContainer);
        }

        if (InventoryManager.Instance != null)
            dto.Carried = SaveStackDto.From(InventoryManager.Instance.MouseCarriage);

        var weapons = Object.FindFirstObjectByType<WeaponManager>();
        if (weapons != null && weapons.weapons != null)
            foreach (var w in weapons.weapons)
                dto.Ammo.Add(w != null ? w.CurrentAmmo : 0);

        return dto;
    }

    // ── 복원 ──────────────────────────────────────────────────────

    public void Restore(JToken data)
    {
        var dto = SaveJson.FromToken<Dto>(data);
        if (dto == null) return;

        var controller = Object.FindFirstObjectByType<PlayerController>();
        if (controller != null)
        {
            controller.RestoreTransform(dto.Position, dto.Yaw);
            if (controller.CameraRig != null) controller.CameraRig.RestorePitch(dto.Pitch);

            var entity = controller.GetComponent<Player>();
            if (entity != null && dto.HpMax > 0f)
                entity.Health.RestoreState(dto.HpMax, dto.HpCurrent, isDead: false);
        }

        var holder = PlayerInventoryHolder.Instance;
        if (holder != null)
        {
            dto.Hotbar?.ApplyTo(holder.HotbarContainer);
            dto.Main?.ApplyTo(holder.MainContainer);
            dto.CraftingInput?.ApplyTo(holder.CraftingInputContainer);
            dto.CraftingOutput?.ApplyTo(holder.CraftingOutputContainer);
        }

        if (InventoryManager.Instance != null)
            InventoryManager.Instance.RestoreMouseCarriage(dto.Carried?.ToStack());

        var weapons = Object.FindFirstObjectByType<WeaponManager>();
        if (weapons?.weapons != null && dto.Ammo != null)
            for (int i = 0; i < weapons.weapons.Length && i < dto.Ammo.Count; i++)
                if (weapons.weapons[i] != null) weapons.weapons[i].RestoreAmmo(dto.Ammo[i]);

        // 소지품이 제자리를 찾은 뒤에 선택 칸을 되돌려야 그 칸의 무기가 올바로 장착된다
        if (HotbarController.Instance != null) HotbarController.Instance.RestoreSelection(dto.HotbarIndex);

        InventoryManager.Instance?.RefreshAllGameUIs();
    }
}
