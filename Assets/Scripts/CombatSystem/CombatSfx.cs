using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NorthLand.Combat
{
public enum CombatSfxPriority { Low, Normal, High }

/// 전투 위치 기반 효과음의 공용 진입점. 호출자는 풀과 AudioSource를 알지 않는다.
public static class CombatSfx
{
    public static CombatSfxHandle Play(
        AudioClip clip,
        Vector3 worldPosition,
        bool loop = false,
        float volumeScale = 1f,
        CombatSfxPriority priority = CombatSfxPriority.Normal)
    {
        return clip == null
            ? default
            : CombatSfxPool.Instance.Play(clip, worldPosition, loop, volumeScale, priority);
    }

    /// 이 위치의 소리가 지금 들리는가(화면 안 + 줌 감쇠 > 0).
    ///
    /// **루프 소비처를 위한 것이다.** 단발음은 들리지 않아도 곧 끝나므로 이 판정이 필요 없고,
    /// 그냥 재생해 두면 풀이 알아서 회수한다. 문제는 루프다 — 화면 밖 루프는 `LastGain`이 계속
    /// 0이라 **탈취 1순위**인데(`IsBetterStealCandidate`), 뺏기면 소비처가 다음 프레임에 곧바로
    /// 다시 잡는다. 풀이 포화면 그 재획득이 매 프레임 반복되면서 **화면 안에서 실제로 들리고
    /// 있던 소리를 60fps로 잘라 낸다.** 증상은 "전투가 격해지면 소리가 지직거린다"로만 보이고
    /// 원인이 화면 밖 타워라는 단서가 없다.
    ///
    /// 그래서 루프 소비처는 새로 잡기 **전에** 이것을 확인한다. 화면에 들어오는 순간 다음 프레임에
    /// 자연히 켜지므로 청감 손실이 없고, 화면 밖 루프가 보이스를 붙들지 않는 부수 효과가 따라온다.
    public static bool IsAudible(Vector3 worldPosition)
        => CombatSfxAudibility.TryEvaluate(worldPosition, out _, out _);
}

/// 슬롯 세대가 일치할 때만 동작하므로 반환된 슬롯이 재사용돼도 새 소리를 잘못 끄지 않는다.
public readonly struct CombatSfxHandle
{
    private readonly CombatSfxPool pool;
    private readonly int slotIndex;
    private readonly uint generation;

    internal CombatSfxHandle(CombatSfxPool pool, int slotIndex, uint generation)
    {
        this.pool = pool;
        this.slotIndex = slotIndex;
        this.generation = generation;
    }

    public bool IsPlaying => pool != null && pool.IsPlaying(slotIndex, generation);

    public void Stop()
    {
        if (pool != null)
            pool.Stop(slotIndex, generation);
    }
}

/// AudioSource 보이스 풀. 한 매니저가 활성 보이스의 감쇠·설정 볼륨·반환을 갱신한다.
sealed class CombatSfxPool : MonoBehaviour
{
    private const int InitialVoiceCount = 16;
    private const int MaxVoiceCount = 32;

    private sealed class Voice
    {
        public GameObject GameObject;
        public AudioSource Source;
        public Vector3 Position;
        public float VolumeScale;
        public float LastGain;
        public CombatSfxPriority Priority;
        public ulong StartedOrder;
        public uint Generation;
        public bool Active;

        /// 풀이 일시정지 때문에 멈춰 둔 상태(#540). `AudioSource.Pause()`는 `isPlaying`을 false로
        /// 만들기 때문에, 이 플래그가 없으면 `Update`의 자연 종료 판정이 정지된 루프를 회수해 버린다.
        public bool Paused;
    }

    private static CombatSfxPool instance;
    private readonly List<Voice> voices = new List<Voice>(MaxVoiceCount);
    private ulong nextStartedOrder;

    public static CombatSfxPool Instance
    {
        get
        {
            if (instance != null)
                return instance;

            var root = new GameObject(nameof(CombatSfxPool));
            instance = root.AddComponent<CombatSfxPool>();
            DontDestroyOnLoad(root);
            return instance;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => instance = null;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);

        for (int i = 0; i < InitialVoiceCount; i++)
            CreateVoice();

        SceneManager.activeSceneChanged += HandleActiveSceneChanged;
    }

    private void OnDestroy()
    {
        SceneManager.activeSceneChanged -= HandleActiveSceneChanged;
        if (instance == this)
            instance = null;
    }

    public CombatSfxHandle Play(
        AudioClip clip,
        Vector3 position,
        bool loop,
        float volumeScale,
        CombatSfxPriority priority)
    {
        int index = AcquireVoice(priority);
        if (index < 0)
            return default;

        Voice voice = voices[index];
        PrepareForReuse(voice);

        voice.Position = position;
        voice.VolumeScale = Mathf.Max(0f, volumeScale);
        voice.Priority = priority;
        voice.StartedOrder = ++nextStartedOrder;
        voice.Generation++;
        if (voice.Generation == 0)
            voice.Generation = 1;
        voice.Active = true;

        voice.GameObject.transform.position = position;
        voice.GameObject.SetActive(true);
        voice.Source.clip = clip;
        voice.Source.loop = loop;
        ApplyVolume(voice);
        voice.Source.Play();

        return new CombatSfxHandle(this, index, voice.Generation);
    }

    /// ⚠ **일시정지로 멈춘 루프도 "재생 중"으로 답한다**(#540). 소비처는 이 값으로 "내 루프가 아직
    /// 살아 있는가"를 묻는데, 정지 중에 false를 주면 `BeamAction.UpdateLoopSfx`처럼 매 프레임 도는
    /// 쪽이 루프가 죽은 줄 알고 **정지 화면에서 계속 새 보이스를 잡는다.** 정지를 고치려다 다른
    /// churn을 만드는 자리다.
    public bool IsPlaying(int index, uint generation)
    {
        return TryGetCurrentVoice(index, generation, out Voice voice)
            && (voice.Source.isPlaying || voice.Paused);
    }

    public void Stop(int index, uint generation)
    {
        if (TryGetCurrentVoice(index, generation, out Voice voice))
            Release(voice);
    }

    /// ⚠ **일시정지는 루프에만 건다**(#540). `Time.timeScale = 0`은 `AudioSource`에 영향을 주지 않고
    /// 이 `Update`도 계속 돌기 때문에, 정지를 명시적으로 걸지 않으면 **설정 패널·보상 선택창·결과창
    /// 밑에서 빔과 전기장 소리가 계속 흐른다.** 단발음(0.2~1초)은 정지 중 스스로 소진되는 편이
    /// 자연스러워 대상에서 뺀다 — 정지 순간 울리던 발사음이 뚝 끊기면 그쪽이 더 어색하다.
    private void Update()
    {
        bool paused = GameSpeedController.Instance != null && GameSpeedController.Instance.IsPaused;

        for (int i = 0; i < voices.Count; i++)
        {
            Voice voice = voices[i];
            if (!voice.Active)
                continue;

            if (voice.Source.loop)
            {
                if (paused && !voice.Paused)
                {
                    voice.Source.Pause();
                    voice.Paused = true;
                }
                else if (!paused && voice.Paused)
                {
                    voice.Source.UnPause();
                    voice.Paused = false;
                }
            }

            // ⚠ **자연 종료 판정보다 먼저 걸러야 한다.** `Pause()`가 `isPlaying`을 false로 만들기
            // 때문에 이 가드가 없으면 정지된 루프가 곧바로 회수되고, 정지를 풀어도 돌아오지 않는다.
            if (voice.Paused)
            {
                ApplyVolume(voice);   // 정지 중에도 설정 슬라이더는 따라간다
                continue;
            }

            if (!voice.Source.isPlaying)
            {
                Release(voice);
                continue;
            }

            ApplyVolume(voice);
        }
    }

    private int AcquireVoice(CombatSfxPriority requestedPriority)
    {
        for (int i = 0; i < voices.Count; i++)
            if (!voices[i].Active)
                return i;

        if (voices.Count < MaxVoiceCount)
        {
            CreateVoice();
            return voices.Count - 1;
        }

        return FindVoiceToSteal(requestedPriority);
    }

    private int FindVoiceToSteal(CombatSfxPriority requestedPriority)
    {
        int bestIndex = -1;
        for (int i = 0; i < voices.Count; i++)
        {
            Voice candidate = voices[i];
            if (candidate.Priority > requestedPriority)
                continue;

            if (bestIndex < 0 || IsBetterStealCandidate(candidate, voices[bestIndex]))
                bestIndex = i;
        }

        return bestIndex;
    }

    private static bool IsBetterStealCandidate(Voice candidate, Voice current)
    {
        bool candidateSilent = candidate.LastGain <= 0f;
        bool currentSilent = current.LastGain <= 0f;

        if (candidateSilent != currentSilent)
            return candidateSilent;
        if (candidate.Priority != current.Priority)
            return candidate.Priority < current.Priority;
        return candidate.StartedOrder < current.StartedOrder;
    }

    private void CreateVoice()
    {
        var go = new GameObject($"Voice {voices.Count}");
        go.transform.SetParent(transform, false);

        AudioSource source = go.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 0f;
        source.volume = 0f;

        go.SetActive(false);
        voices.Add(new Voice { GameObject = go, Source = source });
    }

    private static void PrepareForReuse(Voice voice)
    {
        voice.Source.Stop();
        voice.Source.clip = null;
        voice.Source.loop = false;
        voice.Source.volume = 0f;
        voice.Source.panStereo = 0f;
        voice.LastGain = 0f;
        voice.Paused = false;   // 정지 상태로 회수된 슬롯이 다음 소리를 멈춘 채로 시작하지 않게
        voice.Active = false;
    }

    private static void Release(Voice voice)
    {
        PrepareForReuse(voice);
        voice.GameObject.SetActive(false);
    }

    private static void ApplyVolume(Voice voice)
    {
        bool audible = CombatSfxAudibility.TryEvaluate(voice.Position, out float gain, out float pan);
        float settingsVolume = AudioManager.Instance != null
            ? AudioManager.Instance.GetEffectiveVolume(AudioChannel.Sfx)
            : 1f;

        voice.LastGain = audible ? gain : 0f;
        voice.Source.volume = audible ? gain * voice.VolumeScale * settingsVolume : 0f;
        voice.Source.panStereo = audible ? pan : 0f;
    }

    private bool TryGetCurrentVoice(int index, uint generation, out Voice voice)
    {
        voice = null;
        if (index < 0 || index >= voices.Count)
            return false;

        Voice candidate = voices[index];
        if (!candidate.Active || candidate.Generation != generation)
            return false;

        voice = candidate;
        return true;
    }

    private void StopAll()
    {
        for (int i = 0; i < voices.Count; i++)
            if (voices[i].Active)
                Release(voices[i]);
    }

    private void HandleActiveSceneChanged(Scene _, Scene __)
    {
        StopAll();
        CombatSfxAudibility.ClearCamera();
    }
}

/// 전투 효과음의 카메라 줌·화면 위치 감쇠 계산기.
static class CombatSfxAudibility
{
    private const float FullVolumeOrthoSize = 80f;
    private const float SilentOrthoSize = 160f;

    private static Camera cachedCamera;
    private static int stampedFrame = -1;
    private static float zoomGain;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => ClearCamera();

    public static void ClearCamera()
    {
        cachedCamera = null;
        stampedFrame = -1;
        zoomGain = 0f;
    }

    public static bool TryEvaluate(Vector3 worldPosition, out float gain, out float pan)
    {
        gain = 0f;
        pan = 0f;

        if (!EnsureFrameState() || zoomGain <= 0f)
            return false;

        Vector3 viewport = cachedCamera.WorldToViewportPoint(worldPosition);
        if (viewport.z <= 0f)
            return false;

        float dx = viewport.x - 0.5f;
        float dy = viewport.y - 0.5f;
        float edgeDistance = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) / 0.5f;
        if (edgeDistance >= 1f)
            return false;

        float screenGain = Mathf.SmoothStep(0f, 1f, 1f - edgeDistance);
        gain = zoomGain * screenGain;
        pan = Mathf.Clamp(dx * 2f, -1f, 1f);
        return gain > 0f;
    }

    private static bool EnsureFrameState()
    {
        if (stampedFrame == Time.frameCount)
            return cachedCamera != null;

        stampedFrame = Time.frameCount;

        if (cachedCamera == null)
            cachedCamera = Camera.main;
        if (cachedCamera == null)
            return false;

        if (!cachedCamera.orthographic)
        {
            zoomGain = 1f;
            return true;
        }

        zoomGain = Mathf.SmoothStep(
            0f,
            1f,
            Mathf.InverseLerp(SilentOrthoSize, FullVolumeOrthoSize, cachedCamera.orthographicSize));
        return true;
    }
}
}
