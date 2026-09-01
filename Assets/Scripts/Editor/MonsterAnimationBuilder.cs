using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using CoreDawn.Entities;
using CoreDawn.Placement;

namespace CoreDawn.EditorTools
{
    /// <summary>
    /// 몬스터 애니메이터 에셋을 코드로 찍어내는 도구 — 상태 그래프 하나 + 종별 클립 교체본.
    ///
    /// 타워는 리그가 표준화돼 있어 클립까지 통째로 공유했지만(구 TowerAnimationBuilder(삭제됨 — 타워 처짐은 코드)),
    /// 몬스터는 종마다 스켈레톤이 달라 클립을 직접 공유할 수 없다. 대신 <b>상태 그래프를 공유</b>하고
    /// 클립만 갈아끼운다 — TowerVisualController 주석이 "개성이 필요하면 AnimatorOverrideController로"
    /// 라고 적어 둔 바로 그 방법이다.
    ///
    ///   MonsterCommon.controller      그래프·파라미터의 정본. 자리표시 클립(M_*)을 참조한다.
    ///   Monster_&lt;종&gt;.overrideController  자리표시 → 그 종의 진짜 클립
    ///
    /// 새 적을 추가할 때 여기 코드는 바뀌지 않는다. <see cref="MonsterCatalog.All"/>에 한 줄만 넣으면 된다.
    ///
    /// <b>주의 — 생성물이다.</b> <c>MonsterCommon.controller</c>와 종별 오버라이드는 이 도구의 출력물이라,
    /// Animator 창에서 손으로 고친 내용은 다음 실행 때 사라진다. 전이 시간·블렌드 임계값은 이 파일 위쪽
    /// 상수를, 클립 배정은 <see cref="MonsterCatalog"/>를 고칠 것. 그래프 자체를 손으로 관리하고 싶어지면
    /// (종이 몇 개 안 되고 개성이 강해지는 시점) 이 도구를 버리는 편이 낫다 — 억지로 유지할 이유는 없다.
    ///
    /// 역할 분담: 이 그래프는 <b>본만</b> 움직인다. 몬스터의 위치·회전은 MovementComponent가,
    /// 사망 시 가라앉기는 MonsterVisualController가 각각 다른 트랜스폼에서 소유한다.
    /// </summary>
    public static class MonsterAnimationBuilder
    {
        // 블렌드 임계값. Chomper의 Walk/Run 클립 속도차를 눈으로 맞춘 값이다.
        private const float WalkThreshold = 0.45f;

        // 공격·피격 모션이 끝나기 조금 전에 이동으로 섞어 넣는다. 1.0으로 두면 모션이
        // 완전히 끝난 뒤에야 움직여서 몬스터가 한 박자씩 굳어 보인다.
        private const float ActionExitTime = 0.85f;
        private const float ActionBlend = 0.15f;

        [MenuItem("Tools/Monsters/2. Rebuild Monster Animations")]
        public static void RebuildMenu() => Debug.Log(BuildAll());

        public static string BuildAll()
        {
            var log = new System.Text.StringBuilder();

            MonsterCatalog.EnsureFolder(MonsterCatalog.AnimRoot);
            MonsterCatalog.EnsureFolder(MonsterCatalog.SlotRoot);

            Dictionary<string, AnimationClip> slots = BuildSlotClips();
            log.AppendLine($"자리표시 클립 {slots.Count}개 → {MonsterCatalog.SlotRoot}");

            AnimatorController controller = BuildController(slots);
            log.AppendLine($"공용 컨트롤러 → {MonsterCatalog.CommonController}");

            foreach (var species in MonsterCatalog.All)
                log.AppendLine(BuildOverride(species, controller, slots));

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return log.ToString();
        }

        // ── 자리표시 클립 ───────────────────────────────────────────

