using System.Collections.Generic;
using UnityEngine;

// 튜토리얼 진행을 소유한다. '지금 몇 단계인지'를 아는 유일한 곳.
// 팝업·말풍선을 어떻게 그리는지는 모른다(TutorialOverlay의 몫).
// 무엇을 기다리는지도 모른다(TutorialCondition의 몫) — "됐다"는 통지만 받는다.
public class TutorialController : MonoBehaviour
{
    [SerializeField]
    private TutorialOverlay overlay;

    // 진행 순서 = 이 리스트의 등록 순서. 인덱스 0이 첫 단계다.
    [SerializeField]
    private List<TutorialStepAsset> steps = new List<TutorialStepAsset>();

    // 자동 실행 시점 결정은 후속 이슈다. 지금은 테스트용 스위치.
    [SerializeField]
    private bool startOnPlay = true;

    // 뒤쪽 단계를 고칠 때마다 앞 단계를 전부 통과하지 않아도 되게 하는 스위치.
    // 순서를 확인하려는 것이 아니라 '이 단계 하나가 도는가'를 보려는 용도다.
    [Tooltip("[에디터 테스트용] 켜면 Steps 대신 Debug Steps만 진행한다. 빌드 전에 끌 것.")]
    [SerializeField]
    private bool debugMode;

    [Tooltip("[에디터 테스트용] Debug Mode일 때 진행할 단계. Steps와 같은 규칙으로 등록 순서대로 돈다.")]
    [SerializeField]
    private List<TutorialStepAsset> debugSteps = new List<TutorialStepAsset>();

    private enum Phase
    {
        Idle,    // 돌고 있지 않다
        Popup,   // 팝업이 떠 있고 확인을 기다린다
        Action   // 말풍선이 떠 있고 조건이 충족되길 기다린다
    }

    private Phase _phase = Phase.Idle;
    private int _index = -1;

    private TutorialContext _context;
    private TutorialCondition _active;

    // 이번 실행이 진행할 단계 목록. StartTutorial에서 한 번 확정하고 이후 바뀌지 않는다 —
    // 진행 도중 debugMode를 뒤집으면 _index가 다른 리스트를 가리켜 엉뚱한 단계로 뛴다
    // (MonsterSpawnWaveProvider.isTutorialRun과 같은 규칙).
    private List<TutorialStepAsset> _activeSteps;

    // 지금 이 컨트롤러가 게임을 멈춰 뒀는가. 해제를 빠뜨리면 게임이 영구 정지하므로
    // '내가 걸었는지'를 직접 들고 있다가 모든 이탈 경로에서 되돌린다.
    private bool _pausedByStep;

    public bool IsRunning => _phase != Phase.Idle;

    private void Awake()
    {
        _context = new TutorialContext();

        // 오버레이 없이는 팝업도 말풍선도 띄울 수 없다 — 배선 누락을 raw NRE 대신 여기서 알린다.
        if (overlay == null)
        {
            Debug.LogError($"[{nameof(TutorialController)}] Overlay가 연결되지 않았습니다.",this);

            enabled = false;
        }
    }

    private void OnEnable()
    {
        if (overlay == null)
        {
            return;
        }

        overlay.PopupConfirmed += OnPopupConfirmed;
    }

    private void OnDisable()
    {
        if (overlay != null)
        {
            overlay.PopupConfirmed -= OnPopupConfirmed;
        }

        // 감시를 남긴 채 꺼지면 죽은 구독이 된다. 다만 다시 켜도 이어서 진행되지는 않는다.
        EndActiveCondition();

        // 멈춘 채로 꺼지면 게임이 영구 정지한다.
        ResumeGameIfPaused();
    }

    private void Start()
    {
        // startOnPlay는 에디터 테스트용 임시 스위치다. 실제 진입은 TutorialMode가 결정한다.
        if (startOnPlay || TutorialMode.IsActive)
        {
            StartTutorial();
        }
    }

    public void StartTutorial()
    {
        _activeSteps = debugMode ? debugSteps : steps;

        // 단계가 통째로 안 보이는 것을 '버그'로 오해하기 쉬운 자리라 켜져 있으면 반드시 알린다.
        if (debugMode)
        {
            Debug.LogWarning(
                $"[{nameof(TutorialController)}] 디버그 모드 — Steps 대신 Debug Steps {_activeSteps.Count}개만 진행한다.",
                this);
        }

        _index = -1;
        Advance();
    }

    public void StopTutorial()
    {
        EndActiveCondition();
        ResumeGameIfPaused();
        _phase = Phase.Idle;
        _index = -1;
        overlay.HideAll();
    }

    // 다음 단계로 넘어간다. 리스트의 빈 슬롯(null)은 건너뛴다.
    private void Advance()
    {
        _index++;

        while (_index < _activeSteps.Count && _activeSteps[_index] == null)
        {
            _index++;
        }

        if (_index >= _activeSteps.Count)
        {
            Debug.Log("[Tutorial] 모든 단계를 마쳤다.");
            StopTutorial();
            return;
        }

        EnterStep(_activeSteps[_index]);
    }

    private void EnterStep(TutorialStepAsset step)
    {
        if (step.HasPopup)
        {
            _phase = Phase.Popup;
            overlay.HideBubble();

            // 팝업 구간은 팝업 자체가 전체화면 입력을 막는다 — 딤이 겹칠 이유가 없다.
            overlay.HideDim();
            ApplyPause(step);
            overlay.ShowPopup(step.PopupTitle, step.PopupBody, step.PopupImage);
            return;
        }

        // 팝업이 없는 단계는 곧바로 행동 단계로 간다.
        BeginAction(step);
    }

