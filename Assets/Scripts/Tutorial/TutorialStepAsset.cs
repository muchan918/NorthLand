using UnityEngine;

// 튜토리얼 한 단계의 안내 내용.
// 이 에셋은 "몇 번째 단계인가"를 스스로 갖지 않는다 — 진행 순서는 전적으로
// TutorialSequenceAsset.steps 리스트의 등록 순서가 결정한다(MonsterWaveAsset과 같은 규칙).
[CreateAssetMenu(fileName = "TutorialStep", menuName = "Tutorial/Step")]
public class TutorialStepAsset : ScriptableObject
{
    [Header("팝업 — 셋 다 비우면 팝업을 건너뛴다")]
    [SerializeField]
    private string popupTitle;

    [TextArea(3, 6)]
    [SerializeField]
    private string popupBody;

    // 비워두면 팝업에서 그림 영역이 숨겨진다.
    [SerializeField]
    private Sprite popupImage;

    [Header("말풍선 — 비우면 말풍선을 띄우지 않는다")]
    [TextArea(2, 4)]
    [SerializeField]
    private string bubbleText;

    [Header("완료 조건 — 비우면 팝업 확인만으로 넘어간다")]
    [SerializeReference]
    private TutorialCondition completion;

    public string PopupTitle => popupTitle;

    public string PopupBody => popupBody;

    public string BubbleText => bubbleText;

    public bool HasBubble => !string.IsNullOrWhiteSpace(bubbleText);

    public Sprite PopupImage => popupImage;

    public bool HasPopup =>
        !string.IsNullOrWhiteSpace(popupTitle)
        || !string.IsNullOrWhiteSpace(popupBody)
        || popupImage != null;

    public TutorialCondition Completion => completion;
}