        /// <summary>
        /// 슬롯 하나당 빈 클립 하나. 이 클립들이 오버라이드의 <b>키</b>가 된다 —
        /// AnimatorOverrideController는 상태 이름이 아니라 클립 참조로 짝을 짓기 때문이다.
        ///
        /// 곡선을 존재하지 않는 경로(<c>__slot</c>)에 묶는 이유: 길이 0짜리 클립은 정규화 시간이
        /// 정의되지 않아 Exit Time 전이가 이상해진다. 그렇다고 실제 트랜스폼에 묶으면
        /// 오버라이드가 빠진 슬롯이 몬스터를 실제로 움직여 버린다. 안 걸리는 경로에 1초를 준다.
        /// </summary>
        private static Dictionary<string, AnimationClip> BuildSlotClips()
        {
            var result = new Dictionary<string, AnimationClip>();

            foreach (string slot in MonsterCatalog.AllSlots)
            {
                string path = $"{MonsterCatalog.SlotRoot}/{slot}.anim";
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                if (clip == null)
                {
                    clip = new AnimationClip { name = slot, frameRate = 30f };
                    AssetDatabase.CreateAsset(clip, path);
                }

                var curve = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(1f, 0f));
                AnimationUtility.SetEditorCurve(
                    clip,
                    EditorCurveBinding.FloatCurve("__slot", typeof(Transform), "m_LocalPosition.x"),
                    curve);

                EditorUtility.SetDirty(clip);
                result[slot] = clip;
            }

            return result;
        }

        // ── 공용 컨트롤러 ───────────────────────────────────────────

