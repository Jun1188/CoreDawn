using System.Collections.Generic;
using UnityEngine;
using CoreDawn.Sim;

namespace CoreDawn.Managers
{
    /// <summary>
    /// 내장 연출 등록부 — 파티클·탄 프리팹(<c>Resources/Builtin/Effects/&lt;이름&gt;.prefab</c>)을 <b>이름</b>으로 찾는다.
    /// 파티클 시스템·궤적·Bullet 컴포넌트는 유니티 전용이라 팩 파일로 실을 수 없다(셰이더처럼 코드 쪽 자원). 팩은 이름만 적는다:
    /// 탄약 아이템 <c>view.bullet / muzzleFlash / hitEffect</c>. 없으면 오류 + null — 판정은 되지만 몸·연출 없이 나간다.
    /// </summary>
    public static class BuiltinEffects
    {
        const string Folder = "Builtin/Effects/";
        static readonly Dictionary<string, GameObject> cache = new Dictionary<string, GameObject>();
        static readonly HashSet<string> warned = new HashSet<string>();

        public static GameObject Of(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (cache.TryGetValue(name, out var go) && go != null) return go;
            go = Resources.Load<GameObject>(Folder + name);
            if (go == null) { if (warned.Add(name)) Debug.LogError($"[BuiltinEffects] 내장 연출 '{name}'이 없습니다(Resources/{Folder}{name}.prefab)."); return null; }
            cache[name] = go;
            return go;
        }

        /// <summary>탄약 아이템의 연출 셋 — view.bullet(탄 몸, Bullet 컴포넌트)·muzzleFlash·hitEffect.</summary>
        public sealed class Ammo
        {
            public GameObject bulletPrefab, muzzleFlashPrefab, hitEffectPrefab;
        }

        public static Ammo AmmoOf(ItemDef item)
        {
            var v = item?.View;
            if (v == null) return null;
            return new Ammo
            {
                bulletPrefab = Of((string)v["bullet"]),
                muzzleFlashPrefab = Of((string)v["muzzleFlash"]),
                hitEffectPrefab = Of((string)v["hitEffect"]),
            };
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() { cache.Clear(); warned.Clear(); }
    }
}
