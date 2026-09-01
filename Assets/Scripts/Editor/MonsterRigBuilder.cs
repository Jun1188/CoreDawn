using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using CoreDawn.Combat;
using CoreDawn.Entities;

namespace CoreDawn.EditorTools
{
    /// <summary>
    /// 몬스터 프리팹의 <b>View 서브트리</b>를 표준 계층으로 만들어 넣는 에디터 도구.
    /// 구 TowerRigBuilder(타워 리그는 이제 팩 view.model의 fbx — 5a-4b)의 몬스터판이고, 이유도 같다 — 손으로 조립하면 종마다
    /// 축척·접지·머티리얼 짝짓기가 미묘하게 어긋나고, 그걸 눈으로 잡아내기 어렵다.
    ///
    /// 표준 계층:
    ///   (루트)              ← Monster.cs / 콜라이더 / 강체 / MonsterVisualController
    ///     View              ← 이 빌더: 종별 축척 + 접지 / 컨트롤러: 사망 시 가라앉기
    ///       &lt;종 모델&gt;       ← Animator(아바타 + 종별 오버라이드) + 스킨드 메시 + 본
    ///
    /// Animator를 모델 루트에 다는 것이 중요하다. Generic 아바타는 자기가 만들어진 FBX 루트를
    /// 기준으로 본 경로를 푼다 — 한 단계 위(View)에 달면 경로가 어긋나 클립이 조용히 안 먹는다.
    ///
    /// 기존 프리팹은 항상 <b>제자리 수정</b>한다. Monster.prefab / BossMonster.prefab의 GUID는
    /// 여러 씬과 MonsterNest에 박혀 있어서, 새로 만들어 갈아끼우면 그 참조가 전부 끊어진다.
    ///
    /// 새 적을 추가하려면 <see cref="MonsterCatalog.All"/>에 한 줄 넣고 메뉴를 다시 실행하면 된다.
    /// </summary>
    public static class MonsterRigBuilder
    {
        private const string DefaultTemplate = MonsterCatalog.PrefabRoot + "/Monster.prefab";

        [MenuItem("Tools/Monsters/3. Rebuild Monster Rigs")]
        public static void RebuildMenu() => Debug.Log(BuildAll());

        [MenuItem("Tools/Monsters/Rebuild Everything")]
        public static void RebuildEverything()
        {
            var log = new System.Text.StringBuilder();
            log.AppendLine(MonsterAssetSetup.PrepareAll());
            log.AppendLine(MonsterAnimationBuilder.BuildAll());
            log.AppendLine(BuildAll());
            Debug.Log(log.ToString());
        }

