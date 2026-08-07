using UnityEngine;

/// <summary>
/// 마을 가로등을 밤에만 켠다.
///
/// 가로등 메시(5_obj05_1.0_0_0)는 FBX에서 하나로 병합돼 있어 램프별 GameObject가 없다.
/// 그래서 전구 위치에 자식 Light를 따로 세우고(에디터에서 생성), 이 컴포넌트가 그것들을
/// 페이즈에 맞춰 일괄로 켜고 끈다.
///
/// 성능 메모:
/// - 그림자는 전부 끈다. Mobile_RPAsset은 애초에 추가 광원 그림자를 지원하지 않고(false),
///   PC에서도 31개 광원이 각자 그림자 맵을 잡으면 감당이 안 된다.
/// - range를 좁게 유지하는 게 핵심이다. Mobile_Renderer는 Forward(오브젝트당 추가 광원 4개
///   한계)라, 램프 간 최소 간격(10.3)보다 사거리가 크게 넘어가면 한 오브젝트에 4개를 넘겨
///   일부가 조용히 무시된다. PC_Renderer는 Forward+라 이 한계가 없다.
/// - 낮에는 Light 컴포넌트 자체를 끈다(컬링 목록에서 빠진다).
/// </summary>
public class StreetLampController : MonoBehaviour
{
    [SerializeField]
    [Tooltip("가로등 라이트들. 비어 있으면 자식에서 자동 수집한다.")]
    private Light[] lamps;

    [Header("밤 라이트 설정 (Apply 시 전체 램프에 적용)")]
    [SerializeField] private Color lampColor = new Color(1f, 0.78f, 0.48f);
    [SerializeField] private float intensity = 3f;

    // 램프 간 최소 간격이 10.3이므로 이 값을 크게 올리면 모바일(Forward)에서
    // 오브젝트당 4개 한계에 걸리기 시작한다.
    [SerializeField] private float range = 12f;

    [Header("전환 연출")]
    // 전환 블렌드가 이 지점을 넘어서면 등이 켜지기 시작한다. 0.15면 해가 조금 기울었을 때
    // 불이 들어오는 인상이 된다. 1이면 전환이 끝나는 순간에만 켜진다.
    [SerializeField]
    [Range(0f, 1f)]
    private float turnOnAt = 0.15f;

    // DayNightTransition이 SetBlend로 구동하는 씬에서는 끈다. 켜 두면 페이즈 이벤트에 직접
    // 반응해 즉시 켜지므로, 전환 도중에 이미 최대 밝기로 들어와 연출과 어긋난다.
    [SerializeField]
    [Tooltip("끄면 페이즈 이벤트를 직접 구독하지 않는다. DayNightTransition이 SetBlend로 구동하는 씬에서 끈다.")]
    private bool subscribeToPhaseEvents = true;

    private void Awake()
    {
        if (lamps == null || lamps.Length == 0)
        {
            lamps = GetComponentsInChildren<Light>(true);
        }

        ApplySettings();
    }

    private void Start()
    {
        if (DayNightManager.Instance == null)
        {
            Debug.LogError("DayNightManager 없음", this);
            SetLampsEnabled(false);
            return;
        }

        // 세이브 복원으로 밤에서 시작할 수 있으므로 현재 페이즈를 보고 맞춘다.
        // 이 초기 동기화는 전환 구동 여부와 무관하게 필요하다.
        SetBlend(DayNightManager.Instance.CurrentPhase == DayNightManager.Phase.Night ? 1f : 0f);

        if (!subscribeToPhaseEvents) return;

        DayNightManager.Instance.OnDayToNight += HandleDayToNight;
        DayNightManager.Instance.OnNightToDay += HandleNightToDay;
    }

    /// <summary>
    /// 낮(0)과 밤(1) 사이의 임의 지점을 적용한다. 전환 연출(DayNightTransition)이 매 프레임 부른다.
    /// turnOnAt 이전에는 꺼진 채로 두고, 이후 구간에서 밝기를 끌어올린다.
    /// </summary>
    public void SetBlend(float t)
    {
        if (lamps == null) return;

        float k = turnOnAt >= 1f
            ? (t >= 1f ? 1f : 0f)
            : Mathf.Clamp01((t - turnOnAt) / (1f - turnOnAt));

        bool on = k > 0f;

        for (int i = 0; i < lamps.Length; i++)
        {
            Light lamp = lamps[i];

            if (lamp == null) continue;

            lamp.enabled = on;
            lamp.intensity = intensity * k;
        }
    }

    private void OnDestroy()
    {
        if (DayNightManager.Instance == null) return;

        DayNightManager.Instance.OnDayToNight -= HandleDayToNight;
        DayNightManager.Instance.OnNightToDay -= HandleNightToDay;
    }

    private void HandleDayToNight() => SetBlend(1f);
    private void HandleNightToDay() => SetBlend(0f);

    private void SetLampsEnabled(bool on)
    {
        if (lamps == null) return;

        for (int i = 0; i < lamps.Length; i++)
        {
            if (lamps[i] != null) lamps[i].enabled = on;
        }
    }

    /// <summary>
    /// 색·강도·사거리를 전체 램프에 밀어 넣는다. 램프가 31개라 하나씩 고르면 튜닝이 불가능해서
    /// 이 컴포넌트를 단일 조정 지점으로 둔다.
    /// </summary>
    [ContextMenu("Apply Settings To Lamps")]
    private void ApplySettings()
    {
        if (lamps == null) return;

        for (int i = 0; i < lamps.Length; i++)
        {
            Light lamp = lamps[i];

            if (lamp == null) continue;

            lamp.type = LightType.Point;
            lamp.color = lampColor;
            lamp.intensity = intensity;
            lamp.range = range;
            lamp.shadows = LightShadows.None;
        }
    }
}
