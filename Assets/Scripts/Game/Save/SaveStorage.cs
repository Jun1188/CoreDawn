using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using fNbt;
using UnityEngine;

namespace CoreDawn.Save
{
    /// <summary>
    /// 세이브 파일의 디스크 입출력 — 경로 규칙, NBT(gzip), 안전한 덮어쓰기.
    ///
    /// 덮어쓰기가 안전해야 하는 이유: 저장 도중 게임이 죽으면 반쯤 쓰인 파일이 남는다.
    /// 그래서 항상 임시 파일에 먼저 다 쓰고, 다 쓴 뒤에 한 번의 교체 연산으로 갈아끼운다.
    /// 교체할 때 기존 파일은 .bak으로 밀려나므로, 본체가 깨져도 직전 세이브로 돌아갈 수 있다.
    ///
    /// 레이아웃:
    ///   {persistentDataPath}/saves/{slotId}/save.nbt.gz     본체 (fNbt, gzip)
    ///                                      /meta.json        목록 표시용 요약 (JSON, 비압축)
    ///                                      /save.bak.nbt.gz  직전 세이브
    ///
    /// 본체 꼴: root "coredawn" { schemaVersion: Int, meta: Compound(SaveMeta), modules: Compound { &lt;moduleId&gt;: Compound } }.
    /// JSON 시절(v1~v5, save.json.gz)은 베타 전 정리(5단계)에서 지원을 끊었다 — 그런 슬롯은 읽기가 실패하고 오류를 남긴다.
    /// </summary>
    public static class SaveStorage
    {
        /// <summary>세이브 폴더 이름 — persistentDataPath 바로 아래.</summary>
        public const string SavesFolder = "saves";
        const string MetaFileName = "meta.json";
        const string TempFileName = "save.tmp";
        const string RootTagName = "coredawn";

        /// <summary>압축을 끄면 .nbt 그대로라 NBT 뷰어로 바로 열린다 (디버깅용).</summary>
        static string BodyName(bool compressed) => compressed ? "save.nbt.gz" : "save.nbt";
        static string BackupName(bool compressed) => compressed ? "save.bak.nbt.gz" : "save.bak.nbt";
        static readonly string[] LegacyBodies = { "save.json.gz", "save.json" };

        /// <summary>모든 슬롯이 들어가는 최상위 폴더.</summary>
        public static string RootDir => Path.Combine(Application.persistentDataPath, SavesFolder);
        public static string SlotDir(string slotId) => Path.Combine(RootDir, slotId);

        // ── 쓰기 ────────────────────────────────────────────────

        /// <summary>슬롯에 세이브를 기록한다. 실패 시 false를 반환하고 기존 파일은 건드리지 않는다.</summary>
        public static bool Write(string slotId, SaveFile file, bool compress)
        {
            try
            {
                string dir = SlotDir(slotId);
                Directory.CreateDirectory(dir);

                string tmp = Path.Combine(dir, TempFileName);
                string body = Path.Combine(dir, BodyName(compress));
                string bak = Path.Combine(dir, BackupName(compress));

                try
                {
                    new NbtFile(ToRoot(file)).SaveToFile(tmp, compress ? NbtCompression.GZip : NbtCompression.None);
                    ReplaceKeepingBackup(tmp, body, bak);
                }
                finally
                {
                    if (File.Exists(tmp)) File.Delete(tmp);   // 쓰다 만 임시 파일은 남기지 않는다
                }

                // 요약은 본체가 무사히 자리잡은 뒤에 쓴다 — 순서가 반대면
                // 목록에는 새 세이브가 보이는데 본체는 옛것인 상태가 생길 수 있다
                File.WriteAllText(Path.Combine(dir, MetaFileName), SaveJson.Serialize(file.Meta), Encoding.UTF8);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[Save] 슬롯 '{slotId}' 저장 실패: {e}");
                return false;
            }
        }

        /// <summary>SaveFile → 루트 compound. 모듈 태그는 복제해 넣는다(태그는 부모가 하나뿐이다).</summary>
        public static NbtCompound ToRoot(SaveFile file)
        {
            var root = new NbtCompound(RootTagName) { new NbtInt("schemaVersion", file.SchemaVersion) };
            root.Add(SaveNbt.ToTag(file.Meta ?? new SaveMeta(), "meta"));
            var modules = new NbtCompound("modules");
            if (file.Modules != null)
                foreach (var kv in file.Modules)
                {
                    if (kv.Value == null) continue;
                    var c = (NbtCompound)kv.Value.Clone();
                    c.Name = kv.Key;
                    modules.Add(c);
                }
            root.Add(modules);
            return root;
        }

