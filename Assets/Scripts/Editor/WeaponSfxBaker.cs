using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace CoreDawn.EditorTools
{
    /// <summary>
    /// 근접무기 효과음을 <b>코드로 굽는</b> 생성기 — 타워/몬스터 리그 빌더와 같은 성격이다.
    ///
    /// 왜 굽는가: 프로젝트의 사운드 라이브러리에 칼 휘두르는 소리가 한 개도 없다(총기·폭발·발소리뿐).
    /// 총기 클립을 돌려 쓰면 근접무기인데 총소리가 나므로, 필요한 소리를 직접 합성해 wav로 남긴다.
    /// 수치를 바꿔 다시 구우면 같은 경로의 에셋이 제자리에서 갱신되므로 이미 물려 둔 참조가 살아 있다.
    ///
    /// 합성 구성: ① 휘두름(whoosh) — 대역통과 필터를 스윕하는 노이즈, ② 플라즈마 — 링모듈레이션 톱니.
    /// </summary>
    public static class WeaponSfxBaker
    {
        const string OutPath = "Assets/Art/Audio/Weapon/PlasmaCutter_Swing.wav";
        const string SoundId = "Sound:PlasmaCutterFire";

        const int   SampleRate = 44100;
        const float Duration   = 0.36f;

        [MenuItem("Tools/Weapons/Bake Melee SFX")]
        public static void Bake()
        {
            int n = Mathf.RoundToInt(SampleRate * Duration);
            var buf = new float[n];

            var rng = new System.Random(20260820);   // 고정 시드 — 다시 구워도 같은 소리가 나온다

            // 대역통과 상태변수 필터 상태
            float low = 0f, band = 0f;

            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;                       // 0~1 진행도

                // ── ① 휘두름: 옆을 스쳐 지나가는 바람. 중심주파수가 올라갔다 내려온다
                float fc = t < 0.45f
                    ? Mathf.Lerp(400f, 1900f, t / 0.45f)
                    : Mathf.Lerp(1900f, 500f, (t - 0.45f) / 0.55f);
                float f = 2f * Mathf.Sin(Mathf.PI * fc / SampleRate);
                const float q = 0.55f;                        // 낮을수록 쨍한 공명

                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                float high = noise - low - q * band;
                band += f * high;
                low  += f * band;

                // 어택 12ms → 완만한 감쇠. 스쳐 지나가는 느낌이라 중간이 가장 크다.
                float attack = Mathf.Clamp01(t / 0.035f);
                float decay  = Mathf.Pow(1f - Mathf.Clamp01((t - 0.035f) / 0.965f), 1.6f);
                float whoosh = band * attack * decay * 0.9f;

                // ── ② 플라즈마: 링모듈레이션된 톱니. 짧게 지직거리고 사라진다
                float phase = t * Duration * 170f;
                float saw = (phase - Mathf.Floor(phase)) * 2f - 1f;
                float ring = Mathf.Sin(2f * Mathf.PI * 93f * t * Duration);
                float zapEnv = Mathf.Exp(-t * 16f);
                float zap = saw * ring * zapEnv * 0.35f;

                buf[i] = whoosh + zap;
            }

            // 정규화 + 양끝 페이드(클릭 방지)
            float peak = 0f;
            for (int i = 0; i < n; i++) peak = Mathf.Max(peak, Mathf.Abs(buf[i]));
            float gain = peak > 0.0001f ? 0.85f / peak : 1f;
            int fade = SampleRate / 200;   // 5ms
            for (int i = 0; i < n; i++)
            {
                float edge = Mathf.Min(1f, Mathf.Min(i, n - 1 - i) / (float)fade);
                buf[i] *= gain * edge;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(OutPath));
            File.WriteAllBytes(OutPath, EncodeWav(buf, SampleRate));
            AssetDatabase.ImportAsset(OutPath, ImportAssetOptions.ForceUpdate);

            var importer = (AudioImporter)AssetImporter.GetAtPath(OutPath);
            if (importer != null)
            {
                var s = importer.defaultSampleSettings;
                s.loadType = AudioClipLoadType.DecompressOnLoad;   // 짧은 효과음 — 지연 없이 즉시
                s.preloadAudioData = true;                         // 플랫폼별 설정으로 옮겨진 항목
                importer.defaultSampleSettings = s;
                importer.SaveAndReimport();
            }

            // 팩 소리 항목에 물려 준다 — 소리의 정본은 v1 GameData.json의 sounds(Sound:PlasmaCutterFire). 한 번의 메뉴 클릭으로 굽기+배선이 끝나야 재실행이 쉽다.
            bool wired = false;
            string guid = AssetDatabase.AssetPathToGUID(OutPath);
            string jsonPath = $"{GameDataJson.ImportFolder}/GameData.json";
            if (!string.IsNullOrEmpty(guid) && File.Exists(jsonPath))
            {
                var root = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(jsonPath));
                if (root["sounds"] is Newtonsoft.Json.Linq.JArray sounds)
                    foreach (var snd in sounds)
                        if ((string)snd["id"] == SoundId)
                        {
                            snd["clips"] = new Newtonsoft.Json.Linq.JArray { new Newtonsoft.Json.Linq.JObject { ["clip"] = Path.GetFileNameWithoutExtension(OutPath), ["clipGuid"] = guid } };
                            wired = true;
                        }
                if (wired)
                {
                    File.WriteAllText(jsonPath, root.ToString(Newtonsoft.Json.Formatting.Indented) + "\n");
                    AssetDatabase.ImportAsset(jsonPath);
                }
            }

            Debug.Log($"[WeaponSfxBaker] {OutPath} 생성 ({Duration:F2}s) — " +
                      (wired ? $"{SoundId}에 배선했습니다. GameData 편집기에서 저장(v2 내보내기 + 카탈로그 베이크)하세요." : $"{jsonPath}의 sounds에 {SoundId}가 없어 배선은 생략."));
        }

        /// <summary>float(-1~1) 샘플을 16bit PCM 모노 RIFF/WAVE 바이트로.</summary>
        static byte[] EncodeWav(float[] samples, int sampleRate)
        {
            using var stream = new MemoryStream();
            using var w = new BinaryWriter(stream);

            int dataBytes = samples.Length * 2;
            w.Write(new[] { 'R', 'I', 'F', 'F' });
            w.Write(36 + dataBytes);
            w.Write(new[] { 'W', 'A', 'V', 'E' });
            w.Write(new[] { 'f', 'm', 't', ' ' });
            w.Write(16);                       // PCM 청크 크기
            w.Write((short)1);                 // PCM
            w.Write((short)1);                 // 모노
            w.Write(sampleRate);
            w.Write(sampleRate * 2);           // 초당 바이트
            w.Write((short)2);                 // 블록 정렬
            w.Write((short)16);                // 비트 심도
            w.Write(new[] { 'd', 'a', 't', 'a' });
            w.Write(dataBytes);
            foreach (var s in samples)
                w.Write((short)(Mathf.Clamp(s, -1f, 1f) * short.MaxValue));

            w.Flush();
            return stream.ToArray();
        }
    }
}
