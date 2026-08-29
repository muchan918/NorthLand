using UnityEngine;
using System.Collections.Generic;
using UnityEngine.InputSystem;
using NorthLand.Core;

/// 게임을 멈춘 **이유**. 이유마다 한 칸씩 잡고 모두 풀려야 시간이 다시 흐른다 — 리워드 패널과
/// 설정 창이 겹쳤을 때 먼저 닫힌 쪽이 나머지의 정지를 풀어버리는 사고를 막는다.
public enum GamePauseReason
{
    Reward,
    Settings,
    ResultDecided,
    Cutscene,
    Tutorial
}

/// 배속과 일시정지의 단일 소유자. 전역 `Time.timeScale`을 여기서만 쓴다.
///
/// **배속은 1배 ↔ 2배 토글 하나다(#537).** 그 전에는 1/2/4배 버튼 3개였고 각 버튼이 씬의
/// UnityEvent로 서로 다른 메서드를 불렀는데, 그러면 "지금 켜진 배속"이 버튼 색과 `CurrentSpeed`
/// 두 곳에 생겨 어긋날 수 있었다. 지금은 버튼도 스페이스바도 <see cref="ToggleSpeed"/> **하나**를
/// 부르고, 표시는 그 결과를 따라간다 — 표시와 실제가 갈릴 자리를 없앤 것이 이 구조의 목적이다
/// (`UndoRequest.Submit`이 되돌리기 버튼과 Ctrl+Z를 한 진입점으로 묶은 것과 같은 이유).
///
/// 🗑 4배속은 함께 폐기했다. 보스 BT의 예고·준비 모션이 전부 스케일드 타임이라 4배에서 예고
/// `Duration`(0.5초)이 0.125초로 뭉개져 인지 자체가 불가능했다(WL-119).
public class GameSpeedController : MonoBehaviour
{
    private const float NormalSpeed = 1f;
    private const float BoostSpeed = 2f;

    [SerializeField]
    [Tooltip("배속이 켜져 있는 동안 버튼 테두리를 도는 연출. 비워도 배속 자체는 동작한다.")]
    private SpeedBoostEffect boostEffect;

    [SerializeField]
    private CanvasGroup speedControls;

    [SerializeField]
    [Range(0f, 1f)]
    private float pausedControlsAlpha = 0.8f;

    public float CurrentSpeed { get; private set; } = NormalSpeed;
    public bool IsPaused => pauseReasons.Count > 0;

    /// 지금 2배속인가. 일시정지 중에도 **꺼지지 않는다** — 정지는 배속 위에 겹쳐지는 상태이지
    /// 배속을 1배로 되돌리는 것이 아니다(풀리면 2배로 돌아와야 한다).
    public bool IsBoosted => Mathf.Approximately(CurrentSpeed, BoostSpeed);

    public static GameSpeedController Instance { get; private set; }

    private readonly HashSet<GamePauseReason> pauseReasons = new();

    private bool controlsLocked;
    private bool interactableBeforePause;
    private float alphaBeforePause;

    private GameManager gameManager;

    private void Awake()
    {
        if (speedControls == null)
        {
            speedControls = GetComponent<CanvasGroup>();
        }

        if (speedControls == null)
        {
            Debug.LogError("[GameSpeedController] CanvasGroup이 연결되지 않았습니다.", this);
        }

        if (Instance != null && Instance != this)
        {
            Debug.LogError("[GameSpeedController] 씬에 컨트롤러가 두 개 존재합니다.", this);

            // ⚠ `enabled = false`까지 해야 한다. Awake에서 돌아가기만 하면 **OnEnable은 그대로 불려**
            //    중복 인스턴스가 스페이스바를 한 번 더 등록하고, 그러면 한 번 눌러 두 번 토글돼
            //    "단축키가 안 먹는다"로 보인다. Awake 안에서 끄면 OnEnable 자체가 호출되지 않는다.
            enabled = false;
            return;
        }

        Instance = this;

        SetSpeed(NormalSpeed);
    }

