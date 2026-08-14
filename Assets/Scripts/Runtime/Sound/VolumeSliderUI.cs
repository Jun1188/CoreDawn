using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Slider))]
public class VolumeSliderUI : MonoBehaviour
{
    public enum VolumeType { Master, BGM, SFX }

    [SerializeField] private VolumeType volumeType;
    [SerializeField] private Slider slider;

    private void Start()
    {
        if (slider == null) slider = GetComponent<Slider>();

        // 1. JSON 데이터 불러오기
        AudioSettingsData settings = AudioSaveSystem.LoadSettings();

        // 2. 해당되는 볼륨 타입의 값으로 슬라이더 초기화
        float initialValue = volumeType switch
        {
            VolumeType.Master => settings.masterVolume,
            VolumeType.BGM => settings.bgmVolume,
            VolumeType.SFX => settings.sfxVolume,
            _ => 0.8f
        };

        slider.value = initialValue;
        ApplyVolume(initialValue);

        // 3. 슬라이더 조절 이벤트 연동
        slider.onValueChanged.AddListener(OnValueChanged);
    }

    private void OnValueChanged(float value)
    {
        ApplyVolume(value);

        // 변경된 값 JSON에 즉시 업데이트 후 저장
        AudioSettingsData settings = AudioSaveSystem.LoadSettings();
        switch (volumeType)
        {
            case VolumeType.Master: settings.masterVolume = value; break;
            case VolumeType.BGM: settings.bgmVolume = value; break;
            case VolumeType.SFX: settings.sfxVolume = value; break;
        }
        AudioSaveSystem.SaveSettings(settings);
    }

    private void ApplyVolume(float value)
    {
        switch (volumeType)
        {
            case VolumeType.Master: SoundManager.Instance.SetMasterVolume(value); break;
            case VolumeType.BGM: SoundManager.Instance.SetBGMVolume(value); break;
            case VolumeType.SFX: SoundManager.Instance.SetSFXVolume(value); break;
        }
    }
}