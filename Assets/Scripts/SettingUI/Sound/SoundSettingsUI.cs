using UnityEngine;
using UnityEngine.UI;

public class SoundSettingsUI : MonoBehaviour
{
    [SerializeField] private Slider masterVolumeSlider;
    [SerializeField] private Slider bgmVolumeSlider;
    [SerializeField] private Slider sfxVolumeSlider;

    private AudioManager audioManager;
    private bool isBound;

    private void Awake()
    {
        ConfigureSlider(masterVolumeSlider);
        ConfigureSlider(bgmVolumeSlider);
        ConfigureSlider(sfxVolumeSlider);
    }

    private void OnEnable()
    {
        Bind();
    }

    private void Start()
    {
        // OnEnable 시점에 AudioManager 부팅이 끝나지 않은 특수한 경우 한 번 더 시도한다.
        Bind();
    }

    private void OnDisable()
    {
        Unbind();
    }

    private void Bind()
    {
        if (isBound)
            return;

        if (masterVolumeSlider == null ||
            bgmVolumeSlider == null ||
            sfxVolumeSlider == null)
        {
            Debug.LogWarning(
                "[SoundSettingsUI] Master/BGM/SFX Slider 참조가 모두 필요합니다.",
                this);
            return;
        }

        audioManager = AudioManager.Instance;
        if (audioManager == null)
        {
            Debug.LogWarning(
                "[SoundSettingsUI] AudioManager를 찾지 못했습니다.",
                this);
            return;
        }

        masterVolumeSlider.onValueChanged.AddListener(OnMasterVolumeChanged);
        bgmVolumeSlider.onValueChanged.AddListener(OnBgmVolumeChanged);
        sfxVolumeSlider.onValueChanged.AddListener(OnSfxVolumeChanged);
        audioManager.OnAudioSettingsChanged += RefreshSliders;

        isBound = true;
        RefreshSliders();
    }

    private void Unbind()
    {
        if (!isBound)
            return;

        masterVolumeSlider.onValueChanged.RemoveListener(OnMasterVolumeChanged);
        bgmVolumeSlider.onValueChanged.RemoveListener(OnBgmVolumeChanged);
        sfxVolumeSlider.onValueChanged.RemoveListener(OnSfxVolumeChanged);

        if (audioManager != null)
            audioManager.OnAudioSettingsChanged -= RefreshSliders;

        audioManager = null;
        isBound = false;
    }

    private void RefreshSliders()
    {
        if (audioManager == null)
            return;

        masterVolumeSlider.SetValueWithoutNotify(audioManager.GetVolume(AudioChannel.Master));
        bgmVolumeSlider.SetValueWithoutNotify(audioManager.GetVolume(AudioChannel.Bgm));
        sfxVolumeSlider.SetValueWithoutNotify(audioManager.GetVolume(AudioChannel.Sfx));
    }

    private void OnMasterVolumeChanged(float value)
    {
        audioManager?.SetVolume(AudioChannel.Master, value);
    }

    private void OnBgmVolumeChanged(float value)
    {
        audioManager?.SetVolume(AudioChannel.Bgm, value);
    }

    private void OnSfxVolumeChanged(float value)
    {
        audioManager?.SetVolume(AudioChannel.Sfx, value);
    }

    private static void ConfigureSlider(Slider slider)
    {
        if (slider == null)
            return;

        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.wholeNumbers = false;
    }
}