    // 단축키 등록/해제는 OnEnable/OnDisable 대칭으로 둔다 — 바인딩 목록이 static이라
    // 인스턴스 메서드를 넣어 놓고 걷지 않으면 파괴된 오브젝트를 붙든 채 남는다(WL-199).
    // 씬을 다시 로드하면 죽은 컨트롤러와 새 컨트롤러가 함께 발화해 토글이 두 번 돈다.
    private void OnEnable()
    {
        // exactModifiers: false — Shift는 이 게임에서 그룹 선택으로 **쥔 채** 조작하는 키다.
        // 정확 일치로 등록하면 유닛을 여러 개 잡는 중에만 배속 단축키가 안 먹는, 원인을 짐작하기
        // 어려운 증상이 된다(WL-201).
        KeyboardManager.Bind(Key.Space, KeyModifier.None, ToggleSpeed, "배속 토글", exactModifiers: false);
    }

    private void OnDisable()
    {
        KeyboardManager.Unbind(Key.Space, KeyModifier.None, ToggleSpeed);
    }

    private void Start()
    {
        gameManager = GameManager.Instance;

        if (gameManager == null)
        {
            Debug.LogError("[GameSpeedController] GameManager를 찾지 못해 게임 종료 시 시간을 정지할 수 없습니다.", this);
            return;
        }

        gameManager.OnResultDecided += HandleResultDecided;
    }

    /// 1배 ↔ 2배. 배속 버튼의 `OnClick`과 스페이스바가 **공유하는 단 하나의 진입점**이다.
    public void ToggleSpeed()
    {
        // 일시정지 중에는 무시한다. 정지 중 배속 조작은 버튼 쪽이 이미 CanvasGroup으로 막혀 있어서
        // (UpdateControlsLock) 여기서 열어 두면 **단축키만 통하는** 상태가 되고, 그러면 리워드·설정
        // 창을 닫는 순간 아무도 누른 기억이 없는 배속으로 바뀌어 있다.
        if (IsPaused)
        {
            return;
        }

        SetSpeed(IsBoosted ? NormalSpeed : BoostSpeed);
    }

    public void SetPaused(GamePauseReason reason, bool paused)
    {
        bool changed;

        if (paused)
        {
            changed = pauseReasons.Add(reason);
        }
        else
        {
            changed = pauseReasons.Remove(reason);
        }

        if (changed)
        {
            ApplyTimeScale();
        }
    }

    private void SetSpeed(float speed)
    {
        CurrentSpeed = speed;

        ApplyTimeScale();
        UpdateBoostEffect();
    }

    private void ApplyTimeScale()
    {
        Time.timeScale = IsPaused ? 0f : CurrentSpeed;
        UpdateControlsLock(IsPaused);
    }

    /// 연출은 **배속 상태만** 따른다(일시정지는 보지 않는다). 정지 중에도 2배속이라는 사실은
    /// 그대로이고, 연출이 unscaled라 멈추지도 않는다 — `SpeedBoostEffect` 주석 참고.
    private void UpdateBoostEffect()
    {
        if (boostEffect == null)
        {
            return;
        }

        if (IsBoosted)
        {
            boostEffect.Play();
        }
        else
        {
            boostEffect.Stop();
        }
    }

    private void UpdateControlsLock(bool paused)
    {
        if (speedControls == null)
        {
            return;
        }

        if (paused && !controlsLocked)
        {
            interactableBeforePause = speedControls.interactable;

            alphaBeforePause = speedControls.alpha;

            speedControls.interactable = false;
            speedControls.alpha = pausedControlsAlpha;
            controlsLocked = true;
        }
        else if (!paused && controlsLocked)
        {
            speedControls.interactable = interactableBeforePause;

            speedControls.alpha = alphaBeforePause;

            controlsLocked = false;
        }
    }

    private void HandleResultDecided(GameResult result)
    {
        if (result == GameResult.Playing)
        {
            return;
        }

        SetPaused(GamePauseReason.ResultDecided, true);
    }

    private void OnDestroy()
    {
        if (gameManager != null)
        {
            gameManager.OnResultDecided -= HandleResultDecided;
        }

        if (Instance != this)
        {
            return;
        }

        Instance = null;
        Time.timeScale = NormalSpeed;
    }
}
