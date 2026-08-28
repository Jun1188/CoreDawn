using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace CoreDawn.EditorTools
{
    /// <summary>
    /// 코드로 굽는 애니메이션 에셋(.anim / .controller)을 <b>제자리에서</b> 갱신하는 도우미.
    ///
    /// 왜 있는가: 원래 타워·몬스터 빌더는 둘 다 <c>AssetDatabase.DeleteAsset</c> 후 새로 만들었다.
    /// 그러면 GUID가 .meta 덕에 유지되더라도 <b>에셋의 객체 정체성</b>이 갈려서, 이미 그 에셋을
    /// 참조하던 프리팹의 참조가 그 세션 동안 <c>null</c>이 된다(AssetDatabase.Refresh로도 안 낫는다).
    /// 몬스터 쪽에서 실제로 재현했다 — 애니메이션 빌더만 돌리고 리그 빌더를 안 돌리면
    /// <b>애니메이션이 조용히 사라진 프리팹</b>이 남는다.
    ///
    /// 제자리 갱신은 그 문제를 뿌리에서 없앨 뿐 아니라, 팀원이 인스펙터에서 이 에셋들을 어딘가에
    /// 물려 놨어도 그 참조를 살려 준다.
    /// </summary>
    public static class GeneratedAnimationAssets
    {
        /// <summary>
        /// 새로 만든 클립의 내용을 경로의 기존 클립에 덮어써서 <b>같은 에셋</b>으로 유지한다.
        /// 처음이면 그냥 만든다. 돌려주는 것은 항상 '경로에 실제로 존재하는' 클립이다.
        /// </summary>
        public static AnimationClip SaveClip(AnimationClip fresh, string path)
        {
            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(fresh, path);
                return AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            }

            // 커브·클립 설정을 통째로 옮긴다. 에셋 자체는 그대로라 참조가 살아남는다.
            EditorUtility.CopySerialized(fresh, existing);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        /// <summary>경로의 컨트롤러를 가져오거나, 없으면 만든다.</summary>
        public static AnimatorController LoadOrCreateController(string path)
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            return controller != null
                ? controller
                : AnimatorController.CreateAnimatorControllerAtPath(path);
        }

        /// <summary>
        /// 상태·전이·파라미터를 전부 비워 빈 그래프로 되돌린다.
        /// <c>RemoveState</c>가 상태와 그 전이를 서브에셋에서도 지워 주지만 블렌드트리는 남으므로,
        /// 주인 잃은 블렌드트리는 따로 쓸어낸다 (안 그러면 다시 지을 때마다 파일 안에 죽은 트리가 쌓인다).
        /// </summary>
        public static void ClearController(AnimatorController controller)
        {
            AnimatorStateMachine sm = controller.layers[0].stateMachine;

            foreach (var t in sm.anyStateTransitions) sm.RemoveAnyStateTransition(t);
            foreach (var t in sm.entryTransitions) sm.RemoveEntryTransition(t);
            foreach (var child in sm.stateMachines) sm.RemoveStateMachine(child.stateMachine);
            foreach (var child in sm.states) sm.RemoveState(child.state);

            while (controller.parameters.Length > 0) controller.RemoveParameter(0);

            string path = AssetDatabase.GetAssetPath(controller);
            foreach (Object sub in AssetDatabase.LoadAllAssetsAtPath(path))
                if (sub is BlendTree) Object.DestroyImmediate(sub, true);
        }
    }
}
