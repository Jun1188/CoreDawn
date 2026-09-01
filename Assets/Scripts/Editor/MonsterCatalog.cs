using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using CoreDawn.Entities;

namespace CoreDawn.EditorTools
{
    /// <summary>
    /// 몬스터 종(種) 목록 — <b>새 적을 추가할 때 손대는 유일한 곳.</b>
    ///
    /// 임포터 설정(구 MonsterAssetSetup(삭제됨)), 애니메이터 생성(<see cref="MonsterAnimationBuilder"/>),
    /// 프리팹 조립(구 MonsterRigBuilder(삭제됨 — 몬스터 뷰는 MonsterAssembler가 팩 view로 세운다)) 셋이 전부 이 표 하나를 읽는다.
    /// 종이 늘어도 그 세 도구의 코드는 바뀌지 않는다.
    ///
    /// 타워 쪽 구 TowerRigBuilder.Spec(타워는 5a-4b에서 팩 view로 대체됨)와 같은 발상이되, 몬스터는 종마다 리그가 달라
    /// 클립을 직접 공유할 수 없으므로 "슬롯 → 종별 클립" 매핑이 한 겹 더 있다.
    /// </summary>
    public static class MonsterCatalog
    {
        public const string CharacterRoot = "Assets/ThirdParty/3D Game Kit - Character Pack/Characters";
        public const string AnimRoot = "Assets/Art/Animation/Monsters";
        public const string SlotRoot = AnimRoot + "/Slots";
        public const string MaterialRoot = "Assets/Art/Materials/Monsters";
        public const string PrefabRoot = "Assets/Prefabs/Monster";

        public const string CommonController = AnimRoot + "/MonsterCommon.controller";

        // ── 모션 슬롯 ───────────────────────────────────────────────
        // MonsterCommon.controller가 참조하는 자리표시 클립의 이름이자, 종별
        // AnimatorOverrideController의 키다. 여기에 한 줄 넣으면 세 도구가 함께 따라온다.

        public const string SlotIdle = "M_Idle";
        public const string SlotWalk = "M_Walk";
        public const string SlotRun = "M_Run";
        public const string SlotAlert = "M_Alert";
        public const string SlotDeath = "M_Death";
        public const string SlotAttackPrefix = "M_Attack_";
        public const string SlotHitPrefix = "M_Hit_";

        /// <summary>공격 슬롯 최대 개수 — 컨트롤러에 만들어 두는 상태 수.</summary>
        public const int AttackSlots = 3;

        /// <summary>피격 슬롯 최대 개수.</summary>
        public const int HitSlots = 4;

        /// <summary>컨트롤러가 갖는 모든 슬롯 이름(자리표시 클립 이름과 동일).</summary>
        public static IEnumerable<string> AllSlots
        {
            get
            {
                yield return SlotIdle;
                yield return SlotWalk;
                yield return SlotRun;
                yield return SlotAlert;
                yield return SlotDeath;
                for (int i = 0; i < AttackSlots; i++) yield return SlotAttackPrefix + i;
                for (int i = 0; i < HitSlots; i++) yield return SlotHitPrefix + i;
            }
        }

        // ── 종 정의 ─────────────────────────────────────────────────

        public class Species
        {
            /// <summary>종 이름. 오버라이드 컨트롤러 파일명에 쓰인다.</summary>
            public string Id;

            /// <summary>캐릭터 팩 안의 폴더 — 모델·머티리얼·텍스처의 출처.</summary>
            public string SourceFolder;

            /// <summary>붙일 모델 프리팹(팩의 맨몸 FBX 인스턴스).</summary>
            public string ModelPrefab;

            /// <summary>
            /// 구울 게임 프리팹. 이미 있으면 <b>제자리 수정</b>해 GUID를 보존한다
            /// (씬·SO에 박힌 기존 참조가 전부 살아 있어야 한다).
            /// 없으면 <see cref="TemplatePrefab"/>을 복사해 만든다.
            /// </summary>
            public string TargetPrefab;

            /// <summary>대상 프리팹이 없을 때 베껴올 원본. 비우면 Monster.prefab.</summary>
            public string TemplatePrefab;

            /// <summary>모델을 맞출 월드 높이(미터). 콜라이더는 건드리지 않고 모델만 여기에 맞춘다.</summary>
            public float TargetHeight;

