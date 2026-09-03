using System;
using System.Collections.Generic;
using UnityEngine;

namespace CoreDawn.Save
{
    /// <summary>
    /// 구버전 세이브를 현재 스키마로 끌어올린다.
    ///
    /// 쓰는 법: <see cref="SaveFile.CurrentSchemaVersion"/>을 올리고, 여기 Steps에
    /// "이전 버전 → 다음 버전" 변환 하나를 추가한다. 변환은 NBT 트리(SaveFile.Modules)를 직접 만지므로
    /// 옛 DTO 클래스를 남겨둘 필요가 없다.
    ///
    /// 읽는 쪽(DTO·SaveRefs)에는 옛 키·옛 id를 받아 주는 폴백을 두지 않는다 — 변환은 전부 여기서, 한 번, 소리 내어 한다.
    /// 변환할 수 없는 세이브는 조용히 반쯤 열리는 대신 로드가 실패한다(SaveManager가 오류를 남긴다).
    ///
    /// 내력: v1~v5 는 JSON(save.json.gz)이었고 단계 넷(팩 id·역할 키 그릇·행동→모듈·몬스터/튜토리얼 id)이 있었다.
    /// v6(2026-09-03, 5단계)에서 본체가 NBT 로 바뀌며 베타 전이라 그 단계들과 함께 JSON 세이브 지원을 끊었다 —
    /// <see cref="OldestReadable"/> 미만은 SaveStorage 가 읽기 단계에서 거절한다.
    /// </summary>
    public static class SaveMigrations
    {
        /// <summary>이 빌드가 열 수 있는 가장 오래된 스키마 — v6(NBT).</summary>
        public const int OldestReadable = 6;

        /// <summary>key = 이 단계가 적용되는 버전. 실행하면 key+1 버전이 된다.</summary>
        static readonly Dictionary<int, Action<SaveFile>> Steps = new()
        {
        };

        /// <summary>
        /// 세이브를 현재 버전까지 순차 변환한다.
        /// 미래 버전(이 빌드보다 새 세이브)과 지원이 끊긴 옛 버전은 변환할 수 없으므로 false를 반환한다.
        /// </summary>
        public static bool TryMigrate(SaveFile file, out string error)
        {
            error = null;
            if (file == null) { error = "세이브가 비어 있습니다."; return false; }
            if (file.SchemaVersion > SaveFile.CurrentSchemaVersion)
            {
                error = $"이 세이브는 더 새로운 버전(v{file.SchemaVersion})입니다 — " +
                        $"현재 빌드는 v{SaveFile.CurrentSchemaVersion}까지 읽을 수 있습니다.";
                return false;
            }
            if (file.SchemaVersion < OldestReadable)
            {
                error = $"베타 이전 세이브(v{file.SchemaVersion})는 더 이상 열 수 없습니다 — 현재 빌드는 v{OldestReadable} 부터 읽습니다.";
                return false;
            }
            while (file.SchemaVersion < SaveFile.CurrentSchemaVersion)
            {
                if (!Steps.TryGetValue(file.SchemaVersion, out var step))
                {
                    error = $"v{file.SchemaVersion} → v{file.SchemaVersion + 1} 변환 단계가 없습니다.";
                    return false;
                }
                int from = file.SchemaVersion;
                try
                {
                    step(file);
                }
                catch (Exception e)
                {
                    error = $"v{from} → v{from + 1} 변환 중 오류: {e.Message}";
                    return false;
                }
                file.SchemaVersion++;
                Debug.Log($"[Save] 세이브 마이그레이션 v{from} → v{file.SchemaVersion}");
            }
            return true;
        }
    }
}
