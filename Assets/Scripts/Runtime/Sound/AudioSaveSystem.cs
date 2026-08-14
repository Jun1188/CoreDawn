using System.IO;
using UnityEngine;

public static class AudioSaveSystem
{
    // 저장 경로 프로퍼티
    private static string SavePath
    {
        get
        {
#if UNITY_EDITOR
            // ★ 에디터 테스트용: Assets/Data/audio_settings.json
            string folderPath = Path.Combine(Application.dataPath, "Data");

            // Assets/Data 폴더가 없으면 자동으로 생성!
            if (!Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
            }

            return Path.Combine(folderPath, "audio_settings.json");
#else
            // ★ 실제 빌드(.exe)용: 안전한 사용자 데이터 경로
            return Path.Combine(Application.persistentDataPath, "audio_settings.json");
#endif
        }
    }

    /// <summary> JSON 파일 읽어오기 </summary>
    public static AudioSettingsData LoadSettings()
    {
        if (File.Exists(SavePath))
        {
            string json = File.ReadAllText(SavePath);
            return JsonUtility.FromJson<AudioSettingsData>(json);
        }

        // 파일이 없으면 기본값으로 생성 및 저장
        AudioSettingsData defaultData = new AudioSettingsData();
        SaveSettings(defaultData);
        return defaultData;
    }

    /// <summary> JSON 파일 저장하기 </summary>
    public static void SaveSettings(AudioSettingsData data)
    {
        string json = JsonUtility.ToJson(data, true);
        File.WriteAllText(SavePath, json);
    }
}