            /// <summary>
            /// 클립을 찾을 폴더들 — 앞에서부터 뒤진다.
            /// Spitter가 Chomper 폴더를 뒤에 다는 식으로 쓴다
            /// (팩의 animations.txt: "Spitter uses the same animation clips as the chomper model").
            /// </summary>
            public string[] ClipFolders;

            /// <summary>슬롯 → 클립 이름. 짝수 인덱스가 슬롯, 홀수가 클립 이름.</summary>
            public string[] SlotClips;

            /// <summary>실제로 무작위 선택에 쓰는 공격 모션 수. 슬롯을 다 채워도 이 수만 돌린다.</summary>
            public int AttackVariants = 1;

            /// <summary>실제로 무작위 선택에 쓰는 피격 모션 수.</summary>
            public int HitVariants = 1;

            /// <summary>사망 연출 — 전용 클립이 없는 종은 SinkAway.</summary>
            public MonsterVisualController.DeathStyle DeathStyle;

            /// <summary>
            /// 사망 후 소멸까지의 시간(초) — <c>Entity.deathDelay</c>에 그대로 쓴다.
            /// 사망 연출이 다 보일 만큼은 되어야 하고, 시체가 널브러져 있을 만큼 길면 안 된다.
            /// 0 이하면 프리팹 값을 그대로 둔다.
            /// </summary>
            public float DeathDelay;

            public string OverrideController => AnimRoot + "/Monster_" + Id + ".overrideController";
            public string MaterialFolder => MaterialRoot + "/" + Id;
        }

