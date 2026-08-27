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

    public bool IsPlaying(int index, uint generation)
    {
        return TryGetCurrentVoice(index, generation, out Voice voice) && voice.Source.isPlaying;
    }

    public void Stop(int index, uint generation)
    {
        if (TryGetCurrentVoice(index, generation, out Voice voice))
            Release(voice);
    }

    private void Update()
    {
        for (int i = 0; i < voices.Count; i++)
        {
            Voice voice = voices[i];
            if (!voice.Active)
                continue;

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
