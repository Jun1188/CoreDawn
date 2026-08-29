using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using CoreDawn.Entities;
using CoreDawn.FPS;
using CoreDawn.Inventories;

namespace CoreDawn.Save
{
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
            // craftIn·craftOut은 없다 — 제작 4+1 그릇이 사라졌다(53f6889).
            // UITK 인벤토리가 가방·핫바에서 직접 소모·지급하므로 재료를 맡아둘 그릇이 필요 없다.
            // 옛 세이브에 남아 있는 두 필드는 역직렬화에서 그냥 버려진다.

            [JsonProperty("hotbarIndex")] public int HotbarIndex;
            // carried(마우스에 들린 스택)는 없다 — uGUI InventoryManager와 함께 제거(2026-08-28).
            // UITK 패널은 닫힐 때 들고 있던 것을 되돌려 놓으므로(ReturnCarried) 저장할 것이 없다. 옛 세이브의 필드는 버려진다.

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

            var entity = controller.GetComponent<PlayerView>();
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
            }

            var weapons = Object.FindFirstObjectByType<WeaponManager>();
            if (weapons != null) dto.Ammo.AddRange(weapons.CaptureAmmo());

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

                var entity = controller.GetComponent<PlayerView>();
                if (entity != null && dto.HpMax > 0f)
                    entity.Health.RestoreState(dto.HpMax, dto.HpCurrent, isDead: false);
            }

            var holder = PlayerInventoryHolder.Instance;
            if (holder != null)
            {
                dto.Hotbar?.ApplyTo(holder.HotbarContainer);
                dto.Main?.ApplyTo(holder.MainContainer);
            }

            var weapons = Object.FindFirstObjectByType<WeaponManager>();
            if (weapons != null) weapons.RestoreAmmo(dto.Ammo);

            // 소지품이 제자리를 찾은 뒤에 선택 칸을 되돌려야 그 칸의 무기가 올바로 장착된다
            if (HotbarController.Instance != null) HotbarController.Instance.RestoreSelection(dto.HotbarIndex);
        }
    }
}