        /// <summary>
        /// Gunner는 의도적으로 빠져 있다 — FBX에 스켈레톤(Deformer/LimbNode)이 하나도 없고
        /// 애니메이션 클립도 0개라 스킨드 애니메이션을 붙일 방법이 없다. 2026-08-18 폐기 결정.
        /// </summary>
        public static readonly Species[] All =
        {
            // ── 일반몹 ──
            new Species {
                Id = "Chomper",
                SourceFolder = CharacterRoot + "/Chomper",
                ModelPrefab = CharacterRoot + "/Chomper/Prefabs/Chomper.prefab",
                TargetPrefab = PrefabRoot + "/Monster.prefab",
                TargetHeight = 1.0f,   // 네발로 웅크린 체형 — 키를 낮게 잡아야 몸길이가 칸을 안 넘는다
                ClipFolders = new[] { CharacterRoot + "/Chomper/AnimationClips" },
                SlotClips = new[]
                {
                    SlotIdle,             "ChomperIdle",
                    SlotWalk,             "ChomperWalkForward",
                    SlotRun,              "ChomperRunForward",
                    SlotAlert,            "ChomperCutsceneTOIdle",
                    SlotAttackPrefix + 0, "ChomperAttack",
                    SlotHitPrefix + 0,    "ChomperHit1",
                    SlotHitPrefix + 1,    "ChomperHit2",
                    SlotHitPrefix + 2,    "ChomperHit3",
                    SlotHitPrefix + 3,    "ChomperHit4",
                    // 사망 클립이 없다 — 피격 모션을 마지막으로 한 번 보이고 가라앉힌다
                    SlotDeath,            "ChomperHit1",
                },
                AttackVariants = 1,
                HitVariants = 4,
                DeathStyle = MonsterVisualController.DeathStyle.SinkAway,
                DeathDelay = 2f,   // 가라앉기는 0.4 + 1.2 = 1.6초에 끝난다
            },

            new Species {
                Id = "Spitter",
                SourceFolder = CharacterRoot + "/Spitter",
                ModelPrefab = CharacterRoot + "/Spitter/Prefabs/Spitter.prefab",
                TargetPrefab = PrefabRoot + "/Monster_Spitter.prefab",
                TargetHeight = 1.05f,
                // 자기 폴더를 먼저 보고(Spotted·SpitterSpit), 나머지는 Chomper 것을 쓴다 — 스켈레톤이 같다
                ClipFolders = new[]
                {
                    CharacterRoot + "/Spitter/AnimationClips",
                    CharacterRoot + "/Chomper/AnimationClips",
                },
                SlotClips = new[]
                {
                    SlotIdle,             "ChomperIdle",
                    SlotWalk,             "ChomperWalkForward",
                    SlotRun,              "ChomperRunForward",
                    SlotAlert,            "Spotted",
                    SlotAttackPrefix + 0, "ChomperAttack",
                    SlotAttackPrefix + 1, "SpitterSpit",
                    SlotHitPrefix + 0,    "ChomperHit1",
                    SlotHitPrefix + 1,    "ChomperHit2",
                    SlotHitPrefix + 2,    "ChomperHit3",
                    SlotHitPrefix + 3,    "ChomperHit4",
                    SlotDeath,            "ChomperHit2",
                },
                AttackVariants = 2,
                HitVariants = 4,
                DeathStyle = MonsterVisualController.DeathStyle.SinkAway,
                DeathDelay = 2f,
            },

            // ── 보스 ──
            new Species {
                Id = "Grenadier",
                SourceFolder = CharacterRoot + "/Grenadier",
                ModelPrefab = CharacterRoot + "/Grenadier/Prefabs/Grenadier.prefab",
                TargetPrefab = PrefabRoot + "/BossMonster.prefab",
                TargetHeight = 3.4f,   // 이족보행 거구 — 보스답게 플레이어를 내려다보는 키
                ClipFolders = new[] { CharacterRoot + "/Grenadier/AnimationClips" },
                SlotClips = new[]
                {
                    SlotIdle,             "GrenadierIdle",
                    SlotWalk,             "GrenadierWalk",
                    SlotRun,              "GrenadierWalkFast",
                    // 마땅한 경계 모션이 없다 — 대기 자세로 폴백
                    SlotAlert,            "GrenadierIdle",
                    SlotAttackPrefix + 0, "GrenadierMeleeAttack",
                    SlotAttackPrefix + 1, "GrenadierCloseRangeAttack",
                    // 원거리 모션은 슬롯에 넣어만 둔다. 지금 보스는 근접(attackRange 1.5)이라
                    // AttackVariants=2로 막아 두었다 — 원거리 보스가 생기면 3으로 올리면 된다.
                    SlotAttackPrefix + 2, "GrenadierRangeAttack",
                    SlotHitPrefix + 0,    "GrenadierHit1",
                    SlotHitPrefix + 1,    "GrenadierHit2",
                    SlotHitPrefix + 2,    "GrenadierHit3",
                    SlotHitPrefix + 3,    "GrenadierHit4",
                    SlotDeath,            "GrenadierDeath",
                },
                AttackVariants = 2,
                HitVariants = 4,
                DeathStyle = MonsterVisualController.DeathStyle.AnimationClip,
                // GrenadierDeath는 280프레임(≈9.3초)짜리다. 전부 보여주면 시체가 전장에 오래 남으므로
                // 쓰러지는 대목까지만 보이고 걷어낸다. 보스는 한 번에 한 마리라 4초는 부담이 아니다.
                DeathDelay = 4.5f,
            },
        };

        // ── 도우미 ──────────────────────────────────────────────────

        /// <summary>슬롯 → 클립 이름 사전으로 편다.</summary>
        public static Dictionary<string, string> SlotMap(Species species)
        {
            var map = new Dictionary<string, string>();
            for (int i = 0; i + 1 < species.SlotClips.Length; i += 2)
                map[species.SlotClips[i]] = species.SlotClips[i + 1];
            return map;
        }

        /// <summary>
        /// 종의 클립 폴더들에서 이름으로 클립을 찾는다. FBX 서브에셋이라 경로만으로는 못 집는다.
        /// Unity가 붙이는 <c>__preview__</c> 클립은 건너뛴다.
        /// </summary>
        public static AnimationClip FindClip(Species species, string clipName)
        {
            if (string.IsNullOrEmpty(clipName)) return null;

            foreach (string folder in species.ClipFolders)
            {
                if (!AssetDatabase.IsValidFolder(folder)) continue;

                foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { folder }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
                    {
                        if (asset is AnimationClip clip &&
                            !clip.name.StartsWith("__preview__") &&
                            clip.name == clipName)
                            return clip;
                    }
                }
            }
            return null;
        }

        /// <summary>없으면 만들어 가며 폴더 경로를 보장한다.</summary>
        public static void EnsureFolder(string path)
        {
            if (string.IsNullOrEmpty(path) || AssetDatabase.IsValidFolder(path)) return;
            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }
    }
}
