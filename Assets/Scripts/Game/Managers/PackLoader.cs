using System.IO;
using UnityEngine;
using CoreDawn.Sim;

namespace CoreDawn.Managers
{
    /// <summary>
    /// 팩 json → SimDatabase. 경로·플랫폼을 아는 유일한 곳(심은 파일을 모른다). 기본 팩은 StreamingAssets/packs/coredawn.
    /// 모드는 같은 폴더에 다른 팩을 두는 것으로 시작한다(합치기는 나중).
    /// </summary>
    public static class PackLoader
    {
        public const string DefaultPack = "coredawn";

        public static string PathOf(string pack) => Path.Combine(Application.streamingAssetsPath, "packs", pack, "data.json");

        public static SimDatabase Load(string pack = DefaultPack)
        {
            string path = PathOf(pack);
            if (!File.Exists(path))
            {
                Debug.LogError($"[PackLoader] 팩 파일이 없습니다: {path} — GameData 에디터에서 저장하면 생성됩니다");
                return null;
            }
            var db = SimDatabase.Load(File.ReadAllText(path), pack, strict: false);
            if (db.Errors.Count > 0)
                Debug.LogError($"[PackLoader] {pack}: 정의 오류 {db.Errors.Count}건\n  " + string.Join("\n  ", db.Errors));
            else
                Debug.Log($"[PackLoader] {pack}: entities {db.Entities.Count} · items {db.Items.Count} · recipes {db.Recipes.Count} · effects {db.Effects.Count} · guns {db.Guns.Count} · wave {(db.Wave != null ? "rule" : "none")}");
            return db;
        }

        // 씬이 뜨기 전에 로더를 꽂아 둔다 — 첫 요청(스폰·효과 변환)이 읽는다
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register() => SimHost.DatabaseLoader = () => Load();
    }
}