        public static string BuildAll()
        {
            var log = new System.Text.StringBuilder();
            foreach (var species in MonsterCatalog.All)
            {
                try { log.AppendLine(Build(species)); }
                catch (System.Exception e) { log.AppendLine($"FAIL {species.Id}: {e.Message}\n{e.StackTrace}"); }
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return log.ToString();
        }

        public static string Build(MonsterCatalog.Species species)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(species.ModelPrefab);
            if (model == null) return $"FAIL {species.Id}: 모델 프리팹 없음 {species.ModelPrefab}";

            var overrideController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(species.OverrideController);
            if (overrideController == null)
                return $"FAIL {species.Id}: 오버라이드 컨트롤러 없음 — 'Rebuild Monster Animations'를 먼저 실행할 것";

            if (!EnsureTargetPrefab(species, out string prepError)) return prepError;

            Avatar avatar = FindAvatar(species);
            Dictionary<string, Material> materials = MonsterAssetSetup.LoadUrpMaterials(species);

            GameObject root = PrefabUtility.LoadPrefabContents(species.TargetPrefab);
            try
            {
                StripPlaceholder(root);

                var view = new GameObject("View").transform;
                view.SetParent(root.transform, false);

                GameObject body = Object.Instantiate(model);
                body.name = species.Id;
                body.transform.SetParent(view, false);

                // ── 축척 ── 모델 높이를 목표 월드 높이에 맞춘다. 콜라이더는 건드리지 않는다:
                // 반지름·높이는 이미 밸런스가 잡힌 게임플레이 값이고 CrowdSystem도 이를 전제한다.
                Bounds raw = WorldBounds(view);
                float height = raw.size.y;
                float scale = height > 0.0001f ? species.TargetHeight / height : 1f;
                view.localScale = Vector3.one * scale;

                // ── 접지 ── 모델 바닥을 콜라이더 바닥에 맞춘다. WaveSpawnManager.SnapToGround가
                // 콜라이더 바닥을 지면에 놓으므로(WaveSpawnManager.cs:452), 이렇게 해야 발이 땅에 닿는다.
                AlignToColliderBottom(root, view);

                // ── 애니메이터 ──
                var animator = body.GetComponent<Animator>();
                if (animator == null) animator = body.AddComponent<Animator>();
                if (avatar != null) animator.avatar = avatar;
                animator.runtimeAnimatorController = overrideController;
                // 위치·회전의 주인은 MovementComponent다. 루트 모션을 켜면 둘이 매 프레임 싸운다.
                animator.applyRootMotion = false;
                // 화면 밖에서는 트랜스폼 쓰기만 생략한다. 상태는 계속 흘러야 사망 연출이 밀리지 않는다.
                // (더 공격적인 컬링은 MonsterAnimationSystem이 거리까지 봐서 런타임에 건다.)
                animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

                int swapped = SwapMaterials(view, materials);

                // ── 컴포넌트 배선 ──
                var visual = root.GetComponent<MonsterVisualController>();
                if (visual == null) visual = root.AddComponent<MonsterVisualController>();
                WireVisual(visual, species, view, animator);

                ApplyDeathDelay(root, species);
                string repaired = RepairMonsterData(root, species);

                PrefabUtility.SaveAsPrefabAsset(root, species.TargetPrefab);

                return $"OK {species.Id} → {System.IO.Path.GetFileName(species.TargetPrefab)} " +
                       $"scale={scale:F3} height={species.TargetHeight:F2} 머티리얼={swapped} " +
                       $"avatar={(avatar != null ? avatar.name : "없음")}{repaired}";
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // ── 준비 ────────────────────────────────────────────────────

        /// <summary>대상 프리팹이 없으면 템플릿을 복사해 만든다(신규 종). 있으면 그대로 둔다(GUID 보존).</summary>
        private static bool EnsureTargetPrefab(MonsterCatalog.Species species, out string error)
        {
            error = null;
            if (AssetDatabase.LoadAssetAtPath<GameObject>(species.TargetPrefab) != null) return true;

            string template = string.IsNullOrEmpty(species.TemplatePrefab) ? DefaultTemplate : species.TemplatePrefab;
            if (AssetDatabase.LoadAssetAtPath<GameObject>(template) == null)
            {
                error = $"FAIL {species.Id}: 템플릿 프리팹 없음 {template}";
                return false;
            }

            MonsterCatalog.EnsureFolder(System.IO.Path.GetDirectoryName(species.TargetPrefab).Replace('\\', '/'));
            if (!AssetDatabase.CopyAsset(template, species.TargetPrefab))
            {
                error = $"FAIL {species.Id}: 프리팹 복사 실패 {template} → {species.TargetPrefab}";
                return false;
            }
            AssetDatabase.ImportAsset(species.TargetPrefab);
            return true;
        }

        private static Avatar FindAvatar(MonsterCatalog.Species species)
        {
            string folder = species.SourceFolder + "/Models";
            if (!AssetDatabase.IsValidFolder(folder)) return null;

            foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
                    if (asset is Avatar avatar) return avatar;
            }
            return null;
        }

        /// <summary>
        /// 자리표시 캡슐을 걷어낸다. 메시가 <b>루트에 직접</b> 붙어 있어서(Monster.prefab의 원래 모습)
        /// 자식만 지워서는 안 되고 컴포넌트를 떼야 한다. 이전 실행이 만든 View도 함께 지운다.
        /// </summary>
        private static void StripPlaceholder(GameObject root)
        {
            for (int i = root.transform.childCount - 1; i >= 0; i--)
            {
                Transform c = root.transform.GetChild(i);
                if (c.name == "View" || c.name == "Mesh") Object.DestroyImmediate(c.gameObject);
            }

            var mr = root.GetComponent<MeshRenderer>();
            if (mr != null) Object.DestroyImmediate(mr);

            var mf = root.GetComponent<MeshFilter>();
            if (mf != null) Object.DestroyImmediate(mf);
        }

        // ── 배치 ────────────────────────────────────────────────────

        private static Bounds WorldBounds(Transform subtree)
        {
            var renderers = subtree.GetComponentsInChildren<Renderer>(true);
            bool first = true;
            var bounds = new Bounds();

            foreach (var r in renderers)
            {
                Bounds b = r.bounds;
                if (b.size == Vector3.zero) continue;
                if (first) { bounds = b; first = false; }
                else bounds.Encapsulate(b);
            }
            return bounds;
        }

        /// <summary>
        /// 모델 바닥을 콜라이더 바닥에 맞춘다. 프리팹 컨텐츠 루트는 원점에 있으므로
        /// 월드 y가 그대로 루트 기준 높이다 — 루트의 축척(0.6 / 2)은 이미 반영돼 있다.
        /// </summary>
        private static void AlignToColliderBottom(GameObject root, Transform view)
        {
            Bounds bounds = WorldBounds(view);
            if (bounds.size == Vector3.zero) return;

            float targetBottom = 0f;
            var capsule = root.GetComponent<CapsuleCollider>();
            if (capsule != null)
                targetBottom = (capsule.center.y - capsule.height * 0.5f) * root.transform.localScale.y;
            else
            {
                var box = root.GetComponent<BoxCollider>();
                if (box != null)
                    targetBottom = (box.center.y - box.size.y * 0.5f) * root.transform.localScale.y;
            }

            view.position += Vector3.up * (targetBottom - bounds.min.y);
        }

        /// <summary>
        /// 원본 머티리얼을 이름으로 짝지어 URP 사본으로 바꾼다.
        /// 슬롯을 통째로 덮어쓰지 않는 이유: Grenadier는 본체·에너지코어·눈 세 장을 쓰는데
        /// 서브메시 순서를 가정하면 새 종에서 조용히 어긋난다.
        /// </summary>
        private static int SwapMaterials(Transform view, Dictionary<string, Material> materials)
        {
            if (materials.Count == 0) return 0;

            int swapped = 0;
            foreach (var renderer in view.GetComponentsInChildren<Renderer>(true))
            {
                Material[] shared = renderer.sharedMaterials;
                bool changed = false;

                for (int i = 0; i < shared.Length; i++)
                {
                    if (shared[i] == null) continue;
                    if (!materials.TryGetValue(shared[i].name, out Material replacement)) continue;
                    shared[i] = replacement;
                    changed = true;
                    swapped++;
                }

                if (changed) renderer.sharedMaterials = shared;

                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                renderer.receiveShadows = true;
                if (renderer is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = false;
            }
            return swapped;
        }

        // ── 배선 ────────────────────────────────────────────────────

        private static void WireVisual(MonsterVisualController visual, MonsterCatalog.Species species,
                                       Transform view, Animator animator)
        {
            var so = new SerializedObject(visual);
            so.FindProperty("view").objectReferenceValue = view;
            so.FindProperty("animator").objectReferenceValue = animator;
            so.FindProperty("attackVariants").intValue = Mathf.Max(1, species.AttackVariants);
            so.FindProperty("hitVariants").intValue = Mathf.Max(1, species.HitVariants);
            so.FindProperty("deathStyle").enumValueIndex = (int)species.DeathStyle;
            // 몸이 완전히 묻힐 만큼만 — 키에 비례시키면 종이 커져도 그대로 먹는다
            so.FindProperty("sinkDepth").floatValue = species.TargetHeight * 1.3f;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// 사망 연출 길이를 <c>Entity.deathDelay</c>에 맞춘다. 종마다 사망 클립 길이가 달라서
        /// 프리팹 기본값(2초)으로는 보스가 쓰러지기도 전에 사라진다.
        /// 웨이브 집계는 <c>IsDead</c>로 세므로(WaveSpawnManager.NightWaveAliveCount) 길게 잡아도 진행이 막히지 않는다.
        /// </summary>
        private static void ApplyDeathDelay(GameObject root, MonsterCatalog.Species species)
        {
            if (species.DeathDelay <= 0f) return;

            var entity = root.GetComponent<EntityView>();
            if (entity == null) return;

            var so = new SerializedObject(entity);
            SerializedProperty delay = so.FindProperty("deathDelay");
            if (delay == null) return;
            delay.floatValue = species.DeathDelay;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// BossMonster.prefab은 현재 <c>Monster.cs</c>에 없는 옛 필드(attackDamage 등)로 직렬화돼 있어
        /// <c>combat.attackEffects</c>가 비어 있다 — 즉 <b>보스가 피해를 전혀 주지 못한다.</b>
        /// 리그를 구우면서 같이 고친다. 비어 있을 때만 손대고, 값은 일반 몬스터의 것을 그대로 쓴다
        /// (밸런스는 팀이 정할 몫이라 여기서 새 수치를 지어내지 않는다).
        /// </summary>
        private static string RepairMonsterData(GameObject root, MonsterCatalog.Species species)
        {
            var monster = root.GetComponent<MonsterView>();
            if (monster == null) return "";

            var so = new SerializedObject(monster);
            SerializedProperty effects = so.FindProperty("combat.attackEffects");
            if (effects == null || !effects.isArray || effects.arraySize > 0) return "";

            var template = AssetDatabase.LoadAssetAtPath<GameObject>(DefaultTemplate);
            var source = template != null ? template.GetComponent<MonsterView>() : null;
            if (source == null) return "  [경고] attackEffects 비어 있음 — 템플릿을 찾지 못해 복구 못 함";

            SerializedProperty from = new SerializedObject(source).FindProperty("combat.attackEffects");
            if (from == null || from.arraySize == 0) return "  [경고] attackEffects 비어 있음 — 템플릿도 비어 있음";

            effects.arraySize = from.arraySize;
            for (int i = 0; i < from.arraySize; i++)
            {
                SerializedProperty dst = effects.GetArrayElementAtIndex(i);
                SerializedProperty src = from.GetArrayElementAtIndex(i);
                dst.FindPropertyRelative("effect").objectReferenceValue =
                    src.FindPropertyRelative("effect").objectReferenceValue;
                dst.FindPropertyRelative("value").floatValue =
                    src.FindPropertyRelative("value").floatValue;
            }
            so.ApplyModifiedPropertiesWithoutUndo();

            return "  [복구] attackEffects가 비어 있어 일반 몬스터 값으로 채움 — 밸런스 확인 필요";
        }
    }
}
