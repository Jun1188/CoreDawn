using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEngine;

/// <summary>
/// 세이브 파일의 디스크 입출력 — 경로 규칙, gzip, 안전한 덮어쓰기.
///
/// 덮어쓰기가 안전해야 하는 이유: 저장 도중 게임이 죽으면 반쯤 쓰인 파일이 남는다.
/// 그래서 항상 임시 파일에 먼저 다 쓰고, 다 쓴 뒤에 한 번의 교체 연산으로 갈아끼운다.
/// 교체할 때 기존 파일은 .bak으로 밀려나므로, 본체가 깨져도 직전 세이브로 돌아갈 수 있다.
///
/// 레이아웃:
///   {persistentDataPath}/saves/{slotId}/save.json.gz   본체
///                                      /meta.json      목록 표시용 요약 (비압축)
///                                      /save.bak.gz    직전 세이브
/// </summary>
public static class SaveStorage
{
    /// <summary>세이브 폴더 이름 — persistentDataPath 바로 아래.</summary>
    public const string SavesFolder = "saves";

    const string MetaFileName = "meta.json";
    const string TempFileName = "save.tmp";

    /// <summary>압축을 끄면 확장자가 .json이 되어 텍스트 에디터로 바로 열린다 (디버깅용).</summary>
    static string BodyName(bool compressed) => compressed ? "save.json.gz" : "save.json";
    static string BackupName(bool compressed) => compressed ? "save.bak.gz" : "save.bak.json";

    /// <summary>모든 슬롯이 들어가는 최상위 폴더.</summary>
    public static string RootDir => Path.Combine(Application.persistentDataPath, SavesFolder);

    public static string SlotDir(string slotId) => Path.Combine(RootDir, slotId);

    // ── 쓰기 ──────────────────────────────────────────────────────

    /// <summary>슬롯에 세이브를 기록한다. 실패 시 false를 반환하고 기존 파일은 건드리지 않는다.</summary>
    public static bool Write(string slotId, SaveFile file, bool compress)
    {
        try
        {
            string dir = SlotDir(slotId);
            Directory.CreateDirectory(dir);

            string json = SaveJson.Serialize(file);
            byte[] payload = compress
                ? Gzip(Encoding.UTF8.GetBytes(json))
                : Encoding.UTF8.GetBytes(json);

            string tmp = Path.Combine(dir, TempFileName);
            string body = Path.Combine(dir, BodyName(compress));
            string bak = Path.Combine(dir, BackupName(compress));

            File.WriteAllBytes(tmp, payload);
            ReplaceKeepingBackup(tmp, body, bak);

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

    // ── 읽기 ──────────────────────────────────────────────────────

    /// <summary>슬롯을 읽는다. 본체가 깨졌으면 백업으로 한 번 더 시도한다. 둘 다 실패하면 null.</summary>
    public static SaveFile Read(string slotId)
    {
        string dir = SlotDir(slotId);
        if (!Directory.Exists(dir)) return null;

        // 압축본 → 비압축본 → 백업 순으로 시도 (개발 중 압축 설정을 바꿔가며 쓸 수 있다)
        foreach (var candidate in CandidateBodies(dir))
        {
            if (!File.Exists(candidate.Path)) continue;

            var file = TryReadOne(candidate.Path, candidate.Gzipped);
            if (file != null) return file;

            Debug.LogWarning($"[Save] '{candidate.Path}' 를 읽지 못해 다음 후보로 넘어갑니다.");
        }
        return null;
    }

    readonly struct BodyCandidate
    {
        public readonly string Path;
        public readonly bool Gzipped;
        public BodyCandidate(string path, bool gzipped) { Path = path; Gzipped = gzipped; }
    }

    static List<BodyCandidate> CandidateBodies(string dir) => new()
    {
        new BodyCandidate(Path.Combine(dir, BodyName(true)), true),
        new BodyCandidate(Path.Combine(dir, BodyName(false)), false),
        new BodyCandidate(Path.Combine(dir, BackupName(true)), true),
        new BodyCandidate(Path.Combine(dir, BackupName(false)), false),
    };

    static SaveFile TryReadOne(string path, bool gzipped)
    {
        try
        {
            byte[] raw = File.ReadAllBytes(path);
            string json = Encoding.UTF8.GetString(gzipped ? Gunzip(raw) : raw);

            var file = SaveJson.Deserialize<SaveFile>(json);
            if (file == null) return null;

            if (file.Meta == null) file.Meta = new SaveMeta();
            if (file.Modules == null) file.Modules = new Dictionary<string, Newtonsoft.Json.Linq.JToken>();
            return file;
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

    // ── gzip ─────────────────────────────────────────────────────

    static byte[] Gzip(byte[] data)
    {
        using (var outStream = new MemoryStream())
        {
            using (var gz = new GZipStream(outStream, CompressionMode.Compress))
                gz.Write(data, 0, data.Length);
            return outStream.ToArray();
        }
    }

    static byte[] Gunzip(byte[] data)
    {
        using (var inStream = new MemoryStream(data))
        using (var gz = new GZipStream(inStream, CompressionMode.Decompress))
        using (var outStream = new MemoryStream())
        {
            gz.CopyTo(outStream);
            return outStream.ToArray();
        }
    }
}
