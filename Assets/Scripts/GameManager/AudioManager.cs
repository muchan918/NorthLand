using NorthLand.Core;
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
/// 게임 전체 사운드의 볼륨을 소유하고 BGM 재생·전환과 2D 원샷 효과음을 담당한다. (#361)
///
/// AudioMixer를 쓰지 않는다 — 채널이 3개뿐이고 더킹·스냅샷 요구가 없어
/// 믹서 에셋 + 그룹 배선 + dB 변환 비용이 이득보다 크다고 판단했다.
/// 대신 이 매니저가 볼륨 값을 소유하고 **자기가 소유한** AudioSource.volume에 곱해 넣는다.
/// 따라서 이 매니저를 거치지 않는 재생은 호출부가 실효 볼륨을 직접 반영해야 한다 —
/// 전투 위치 효과음은 <c>CombatSfxPool</c>이 매 프레임 SFX 실효 볼륨을 곱한다.
///
/// "지금 어떤 곡을 틀지"는 여기서 정하지 않는다. 이 오브젝트는 DontDestroyOnLoad라
/// 인스펙터 배선을 가질 수 없으므로, 클립 선택과 페이즈 구독은 씬 쪽 <see cref="BgmCue"/>가 맡는다.
/// </summary>
public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

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

    // 2D 원샷 전용. 풀이 아니라 소스 1개 + PlayOneShot이라 동시재생 상한이 없다(§PlaySfx 주석).
    private AudioSource sfxSource;

    // 겹치면 안 되는 원샷 전용 소스(§PlaySfxExclusive). PlayOneShot이 아니라 clip + Play()라 Stop()으로
    // 끊을 수 있고, 볼륨을 매 프레임 다시 곱할 수 있다(ResidentVoice가 같은 이유로 같은 방식을 쓴다).
    private AudioSource sfxExclusiveSource;

    // 위 소스가 지금 재생 중인 소리의 재생 배율. 매 프레임 실효 볼륨과 다시 곱하려면 기억해야 한다.
    private float exclusiveScale = 1f;

    private float fadeDuration;
    private float fadeProgress = 1f;

    // 스왑 시점에 나가는 소스가 실제로 갖고 있던 가중치. 페이드가 끝난 뒤의 교체라면 1이다.
    // 이걸 기억하지 않으면 페이드 도중 트랙을 다시 요청했을 때 나가는 쪽이 최대 볼륨으로 튄다.
    private float outgoingWeight = 1f;

    /// <summary>
    /// 볼륨·음소거가 바뀔 때마다 발생한다. 설정 패널(#346)의 슬라이더가 코드 쪽 변경을 따라오는 용도.
    /// </summary>
    public event Action OnAudioSettingsChanged;

    private AudioSource ActiveSource => activeIsA ? bgmA : bgmB;

    private AudioSource InactiveSource => activeIsA ? bgmB : bgmA;

    // 씬에 배치하지 않는다 — 두 씬 모두에서 필요하고, 씬에 두면 씬 파일 병합 충돌만 늘어난다.
    // GameSceneManager와 동일한 부팅 패턴.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
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

        LoadSettings();
        CreateSources();

        // 이 시점엔 보통 구독자가 없다(설정 패널은 열 때 GetVolume으로 현재 값을 읽는다).
        // 계약상 "값이 확정되면 알린다"를 로드에도 동일하게 적용해둔다.
        OnAudioSettingsChanged?.Invoke();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {

            Instance = null;
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
        ApplyExclusiveSfxVolume();
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
    /// 채널 볼륨을 0~1로 설정한다.
    /// 음소거 상태는 변경하지 않는다.
    /// </summary>
    public void SetVolume(AudioChannel channel, float value)
    {
        float clamped = Mathf.Clamp01(value);

        if (Mathf.Approximately(volumes[(int)channel], clamped))
            return;

        volumes[(int)channel] = clamped;

        ApplyBgmVolume();
        ApplyExclusiveSfxVolume();

        GameSettingsService settingsService = GameSettingsService.Instance;

        if (settingsService != null)
        {
            settingsService.SetAudioVolume(volumes[(int)AudioChannel.Master],volumes[(int)AudioChannel.Bgm],volumes[(int)AudioChannel.Sfx]);
        }

        OnAudioSettingsChanged?.Invoke();
    }

    /// <summary>
    /// 채널 음소거를 설정한다.
    /// 기존 볼륨 값은 보존한다.
    /// </summary>
    public void SetMuted(AudioChannel channel, bool muted)
    {
        if (mutes[(int)channel] == muted)
            return;

        mutes[(int)channel] = muted;

        ApplyBgmVolume();
        ApplyExclusiveSfxVolume();

        GameSettingsService settingsService = GameSettingsService.Instance;

        if (settingsService != null)
        {
            settingsService.SetAudioMuted(mutes[(int)AudioChannel.Master],mutes[(int)AudioChannel.Bgm],mutes[(int)AudioChannel.Sfx]);
        }

        OnAudioSettingsChanged?.Invoke();
    }

    /// <summary>
    /// 실제로 AudioSource.volume에 곱할 계수. 음소거면 0, 아니면 Master × 채널.
    /// BGM과 SFX 재생 경로에서 실제 출력 볼륨을 계산할 때 사용한다.
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

    // ── SFX ────────────────────────────────────────────────

    /// <summary>
    /// 2D 원샷 효과음을 재생한다. 위치도 감쇠도 없는 화면 전역 소리(페이즈 전환 스팅어·UI 등)용이다.
    ///
    /// ⚠️ <c>PlayOneShot</c>은 **호출 시점의 볼륨을 굽는다** — 재생 중 슬라이더를 움직여도 이미 울리고
    /// 있는 소리에는 반영되지 않는다. 1~2초짜리 짧은 소리에만 쓰는 전제이며, 길게 끄는 소리나
    /// 위치가 필요한 3D 효과음은 이 API로 처리하지 않는다(SFX 풀 도입 시 재검토).
    /// </summary>
    public void PlaySfx(AudioClip clip, float volumeScale = 1f)
    {
        if (clip == null || sfxSource == null)
        {
            return;
        }

        float volume = GetEffectiveVolume(AudioChannel.Sfx) * Mathf.Max(volumeScale, 0f);

        // 음소거·볼륨 0이면 재생 자체를 생략한다(들리지 않는 소리에 보이스를 쓰지 않는다).
        if (volume <= 0f)
        {
            return;
        }

        sfxSource.PlayOneShot(clip, volume);
    }

    /// <summary>
    /// 겹치지 않는 2D 효과음을 재생한다. **이미 이 경로로 울리고 있는 소리는 끊기고 처음부터 다시 난다.**
    ///
    /// <see cref="PlaySfx"/>가 못 하는 일이라 따로 났다 — 그쪽은 <c>PlayOneShot</c>이라 한 번 쏜 소리를
    /// 지목해 멈출 수단이 없고 동시재생 상한도 없다. 길거나(수 초) 연타 가능한 버튼에 물린 소리는
    /// 그대로 두면 여러 벌이 겹쳐 쌓인다(주민 증가음이 9.5초라 실제로 그렇게 된다).
    ///
    /// 부수 효과로 <b>재생 중 슬라이더 조작이 즉시 반영된다</b> — 볼륨을 굽지 않고 <see cref="Update"/>에서
    /// 매 프레임 다시 곱하기 때문이다(<c>ResidentVoice</c>와 같은 성질).
    ///
    /// ⚠️ **소스가 하나뿐이라 이 경로의 소리들끼리도 서로를 끊는다.** 지금 소비처가 하나라 충분하지만,
    /// 둘 이상이 동시에 울려야 하는 상황이 생기면 소리별 소스로 갈라야 한다.
    ///
    /// <param name="startTime">
    /// 클립의 몇 초 지점부터 재생할지. 기본값 0은 처음부터 재생하는 기존 동작이다.
    /// 앞에 워밍업이 붙어 있어 <b>타격 순간이 클립 중간에 있는</b> 효과음을 화면 연출에 맞출 때 쓴다
    /// (결과창 승리 스팅어가 그렇다 — 실제 타격이 0.6초 지점이라 그대로 틀면 로고가 착지하고
    /// 한참 뒤에 소리가 난다). 클립 길이를 넘으면 재생되지 않으므로 안쪽으로 물린다.
    /// </param>
    /// </summary>
    public void PlaySfxExclusive(AudioClip clip, float volumeScale = 1f, float startTime = 0f)
    {
        if (clip == null || sfxExclusiveSource == null)
        {
            return;
        }

        exclusiveScale = Mathf.Max(volumeScale, 0f);

        // 음소거·볼륨 0이면 재생을 생략한다(PlaySfx와 같은 방침). 이미 울리고 있던 소리는 끊는다 —
        // "새 요청이 오면 직전 것을 대체한다"가 이 경로의 계약이고, 들리지 않는 요청도 요청이다.
        sfxExclusiveSource.Stop();

        if (GetEffectiveVolume(AudioChannel.Sfx) * exclusiveScale <= 0f)
        {
            return;
        }

        sfxExclusiveSource.clip = clip;
        sfxExclusiveSource.volume = GetEffectiveVolume(AudioChannel.Sfx) * exclusiveScale;

        // 시작 지점은 Play() **전에** 넣는다. 재생을 걸어 두고 나중에 time을 밀면 앞부분이
        // 한 프레임 새어 나온다 — 워밍업을 건너뛰려고 쓰는 기능이라 그 한 프레임이 곧 실패다.
        // 길이 이상을 넣으면 Unity가 재생을 거르므로 마지막 샘플 앞으로 물린다.
        sfxExclusiveSource.time = Mathf.Clamp(startTime, 0f, Mathf.Max(0f, clip.length - 0.01f));

        sfxExclusiveSource.Play();
    }

    /// <summary>
    /// <see cref="PlaySfxExclusive"/>로 울리고 있는 소리를 즉시 멈춘다.
    ///
    /// **씬 전환에 반드시 필요하다.** 이 매니저는 <c>DontDestroyOnLoad</c>라 씬을 넘어 살아남고,
    /// 이 경로는 <c>PlayOneShot</c>이 아니라 <c>clip</c> + <c>Play()</c>라 **소스가 살아 있는 한 계속
    /// 울린다.** 주민 증가 팡파레가 9.5초짜리라, 재생 중 타이틀로 돌아가면 BGM은 페이드아웃되는데
    /// 팡파레만 타이틀 화면에서 끝까지 이어진다 — WL-180이 BGM에서 낸 사고와 형태가 같다.
    ///
    /// BGM과 같은 방식으로 닫는다: 정지 주체를 **씬 쪽 큐**(<see cref="SoundCue"/>)에 둔다.
    /// 타이틀 복귀 경로가 넷이라 어느 하나를 고치는 것으로는 닫히지 않기 때문이다.
    /// </summary>
    public void StopSfxExclusive()
    {
        if (sfxExclusiveSource == null)
        {
            return;
        }

        sfxExclusiveSource.Stop();

        // 클립 참조를 남기지 않는다 — 다음 씬까지 들고 있을 이유가 없다(BGM StopBgm과 같은 처리).
        sfxExclusiveSource.clip = null;
    }

    // 재생 중에도 설정 볼륨을 따라오게 한다. 재생 중이 아니면 건드릴 것이 없다.
    private void ApplyExclusiveSfxVolume()
    {
        if (sfxExclusiveSource == null || !sfxExclusiveSource.isPlaying)
        {
            return;
        }

        sfxExclusiveSource.volume = GetEffectiveVolume(AudioChannel.Sfx) * exclusiveScale;
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
        // 지금 페이드인 중이던 소스가 그대로 페이드아웃 대상이 된다. 그 시점의 가중치를 붙잡아두지
        // 않으면 fadeProgress가 0으로 리셋되면서 (1 - 0) = 1, 즉 최대 볼륨으로 점프한 뒤 내려온다.
        outgoingWeight = fadeProgress;

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
        InactiveSource.volume = outgoingWeight * (1f - fadeProgress) * bgm;

        if (fadeProgress >= 1f && InactiveSource.isPlaying)
        {
            InactiveSource.Stop();
            InactiveSource.clip = null;
        }
    }

    private void CreateSources()
    {
        bgmA = CreateSource(loop: true);
        bgmB = CreateSource(loop: true);

        // 원샷은 PlayOneShot이 볼륨을 인자로 받으므로 소스 자체는 1.0으로 둔다.
        sfxSource = CreateSource(loop: false);
        sfxSource.volume = 1f;

        // 이쪽은 PlayOneShot이 아니라 소스의 volume을 직접 쓰므로 0으로 두고 재생 시점에 채운다.
        sfxExclusiveSource = CreateSource(loop: false);
    }

    private AudioSource CreateSource(bool loop)
    {
        var source = gameObject.AddComponent<AudioSource>();

        source.playOnAwake = false;
        source.loop = loop;
        source.spatialBlend = 0f;   // 2D — 씬 카메라의 AudioListener 위치와 무관해진다
        source.volume = 0f;

        return source;
    }

    private void LoadSettings()
    {
        GameSettingsService settingsService = GameSettingsService.Instance;

        if (settingsService == null || settingsService.CurrentSettings == null)
        {
            volumes[(int)AudioChannel.Master] = DefaultMasterVolume;
            volumes[(int)AudioChannel.Bgm] = DefaultBgmVolume;
            volumes[(int)AudioChannel.Sfx] = DefaultSfxVolume;

            mutes[(int)AudioChannel.Master] = false;
            mutes[(int)AudioChannel.Bgm] = false;
            mutes[(int)AudioChannel.Sfx] = false;
            return;
        }

        GameSettingsData settings = settingsService.CurrentSettings;

        volumes[(int)AudioChannel.Master] = Mathf.Clamp01(settings.masterVolume);

        volumes[(int)AudioChannel.Bgm] = Mathf.Clamp01(settings.bgmVolume);

        volumes[(int)AudioChannel.Sfx] = Mathf.Clamp01(settings.sfxVolume);

        mutes[(int)AudioChannel.Master] = settings.masterMuted;
        mutes[(int)AudioChannel.Bgm] = settings.bgmMuted;
        mutes[(int)AudioChannel.Sfx] = settings.sfxMuted;
    }
}
