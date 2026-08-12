using System;
using UnityEngine;

/// <summary>
/// 볼륨 채널. 설정 패널의 슬라이더 하나당 하나씩 대응한다.
/// </summary>
public enum AudioChannel
{
    Master,
    Bgm,
    Sfx
}

/// <summary>
/// 게임 전체 사운드의 볼륨을 소유하고 BGM 재생·전환을 담당한다. (#361)
///
/// AudioMixer를 쓰지 않는다 — 채널이 3개뿐이고 더킹·스냅샷 요구가 없어
/// 믹서 에셋 + 그룹 배선 + dB 변환 비용이 이득보다 크다고 판단했다.
/// 대신 이 매니저가 볼륨 값을 소유하고 **자기가 소유한** AudioSource.volume에 곱해 넣는다.
/// 따라서 이 매니저를 거치지 않는 재생은 볼륨 제어를 받지 못한다 —
/// SFX 재생 경로 통합 전까지 Sfx 채널은 값만 보관·노출되고 실제로 걸리는 소리가 없다.
///
/// "지금 어떤 곡을 틀지"는 여기서 정하지 않는다. 이 오브젝트는 DontDestroyOnLoad라
/// 인스펙터 배선을 가질 수 없으므로, 클립 선택과 페이즈 구독은 씬 쪽 <see cref="BgmCue"/>가 맡는다.
/// </summary>
public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    private const string MasterVolumeKey = "MasterVolume";
    private const string BgmVolumeKey = "BgmVolume";
    private const string SfxVolumeKey = "SfxVolume";

    private const string MasterMutedKey = "MasterMuted";
    private const string BgmMutedKey = "BgmMuted";
    private const string SfxMutedKey = "SfxMuted";

    // BGM은 배경이라 낮게 시작한다. 밸런싱 축이므로 청감 확인 후 조정 가능.
    private const float DefaultMasterVolume = 1f;
    private const float DefaultBgmVolume = 0.5f;
    private const float DefaultSfxVolume = 0.8f;

    private const float DefaultFadeSeconds = 1f;

    private const int ChannelCount = 3;

    private readonly float[] volumes = new float[ChannelCount];
    private readonly bool[] mutes = new bool[ChannelCount];

    // BGM 소스 2개를 번갈아 쓰며 크로스페이드한다.
    private AudioSource bgmA;
    private AudioSource bgmB;
    private bool activeIsA = true;

    private float fadeDuration;
    private float fadeProgress = 1f;

    // PlayerPrefs.SetFloat은 메모리 캐시라 슬라이더 드래그마다 불러도 싸지만
    // Save()는 디스크 쓰기다. flush는 종료·포커스 상실 시점으로 미룬다.
    private bool prefsDirty;

    /// <summary>
    /// 볼륨·음소거가 바뀔 때마다 발생한다. 설정 패널(#346)의 슬라이더가 코드 쪽 변경을 따라오는 용도.
    /// </summary>
    public event Action OnAudioSettingsChanged;

    private AudioSource ActiveSource => activeIsA ? bgmA : bgmB;

    private AudioSource InactiveSource => activeIsA ? bgmB : bgmA;

    // 씬에 배치하지 않는다 — 두 씬 모두에서 필요하고, 씬에 두면 씬 파일 병합 충돌만 늘어난다.
    // GameSceneManager와 동일한 부팅 패턴.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (Instance != null)
        {
            return;
        }

        var go = new GameObject(nameof(AudioManager));

        go.AddComponent<AudioManager>();

        DontDestroyOnLoad(go);
    }

    // 혹시 씬에 수동 배치되는 등으로 중복 생성될 경우를 대비한 방어 코드.
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        LoadPrefs();
        CreateBgmSources();

        // 이 시점엔 보통 구독자가 없다(설정 패널은 열 때 GetVolume으로 현재 값을 읽는다).
        // 계약상 "값이 확정되면 알린다"를 로드에도 동일하게 적용해둔다.
        OnAudioSettingsChanged?.Invoke();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            FlushPrefs();

            Instance = null;
        }
    }

    private void OnApplicationQuit()
    {
        FlushPrefs();
    }

    private void OnApplicationPause(bool paused)
    {
        if (paused)
        {
            FlushPrefs();
        }
    }

    private void Update()
    {
        if (fadeProgress < 1f)
        {
            // GameSpeedController가 일시정지에서 timeScale을 0으로 만들고,
            // 설정 패널을 여는 것 자체가 정지다 — scaled를 쓰면 페이드가 얼어붙는다.
            fadeProgress = fadeDuration > 0f
                ? Mathf.Min(fadeProgress + Time.unscaledDeltaTime / fadeDuration, 1f)
                : 1f;
        }

        ApplyBgmVolume();
    }

    // ── 볼륨 ────────────────────────────────────────────────

    public float GetVolume(AudioChannel channel)
    {
        return volumes[(int)channel];
    }

    public bool IsMuted(AudioChannel channel)
    {
        return mutes[(int)channel];
    }

    /// <summary>
    /// 채널 볼륨을 0~1로 설정한다. 음소거 상태는 건드리지 않는다 —
    /// "음소거 중 슬라이더를 움직이면 자동 해제" 같은 UX 정책은 설정 패널(#346) 몫이다.
    /// </summary>
    public void SetVolume(AudioChannel channel, float value01)
    {
        float clamped = Mathf.Clamp01(value01);

        if (Mathf.Approximately(volumes[(int)channel], clamped))
        {
            return;
        }

        volumes[(int)channel] = clamped;

        PlayerPrefs.SetFloat(VolumeKey(channel), clamped);
        prefsDirty = true;

        ApplyBgmVolume();

        OnAudioSettingsChanged?.Invoke();
    }

    /// <summary>
    /// 음소거를 켜고 끈다. 볼륨 값은 보존되므로 해제하면 원래 슬라이더 위치로 돌아온다.
    /// </summary>
    public void SetMuted(AudioChannel channel, bool muted)
    {
        if (mutes[(int)channel] == muted)
        {
            return;
        }

        mutes[(int)channel] = muted;

        PlayerPrefs.SetInt(MutedKey(channel), muted ? 1 : 0);
        prefsDirty = true;

        ApplyBgmVolume();

        OnAudioSettingsChanged?.Invoke();
    }

    /// <summary>
    /// 실제로 AudioSource.volume에 곱할 계수. 음소거면 0, 아니면 Master × 채널.
    /// 지금은 BGM만 쓰고, SFX 재생 경로가 생기면 그쪽이 소비한다.
    /// </summary>
    public float GetEffectiveVolume(AudioChannel channel)
    {
        if (mutes[(int)AudioChannel.Master])
        {
            return 0f;
        }

        float master = volumes[(int)AudioChannel.Master];

        if (channel == AudioChannel.Master)
        {
            return master;
        }

        if (mutes[(int)channel])
        {
            return 0f;
        }

        return master * volumes[(int)channel];
    }

    // ── BGM ────────────────────────────────────────────────

    /// <summary>
    /// BGM을 교체한다. 이미 같은 클립을 틀고 있으면 무시하고 이어 재생한다 —
    /// 씬 재로드나 페이즈 전환에서 트랙이 같으면 끊기지 않는다.
    /// </summary>
    public void PlayBgm(AudioClip clip, float fadeSeconds = DefaultFadeSeconds)
    {
        // 클립 미배선은 조용히 무시한다. BGM 에셋 확보 전까지 BgmCue 필드가 비어 있어도 깨지지 않는다.
        if (clip == null)
        {
            return;
        }

        if (ActiveSource.clip == clip && ActiveSource.isPlaying)
        {
            return;
        }

        SwapSources();

        ActiveSource.clip = clip;
        ActiveSource.time = 0f;
        ActiveSource.Play();

        StartFade(fadeSeconds);
    }

    /// <summary>
    /// 현재 BGM을 페이드아웃하고 멈춘다.
    /// </summary>
    public void StopBgm(float fadeSeconds = DefaultFadeSeconds)
    {
        if (!ActiveSource.isPlaying && !InactiveSource.isPlaying)
        {
            return;
        }

        // 빈 소스로 갈아타면 이전 트랙이 그대로 페이드아웃 대상이 된다.
        SwapSources();

        ActiveSource.clip = null;

        StartFade(fadeSeconds);
    }

    private void SwapSources()
    {
        activeIsA = !activeIsA;

        // 페이드 도중 재요청이면 새 active가 옛 트랙을 물고 있다 — 재사용 전에 버린다.
        ActiveSource.Stop();
    }

    private void StartFade(float fadeSeconds)
    {
        fadeDuration = Mathf.Max(fadeSeconds, 0f);
        fadeProgress = fadeDuration > 0f ? 0f : 1f;
    }

    /// <summary>
    /// 페이드 가중치와 실효 볼륨을 분리해 매 프레임 다시 곱한다.
    /// 둘을 합쳐 volume에 직접 밀면 페이드 도중 슬라이더를 움직였을 때 목표 볼륨이 덮어써진다.
    /// </summary>
    private void ApplyBgmVolume()
    {
        if (bgmA == null || bgmB == null)
        {
            return;
        }

        float bgm = GetEffectiveVolume(AudioChannel.Bgm);

        ActiveSource.volume = fadeProgress * bgm;
        InactiveSource.volume = (1f - fadeProgress) * bgm;

        if (fadeProgress >= 1f && InactiveSource.isPlaying)
        {
            InactiveSource.Stop();
            InactiveSource.clip = null;
        }
    }

    private void CreateBgmSources()
    {
        bgmA = CreateBgmSource();
        bgmB = CreateBgmSource();
    }

    private AudioSource CreateBgmSource()
    {
        var source = gameObject.AddComponent<AudioSource>();

        source.playOnAwake = false;
        source.loop = true;
        source.spatialBlend = 0f;   // 2D — 씬 카메라의 AudioListener 위치와 무관해진다
        source.volume = 0f;

        return source;
    }

    // ── 영속화 ────────────────────────────────────────────────

    private void LoadPrefs()
    {
        volumes[(int)AudioChannel.Master] = LoadVolume(MasterVolumeKey, DefaultMasterVolume);
        volumes[(int)AudioChannel.Bgm] = LoadVolume(BgmVolumeKey, DefaultBgmVolume);
        volumes[(int)AudioChannel.Sfx] = LoadVolume(SfxVolumeKey, DefaultSfxVolume);

        mutes[(int)AudioChannel.Master] = PlayerPrefs.GetInt(MasterMutedKey, 0) != 0;
        mutes[(int)AudioChannel.Bgm] = PlayerPrefs.GetInt(BgmMutedKey, 0) != 0;
        mutes[(int)AudioChannel.Sfx] = PlayerPrefs.GetInt(SfxMutedKey, 0) != 0;
    }

    // 손상된 prefs가 1을 넘는 볼륨으로 들어오는 것을 막는다.
    private static float LoadVolume(string key, float defaultValue)
    {
        return Mathf.Clamp01(PlayerPrefs.GetFloat(key, defaultValue));
    }

    private void FlushPrefs()
    {
        if (!prefsDirty)
        {
            return;
        }

        PlayerPrefs.Save();

        prefsDirty = false;
    }

    private static string VolumeKey(AudioChannel channel)
    {
        return channel switch
        {
            AudioChannel.Master => MasterVolumeKey,
            AudioChannel.Bgm => BgmVolumeKey,
            _ => SfxVolumeKey
        };
    }

    private static string MutedKey(AudioChannel channel)
    {
        return channel switch
        {
            AudioChannel.Master => MasterMutedKey,
            AudioChannel.Bgm => BgmMutedKey,
            _ => SfxMutedKey
        };
    }
}
