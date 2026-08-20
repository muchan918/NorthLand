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

    public bool IsRunning => _phase != Phase.Idle;

    private void Awake()
    {
        _context = new TutorialContext();
    }

    private void OnEnable()
    {
        overlay.PopupConfirmed += OnPopupConfirmed;
    }

    private void OnDisable()
    {
        overlay.PopupConfirmed -= OnPopupConfirmed;

        // 감시를 남긴 채 꺼지면 죽은 구독이 된다. 다만 다시 켜도 이어서 진행되지는 않는다.
        EndActiveCondition();
    }

    private void Start()
    {
        if (startOnPlay)
        {
            StartTutorial();
        }
    }

    public void StartTutorial()
    {
        _index = -1;
        Advance();
    }

    public void StopTutorial()
    {
        EndActiveCondition();
        _phase = Phase.Idle;
        _index = -1;
        overlay.HideAll();
    }

    // 다음 단계로 넘어간다. 리스트의 빈 슬롯(null)은 건너뛴다.
    private void Advance()
    {
        _index++;

        while (_index < steps.Count && steps[_index] == null)
        {
            _index++;
        }

        if (_index >= steps.Count)
        {
            Debug.Log("[Tutorial] 모든 단계를 마쳤다.");
            StopTutorial();
            return;
        }

        EnterStep(steps[_index]);
    }

    private void EnterStep(TutorialStepAsset step)
    {
        if (step.HasPopup)
        {
            _phase = Phase.Popup;
            overlay.HideBubble();
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
        BeginAction(steps[_index]);
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

        // 구독을 Begin보다 먼저 건다 — 조건이 Begin 도중에 충족될 수도 있다.
        _active.Satisfied += OnConditionSatisfied;
        _active.Begin(_context);

        if (step.HasBubble)
        {
            overlay.ShowBubble(step.BubbleText);
        }
    }

    private void OnConditionSatisfied()
    {
        if (_phase != Phase.Action)
        {
            return;
        }

        EndActiveCondition();
        overlay.HideBubble();
        Advance();
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