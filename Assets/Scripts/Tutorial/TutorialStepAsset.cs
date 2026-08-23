using UnityEngine;
using UnityEngine.Serialization;

// 튜토리얼 단계가 무엇을 강조할지 고르는 방식.
// UI는 씬에 미리 존재하므로 이름표(TutorialAnchor)로 지목하고,
// 전투 타일은 CombatMapTileSpawner가 런타임에 생성하므로 그리드 좌표로 지목한다.
public enum TutorialHighlightMode
{
    None,      // 딤 없음
    UiAnchor,  // TutorialAnchor의 id로 지목
    GridCell   // 전투 그리드 좌표로 지목
}

// 튜토리얼 한 단계의 안내 내용.
// 이 에셋은 "몇 번째 단계인가"를 스스로 갖지 않는다 — 진행 순서는 전적으로
// TutorialController.steps 리스트의 등록 순서가 결정한다(MonsterWaveAsset과 같은 규칙).
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

    [Header("강조 — None이면 딤을 띄우지 않는다")]
    [SerializeField]
    private TutorialHighlightMode highlightMode;

    // UiAnchor 모드에서만 쓴다. 씬(또는 프리팹)의 TutorialAnchor.id와 맞아야 한다.
    [SerializeField]
    private string highlightAnchorId;

    // GridCell 모드에서만 쓴다. CombatMapTileSpawner의 그리드 좌표다.
    [SerializeField]
    private Vector2Int highlightCell;

    [Header("이 단계 동안 게임을 멈춘다 — 팝업이 뜨는 순간부터 조건 충족까지")]
    // Time.timeScale = 0으로 멈추므로 게임 로직 전체가 선다. 다만 MouseManager는 계속 돌아
    // 조준·클릭은 그대로 된다 — '멈춘 적을 여유롭게 맞혀 보는' 단계를 만들 수 있는 이유다.
    // 팝업 구간부터 거는 이유: 안내를 읽는 동안 적이 성을 때리고 있으면 안 된다.
    [FormerlySerializedAs("pauseGameDuringAction")]
    [SerializeField]
    private bool pauseGameDuringStep;

    [Header("전투 규칙 — 이 단계 동안만 적용된다")]
    // 켜면 이 단계 동안 타워를 무료로 놓는다(TowerPlacer.FreePlacement). 자원이 모자라 단계가
    // 막히는 것을 피하려는 용도이고, 단계를 벗어나면 컨트롤러가 되돌린다.
    [Tooltip("켜면 이 단계 동안 타워 배치 비용을 받지 않는다.")]
    [SerializeField]
    private bool freeTowerPlacement;

    // 지정하면 타워 패널에서 그 타워만 고를 수 있다(TowerSelectPanelView.RestrictTo).
    // "아처 3개를 지으세요" 같은 단계에서 다른 타워로 새는 것을 막는다.
    [Tooltip("지정하면 타워 패널에서 이 타워만 고를 수 있다. 비우면 제한 없음.")]
    [SerializeField]
    private TowerAsset restrictTowerPanelTo;

    [Header("경영 규칙 — 이 단계 동안만 적용된다")]
    // 켜면 이 단계 동안 건물 업그레이드가 자원을 소모하지 않는다(ManagementController.FreeUpgrade).
    // 타워 무료 배치와 같은 취지 — 자원이 모자라 단계가 막히는 것을 피한다.
    [Tooltip("켜면 이 단계 동안 건물 업그레이드에 자원을 받지 않는다.")]
    [SerializeField]
    private bool freeBuildingUpgrade;

    // 무료로 열어 두면 한 건물만 계속 올릴 수 있다 — 이 상한이 그것을 막는다.
    // ⚠ 완료 조건이 요구하는 레벨보다 낮게 잡으면 그 단계를 영영 끝낼 수 없다.
    [Tooltip("이 단계 동안 올릴 수 있는 최대 건물 레벨. 0이면 제한 없음. 완료 조건이 요구하는 레벨과 맞출 것.")]
    [Min(0)]
    [SerializeField]
    private int upgradeCap;

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

    public TutorialHighlightMode HighlightMode => highlightMode;

    public string HighlightAnchorId => highlightAnchorId;

    public Vector2Int HighlightCell => highlightCell;

    public bool PauseGameDuringStep => pauseGameDuringStep;

    public bool FreeTowerPlacement => freeTowerPlacement;

    public TowerAsset RestrictTowerPanelTo => restrictTowerPanelTo;

    public bool FreeBuildingUpgrade => freeBuildingUpgrade;

    public int UpgradeCap => upgradeCap;

    public TutorialCondition Completion => completion;
}
