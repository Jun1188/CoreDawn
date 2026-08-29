using System;
using System.Collections.Generic;
using UnityEngine;

namespace CoreDawn.Save
{
    /// <summary>
    /// 구버전 세이브를 현재 스키마로 끌어올린다.
    ///
    /// 개발 중 데이터 구조가 바뀌는 것은 정상이다. 이 장치가 없으면 구조를 바꿀 때마다
    /// 팀원들이 갖고 있던 세이브가 전부 못 쓰게 되고, 결국 "세이브 지우고 다시 하세요"가 반복된다.
    ///
    /// 쓰는 법: <see cref="SaveFile.CurrentSchemaVersion"/>을 올리고, 여기 Steps에
    /// "이전 버전 → 다음 버전" 변환 하나를 추가한다. 변환은 JSON 트리를 직접 만지므로
    /// 옛 DTO 클래스를 남겨둘 필요가 없다.
    ///
    /// 예시:
    ///   { 1, f => { f.Modules["combat"]["nests"] ... 새 필드 채우기 ... } },
    /// </summary>
    public static class SaveMigrations
    {
        /// <summary>key = 이 단계가 적용되는 버전. 실행하면 key+1 버전이 된다.</summary>
        static readonly Dictionary<int, Action<SaveFile>> Steps = new()
        {
            // 아직 v1이 최초 버전이라 단계가 없다.
        };

        /// <summary>
        /// 세이브를 현재 버전까지 순차 변환한다.
        /// 미래 버전(이 빌드보다 새 세이브)은 변환할 수 없으므로 false를 반환한다.
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