    private void OnPopupConfirmed()
    {
        // 행동 단계에서 들어온 통지는 무시한다 — 그때 팝업은 떠 있지 않다.
        if (_phase != Phase.Popup)
        {
            return;
        }

        overlay.HidePopup();
        BeginAction(_activeSteps[_index]);
    }

    private void BeginAction(TutorialStepAsset step)
    {
        TutorialCondition condition = step.Completion;

        if (condition == null)
        {
            // 기다릴 조건이 없는 단계 — 설명만 하고 지나간다.
            Advance();
            return;
        }

        _phase = Phase.Action;
        _active = condition;

        // 팝업에서 이미 걸어 뒀으면 그대로 유지된다(PauseGame은 두 번 불러도 안전하다).
        // 팝업이 없는 단계는 여기가 첫 진입점이다.
        ApplyPause(step);

        // 말풍선을 Begin보다 먼저 띄운다 — 조건이 Begin 도중에 충족되면 그 자리에서 다음 단계까지
        // 진입한 뒤 여기로 돌아오므로, 뒤에 두면 지나간 단계의 말풍선이 새 단계 위에 켜진다.
        // 먼저 띄워 두면 그 경로의 HideBubble이 정상적으로 걷어 간다.
        if (step.HasBubble)
        {
            overlay.ShowBubble(step.BubbleText);
        }

        ApplyHighlight(step);

        // 구독을 Begin보다 먼저 건다 — 조건이 Begin 도중에 충족될 수도 있다.
        _active.Satisfied += OnConditionSatisfied;
        _active.Begin(_context);
    }

    // 이 단계에서 어디만 클릭 가능하게 둘지 오버레이에 알린다.
    // 지목에 실패하면 딤을 띄우지 않는다 — 아무것도 못 누르는 상태로 가두는 것이 최악이다.
    private void ApplyHighlight(TutorialStepAsset step)
    {
        if (step.HighlightMode == TutorialHighlightMode.UiAnchor)
        {
            if (TutorialAnchor.TryGet(step.HighlightAnchorId, out RectTransform rect))
            {
                overlay.ShowDimForUi(rect);

                return;
            }

            Debug.LogWarning(
                $"[{nameof(TutorialController)}] 앵커 '{step.HighlightAnchorId}'를 찾지 못해 강조를 건너뛴다.",
                this);
        }
        else if (step.HighlightMode == TutorialHighlightMode.GridCell)
        {
            CombatSpace.CombatMapTileSpawner spawner = _context.TileSpawner;

            if (spawner != null
                && spawner.TryGetTileView(step.HighlightCell, out CombatSpace.CombatMapTileView tileView)
                && tileView != null
                && tileView.gameObject.activeInHierarchy)
            {
                var renderer = tileView.GetComponent<Renderer>();

                if (renderer != null)
                {
                    overlay.ShowDimForWorld(renderer.bounds);

                    return;
                }

                // Renderer가 없으면 셀 크기로 박스를 만든다.
                float size = spawner.TileSize;

                overlay.ShowDimForWorld(new Bounds(
                    spawner.GridToWorldPosition(step.HighlightCell),
                    new Vector3(size, size, size)));

                return;
            }

            // 타일이 꺼져 있는 것은 정상 상태다 — 웨이브 공개 연출(CombatMapRevealController)이 껐을 수 있다.
            Debug.LogWarning(
                $"[{nameof(TutorialController)}] 타일 {step.HighlightCell}을 강조할 수 없어 건너뛴다.",
                this);
        }

        overlay.HideDim();
    }

    private void OnConditionSatisfied()
    {
        if (_phase != Phase.Action)
        {
            return;
        }

        EndActiveCondition();
        ResumeGameIfPaused();
        overlay.HideBubble();
        overlay.HideDim();
        Advance();
    }

    private void ApplyPause(TutorialStepAsset step)
    {
        if (step.PauseGameDuringStep)
        {
            PauseGame();
        }
        else
        {
            ResumeGameIfPaused();
        }
    }

    private void PauseGame()
    {
        if (_pausedByStep)
        {
            return;
        }

        var speed = GameSpeedController.Instance;

        if (speed == null)
        {
            Debug.LogWarning($"[{nameof(TutorialController)}] GameSpeedController가 없어 이 단계에서 게임을 멈출 수 없다.", this);

            return;
        }

        speed.SetPaused(GamePauseReason.Tutorial, true);
        _pausedByStep = true;
    }

    // 내가 걸어 둔 정지만 되돌린다. 두 번 불러도 안전하다.
    private void ResumeGameIfPaused()
    {
        if (!_pausedByStep)
        {
            return;
        }

        _pausedByStep = false;

        // 컨트롤러가 파괴되는 순서에 따라 Instance가 먼저 사라질 수 있다.
        GameSpeedController.Instance?.SetPaused(GamePauseReason.Tutorial, false);
    }

    // 지금 걸려 있는 감시를 푼다. 두 번 불러도 안전하다.
    private void EndActiveCondition()
    {
        if (_active == null)
        {
            return;
        }

        _active.Satisfied -= OnConditionSatisfied;
        _active.End();
        _active = null;
    }
}