        /// <summary>루트 compound → SaveFile. 모듈 태그는 복제해 꺼낸다.</summary>
        public static SaveFile FromRoot(NbtCompound root)
        {
            if (root == null) return null;
            var file = new SaveFile
            {
                SchemaVersion = root["schemaVersion"] is NbtInt v ? v.Value : 0,
                Meta = SaveNbt.FromTag<SaveMeta>(root["meta"]) ?? new SaveMeta(),
                Modules = new Dictionary<string, NbtCompound>(),
            };
            if (root["modules"] is NbtCompound modules)
                foreach (var tag in modules.Tags)
                    if (tag is NbtCompound c) file.Modules[tag.Name] = (NbtCompound)c.Clone();
            return file;
        }

        /// <summary>tmp를 dest 자리에 앉히고 기존 dest는 bak으로 민다.</summary>
        static void ReplaceKeepingBackup(string tmp, string dest, string bak)
        {
            if (!File.Exists(dest))
            {
                File.Move(tmp, dest);
                return;
            }
            // File.Replace는 교체와 백업 생성이 한 연산이라 중간 상태가 없다.
            // 다만 일부 파일시스템(네트워크 드라이브 등)에서 지원되지 않으므로 수동 폴백을 둔다.
            try
            {
                File.Replace(tmp, dest, bak);
            }
            catch (PlatformNotSupportedException)
            {
                ManualReplace(tmp, dest, bak);
            }
            catch (IOException)
            {
                ManualReplace(tmp, dest, bak);
            }
        }

        static void ManualReplace(string tmp, string dest, string bak)
        {
            if (File.Exists(bak)) File.Delete(bak);
            File.Move(dest, bak);
            File.Move(tmp, dest);
        }

        // ── 읽기 ────────────────────────────────────────────────

        /// <summary>슬롯을 읽는다. 본체가 깨졌으면 백업으로 한 번 더 시도한다. 둘 다 실패하면 null(옛 JSON 슬롯이면 오류 로그).</summary>
        public static SaveFile Read(string slotId)
        {
            string dir = SlotDir(slotId);
            if (!Directory.Exists(dir)) return null;

            bool any = false;
            // 압축본 → 비압축본 → 백업 순으로 시도 (개발 중 압축 설정을 바꿔가며 쓸 수 있다)
            foreach (var candidate in CandidateBodies(dir))
            {
                if (!File.Exists(candidate)) continue;
                any = true;
                var file = TryReadOne(candidate);
                if (file != null) return file;
                Debug.LogWarning($"[Save] '{candidate}' 를 읽지 못해 다음 후보로 넘어갑니다.");
            }
            if (!any)
                foreach (var legacy in LegacyBodies)
                    if (File.Exists(Path.Combine(dir, legacy)))
                    {
                        Debug.LogError($"[Save] 슬롯 '{slotId}' 는 베타 이전 JSON 세이브(v{SaveMigrations.OldestReadable - 1} 이하)라 열 수 없습니다 — 새 게임을 시작하세요.");
                        break;
                    }
            return null;
        }

        static IEnumerable<string> CandidateBodies(string dir)
        {
            yield return Path.Combine(dir, BodyName(true));
            yield return Path.Combine(dir, BodyName(false));
            yield return Path.Combine(dir, BackupName(true));
            yield return Path.Combine(dir, BackupName(false));
        }

        static SaveFile TryReadOne(string path)
        {
            try
            {
                var nbt = new NbtFile();
                nbt.LoadFromFile(path);   // 압축 여부는 자동 감지
                if (nbt.RootTag == null || nbt.RootTag.Name != RootTagName)
                {
                    Debug.LogWarning($"[Save] '{path}' 루트 태그가 '{RootTagName}' 이 아닙니다: '{nbt.RootTag?.Name}'");
                    return null;
                }
                return FromRoot(nbt.RootTag);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Save] '{path}' 읽기 실패: {e.Message}");
                return null;
            }
        }

        /// <summary>파일 본체를 열지 않고 요약만 읽는다 (슬롯 목록 화면용). 비어 있으면 null.</summary>
        public static SaveMeta ReadMeta(string slotId)
        {
            string metaPath = Path.Combine(SlotDir(slotId), MetaFileName);
            if (File.Exists(metaPath))
            {
                try
                {
                    var m = SaveJson.Deserialize<SaveMeta>(File.ReadAllText(metaPath, Encoding.UTF8));
                    if (m != null && !m.IsEmpty) return m;
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Save] '{metaPath}' 요약 읽기 실패, 본체에서 복구를 시도합니다: {e.Message}");
                }
            }
            // 요약이 없거나 깨졌으면 본체에서 되살린다 (본체에도 같은 내용이 들어 있다)
            var file = Read(slotId);
            return file != null ? file.Meta : null;
        }

        public static bool Exists(string slotId) => ReadMeta(slotId) != null;

        /// <summary>슬롯 폴더를 통째로 지운다.</summary>
        public static bool Delete(string slotId)
        {
            try
            {
                string dir = SlotDir(slotId);
                if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[Save] 슬롯 '{slotId}' 삭제 실패: {e}");
                return false;
            }
        }
    }
}