        /// <summary>
        /// 컨트롤러를 <b>제자리에서</b> 다시 짓는다. 지우고 새로 만들지 않는 이유가 있다:
        /// <c>DeleteAsset</c> + 재생성은 에셋의 객체 정체성을 갈아치워서, 이미 그것을 참조하던
        /// 프리팹의 참조가 그 세션 동안 <c>null</c>이 된다(.meta가 살아남아 GUID는 같은데도 그렇다).
        /// 그러면 "2번만 돌리고 3번은 안 돌린" 사람은 <b>애니메이션이 조용히 사라진 몬스터</b>를 얻는다.
        /// 실제로 재현했다 — 그래서 상태·파라미터만 비우고 같은 에셋에 다시 쓴다.
        /// 덤으로, 팀원이 인스펙터에서 이 컨트롤러를 어딘가에 물려 놨어도 그 참조가 살아남는다.
        /// </summary>
        private static AnimatorController BuildController(Dictionary<string, AnimationClip> slots)
        {
            var controller = GeneratedAnimationAssets.LoadOrCreateController(MonsterCatalog.CommonController);
            GeneratedAnimationAssets.ClearController(controller);

            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("AttackIndex", AnimatorControllerParameterType.Int);
            controller.AddParameter("Hit", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("HitIndex", AnimatorControllerParameterType.Int);
            controller.AddParameter("Alert", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Dead", AnimatorControllerParameterType.Bool);

            var sm = controller.layers[0].stateMachine;

            // ── 이동: 정지 ↔ 걷기 ↔ 달리기 1차원 블렌드 ──
            AnimatorState locomotion = controller.CreateBlendTreeInController("Locomotion", out BlendTree tree, 0);
            tree.blendType = BlendTreeType.Simple1D;
            tree.blendParameter = "Speed";
            tree.useAutomaticThresholds = false;
            tree.AddChild(slots[MonsterCatalog.SlotIdle], 0f);
            tree.AddChild(slots[MonsterCatalog.SlotWalk], WalkThreshold);
            tree.AddChild(slots[MonsterCatalog.SlotRun], 1f);
            sm.defaultState = locomotion;

            // ── 단발 모션 ──
            AnimatorState alert = AddState(sm, "Alert", slots[MonsterCatalog.SlotAlert]);

            var attacks = new AnimatorState[MonsterCatalog.AttackSlots];
            for (int i = 0; i < attacks.Length; i++)
                attacks[i] = AddState(sm, "Attack_" + i, slots[MonsterCatalog.SlotAttackPrefix + i]);

            var hits = new AnimatorState[MonsterCatalog.HitSlots];
            for (int i = 0; i < hits.Length; i++)
                hits[i] = AddState(sm, "Hit_" + i, slots[MonsterCatalog.SlotHitPrefix + i]);

            AnimatorState death = AddState(sm, "Death", slots[MonsterCatalog.SlotDeath]);

            // ── 전이 ──
            // Any State에서 들어가는 이유: 걷다가도, 공격 중에도, 다른 피격 모션 중에도
            // 끼어들 수 있어야 한다. 상태를 늘릴 때마다 전이를 N×N으로 잇지 않아도 된다.

            AddAnyTransition(sm, alert, ActionBlend, "Alert");
            for (int i = 0; i < attacks.Length; i++)
                AddAnyTransition(sm, attacks[i], ActionBlend, "Attack", ("AttackIndex", i));
            for (int i = 0; i < hits.Length; i++)
                AddAnyTransition(sm, hits[i], 0.08f, "Hit", ("HitIndex", i));

            // 사망만은 트리거가 아니라 bool로 들어간다 — LOD가 Animator를 껐다 켜는 사이에
            // 트리거가 흘러가 버려도 상태가 남아 있어야 한다.
            var toDeath = sm.AddAnyStateTransition(death);
            toDeath.hasExitTime = false;
            toDeath.duration = 0.12f;
            toDeath.canTransitionToSelf = false;
            toDeath.AddCondition(AnimatorConditionMode.If, 0f, "Dead");

            // 단발 모션이 끝나면 이동으로 복귀
            AddReturn(alert, locomotion);
            foreach (var s in attacks) AddReturn(s, locomotion);
            foreach (var s in hits) AddReturn(s, locomotion);

            // 되살아난 경우(풀 재사용·세이브 복원)를 위한 탈출구. 없으면 영영 시체로 남는다.
            var revive = death.AddTransition(locomotion);
            revive.hasExitTime = false;
            revive.duration = 0.15f;
            revive.AddCondition(AnimatorConditionMode.IfNot, 0f, "Dead");

            EditorUtility.SetDirty(controller);
            return controller;
        }

        private static AnimatorState AddState(AnimatorStateMachine sm, string name, Motion motion)
        {
            var state = sm.AddState(name);
            state.motion = motion;
            return state;
        }

        private static void AddAnyTransition(AnimatorStateMachine sm, AnimatorState target, float blend,
                                             string trigger, params (string param, int value)[] ints)
        {
            var t = sm.AddAnyStateTransition(target);
            t.hasExitTime = false;
            t.duration = blend;
            t.canTransitionToSelf = false;
            t.AddCondition(AnimatorConditionMode.If, 0f, trigger);
            // 죽은 뒤에는 어떤 단발 모션도 끼어들지 못한다
            t.AddCondition(AnimatorConditionMode.IfNot, 0f, "Dead");
            foreach (var (param, value) in ints)
                t.AddCondition(AnimatorConditionMode.Equals, value, param);
        }

        private static void AddReturn(AnimatorState from, AnimatorState to)
        {
            var t = from.AddTransition(to);
            t.hasExitTime = true;
            t.exitTime = ActionExitTime;
            t.duration = ActionBlend;
        }

        // ── 종별 오버라이드 ─────────────────────────────────────────

        private static string BuildOverride(MonsterCatalog.Species species, AnimatorController baseController,
                                            Dictionary<string, AnimationClip> slots)
        {
            var map = MonsterCatalog.SlotMap(species);
            var resolved = new Dictionary<string, AnimationClip>();
            var missing = new List<string>();

            foreach (string slot in MonsterCatalog.AllSlots)
            {
                AnimationClip clip = map.TryGetValue(slot, out string clipName)
                    ? MonsterCatalog.FindClip(species, clipName)
                    : null;

                if (clip == null && map.ContainsKey(slot)) missing.Add($"{slot}={map[slot]}");
                resolved[slot] = clip;
            }

            ApplyFallbacks(resolved);

            // 전진이 구워진 클립은 제자리(in-place) 사본으로 바꾼다 — 위치·회전의 주인은
            // MovementComponent다(파일 머리 주석). 원본 클립이 스켈레톤 루트를 실제로 이동시키면
            // 루트모션 없이 재생될 때 클립 루프마다 시작점으로 스냅해, 걷는 몬스터가
            // "조금씩 끊기며 뒤로 순간이동"하는 것처럼 보인다 (Grenadier 걷기 실측 초속 2m).
            var slotKeys = new List<string>(resolved.Keys);
            foreach (string slot in slotKeys)
                if (resolved[slot] != null)
                    resolved[slot] = MakeInPlace(species, resolved[slot]);

            // 공용 컨트롤러와 같은 이유로 제자리 수정한다 — 지우고 새로 만들면 이 에셋을 물고 있는
            // 몬스터 프리팹의 참조가 그 세션 동안 null이 되어, 애니메이션 없는 몬스터가 조용히 생긴다.
            string path = species.OverrideController;
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(path);
            if (controller == null)
            {
                controller = new AnimatorOverrideController();
                AssetDatabase.CreateAsset(controller, path);
            }
            controller.runtimeAnimatorController = baseController;

            var overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>();
            controller.GetOverrides(overrides);

            for (int i = 0; i < overrides.Count; i++)
            {
                AnimationClip key = overrides[i].Key;
                if (key != null && resolved.TryGetValue(key.name, out AnimationClip value) && value != null)
                    overrides[i] = new KeyValuePair<AnimationClip, AnimationClip>(key, value);
            }
            controller.ApplyOverrides(overrides);
            EditorUtility.SetDirty(controller);

            int filled = 0;
            foreach (var kv in resolved) if (kv.Value != null) filled++;

            string warn = missing.Count > 0 ? $"  (못 찾음: {string.Join(", ", missing)})" : "";
            return $"[{species.Id}] 오버라이드 {filled}/{resolved.Count} 슬롯 → {path}{warn}";
        }

        /// <summary>
        /// 루트 전진이 구워진 클립을 제자리 사본으로 바꾼다. 사본에서 지우는 것은
        ///   · RootT.x/z — 임포터가 추출한 루트모션 수평 성분
        ///   · 순 이동(drift)이 있는 본의 m_LocalPosition.x/z — 아바타 해석이 안 될 때
        ///     문자 그대로 재생되는 루트 본 위치 커브 (제자리 흔들림은 순 이동이 0이라 살아남는다)
        /// 수직(y)은 걸음의 들썩임이므로 남긴다. 이미 제자리인 클립은 원본을 그대로 쓴다.
        /// </summary>
        private static AnimationClip MakeInPlace(MonsterCatalog.Species species, AnimationClip source)
        {
            Vector3 horizontal = source.averageSpeed;
            horizontal.y = 0f;
            if (horizontal.magnitude < 0.05f) return source;

            string folder = MonsterCatalog.AnimRoot + "/InPlace";
            MonsterCatalog.EnsureFolder(folder);
            string path = $"{folder}/{species.Id}_{source.name}.anim";

            var copy = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (copy == null)
            {
                copy = new AnimationClip();
                AssetDatabase.CreateAsset(copy, path);
            }
            EditorUtility.CopySerialized(source, copy);
            copy.name = $"{species.Id}_{source.name}";

            foreach (var binding in AnimationUtility.GetCurveBindings(copy))
            {
                bool strip = binding.propertyName == "RootT.x" || binding.propertyName == "RootT.z";

                if (!strip && (binding.propertyName == "m_LocalPosition.x" ||
                               binding.propertyName == "m_LocalPosition.z"))
                {
                    var curve = AnimationUtility.GetEditorCurve(copy, binding);
                    strip = curve != null && curve.length >= 2 &&
                            Mathf.Abs(curve.keys[curve.length - 1].value - curve.keys[0].value) > 0.1f;
                }

                if (strip) AnimationUtility.SetEditorCurve(copy, binding, null);
            }

            EditorUtility.SetDirty(copy);
            return copy;
        }

        /// <summary>
        /// 빈 슬롯을 이웃 슬롯으로 메운다. 자리표시 클립이 그대로 남으면 그 상태에서 몬스터가
        /// 얼어붙으므로, 어색하더라도 뭔가는 재생되게 한다.
        /// </summary>
        private static void ApplyFallbacks(Dictionary<string, AnimationClip> resolved)
        {
            AnimationClip idle = resolved[MonsterCatalog.SlotIdle];

            Fallback(resolved, MonsterCatalog.SlotWalk, idle);
            Fallback(resolved, MonsterCatalog.SlotRun, resolved[MonsterCatalog.SlotWalk] ?? idle);
            Fallback(resolved, MonsterCatalog.SlotAlert, idle);

            AnimationClip attack0 = resolved[MonsterCatalog.SlotAttackPrefix + 0] ?? idle;
            for (int i = 0; i < MonsterCatalog.AttackSlots; i++)
                Fallback(resolved, MonsterCatalog.SlotAttackPrefix + i, attack0);

            AnimationClip hit0 = resolved[MonsterCatalog.SlotHitPrefix + 0] ?? idle;
            for (int i = 0; i < MonsterCatalog.HitSlots; i++)
                Fallback(resolved, MonsterCatalog.SlotHitPrefix + i, hit0);

            Fallback(resolved, MonsterCatalog.SlotDeath, hit0);
        }

        private static void Fallback(Dictionary<string, AnimationClip> resolved, string slot, AnimationClip alt)
        {
            if (resolved[slot] == null) resolved[slot] = alt;
        }
    }
}
