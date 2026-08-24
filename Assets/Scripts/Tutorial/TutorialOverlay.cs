using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 튜토리얼 팝업·말풍선의 '표시'만 담당한다.
// 지금이 몇 단계인지, 다음에 무엇이 오는지는 모른다 — 진행은 TutorialController가 소유한다.
// 여기가 아는 것은 "이 내용을 띄워라"와 "확인이 눌렸다"뿐이다.
public class TutorialOverlay : MonoBehaviour
{
    [Header("팝업")]
    [SerializeField]
    private GameObject popupRoot;

    [SerializeField]
    private TMP_Text popupTitle;

    [SerializeField]
    private TMP_Text popupBody;

    [SerializeField]
    private Image popupImage;

    [SerializeField]
    private Button confirmButton;

    [Header("말풍선")]
    [SerializeField]
    private GameObject bubbleRoot;

    [SerializeField]
    private TMP_Text bubbleText;

    [Header("딤 — 강조 대상만 클릭 가능하게 만든다. 비워두면 딤 기능이 꺼진다(선택)")]
    [SerializeField]
    private RaycastHoleTest dim;

    // 렌더링은 하지 않는다. 강조 영역의 위치·크기를 담는 사각형 정의일 뿐이다.
    [SerializeField]
    private RectTransform hole;

    // 계산된 강조 사각형에 더할 여백. 대상에 딱 붙지 않게 인스펙터에서 튜닝한다.
    [SerializeField]
    private Vector2 holePadding = new Vector2(16f, 16f);

    // 팝업의 확인이 눌렸다. 다음에 무엇을 할지는 구독자(컨트롤러)가 정한다.
    public event Action PopupConfirmed;

    private void Awake()
    {
        // 배선 누락을 raw NRE 대신 어느 필드가 비었는지로 알린다 — 이 오버레이는 씬에서 손으로
        // 배선하는 물건이라(Tutorial.md §1.4) 누락이 실제로 일어난다.
        if (!ValidateReferences())
        {
            enabled = false;
            return;
        }

        confirmButton.onClick.AddListener(OnConfirmClicked);

        // 딤은 선택 기능이다 — 배선되지 않았으면 강조 요청을 조용히 무시한다.
        if (HasDim)
        {
            dim.hole = hole;

            // 홀은 부모 중심 기준으로 위치를 계산한다 — 앵커가 다른 값이면 좌표가 어긋난다.
            hole.anchorMin = new Vector2(0.5f, 0.5f);
            hole.anchorMax = new Vector2(0.5f, 0.5f);
            hole.pivot = new Vector2(0.5f, 0.5f);
        }

        HideAll();
    }

    private void OnDestroy()
    {
        // 리스너를 남기면 씬을 다시 로드했을 때 죽은 대상을 호출한다.
        if (confirmButton != null)
        {
            confirmButton.onClick.RemoveListener(OnConfirmClicked);
        }
    }

    // 하나라도 비면 표시가 성립하지 않는다. 비어 있는 것을 전부 알려 준다 —
    // 하나씩 고쳐 가며 여러 번 재생하지 않게.
    private bool ValidateReferences()
    {
        bool ok = true;

        if (popupRoot == null) { LogMissing(nameof(popupRoot)); ok = false; }
        if (popupTitle == null) { LogMissing(nameof(popupTitle)); ok = false; }
        if (popupBody == null) { LogMissing(nameof(popupBody)); ok = false; }
        if (popupImage == null) { LogMissing(nameof(popupImage)); ok = false; }
        if (confirmButton == null) { LogMissing(nameof(confirmButton)); ok = false; }
        if (bubbleRoot == null) { LogMissing(nameof(bubbleRoot)); ok = false; }
        if (bubbleText == null) { LogMissing(nameof(bubbleText)); ok = false; }
        return ok;
    }

    private void LogMissing(string field)
    {
        Debug.LogError($"[{nameof(TutorialOverlay)}] {field}이(가) 연결되지 않았습니다.",this);
    }

    public void ShowPopup(string title, string body, Sprite image)
    {
        popupTitle.text = title;
        popupBody.text = body;

        // 그림이 없는 단계에서 빈 사각형이 남지 않게 영역째 끈다.
        popupImage.sprite = image;
        popupImage.gameObject.SetActive(image != null);

        popupRoot.SetActive(true);
    }

    public void HidePopup()
    {
        popupRoot.SetActive(false);
    }

    public void ShowBubble(string text)
    {
        bubbleText.text = text;
        bubbleRoot.SetActive(true);
    }

    public void HideBubble()
    {
        bubbleRoot.SetActive(false);
    }

    public void HideAll()
    {
        HidePopup();
        HideBubble();
        HideDim();
    }

    // 지금 무엇을 강조하고 있는가. 홀 계산 경로가 UI냐 월드냐로 갈린다.
    private enum DimTarget
    {
        None,
        Ui,
        World
    }

    private DimTarget _dimTarget = DimTarget.None;
    private RectTransform _dimUiTarget;
    private Bounds _dimWorldBounds;

    private bool HasDim => dim != null && hole != null;

    // UI 하나만 클릭 가능하게 두고 나머지를 덮는다.
    public void ShowDimForUi(RectTransform target)
    {
        if (!HasDim || target == null)
        {
            HideDim();

            return;
        }

        _dimTarget = DimTarget.Ui;
        _dimUiTarget = target;

        dim.gameObject.SetActive(true);
        UpdateHole();
    }

    // 월드 오브젝트(전투 타일 등)를 화면에 투영해 그 영역만 클릭 가능하게 둔다.
    public void ShowDimForWorld(Bounds worldBounds)
    {
        if (!HasDim)
        {
            return;
        }

        _dimTarget = DimTarget.World;
        _dimWorldBounds = worldBounds;

        dim.gameObject.SetActive(true);
        UpdateHole();
    }

    public void HideDim()
    {
        _dimTarget = DimTarget.None;
        _dimUiTarget = null;

        if (dim != null)
        {
            dim.gameObject.SetActive(false);
        }
    }

    // 카메라 팬·줌으로 월드 대상의 화면 위치가 매 프레임 바뀐다 — 1회 계산으로는 어긋난다.
    // 강조 대상은 항상 하나뿐이므로 매 프레임 계산해도 비용은 무시할 수 있다.
    private void LateUpdate()
    {
        if (_dimTarget != DimTarget.None)
        {
            UpdateHole();
        }
    }

    private void UpdateHole()
    {
        var parent = hole.parent as RectTransform;

        if (parent == null)
        {
            HideDim();

            return;
        }

        if (_dimTarget == DimTarget.Ui)
        {
            // 대상이 파괴되거나 꺼졌으면 강조를 유지할 근거가 없다.
            if (_dimUiTarget == null)
            {
                HideDim();

                return;
            }

            Bounds local = RectTransformUtility.CalculateRelativeRectTransformBounds(parent, _dimUiTarget);

            ApplyHoleRect(local.center, local.size);

            return;
        }

        Camera camera = Camera.main;

        if (camera == null)
        {
            HideDim();

            return;
        }

        // 직교 카메라가 비스듬히 보고 있으므로 8코너를 전부 투영해 화면 AABB를 잡는다.
        Vector2 screenMin = new Vector2(float.MaxValue, float.MaxValue);
        Vector2 screenMax = new Vector2(float.MinValue, float.MinValue);

        Vector3 center = _dimWorldBounds.center;
        Vector3 extents = _dimWorldBounds.extents;

        for (int i = 0; i < 8; i++)
        {
            var corner = new Vector3(
                center.x + ((i & 1) == 0 ? -extents.x : extents.x),
                center.y + ((i & 2) == 0 ? -extents.y : extents.y),
                center.z + ((i & 4) == 0 ? -extents.z : extents.z));

            Vector3 screenPoint = camera.WorldToScreenPoint(corner);

            screenMin = Vector2.Min(screenMin, screenPoint);
            screenMax = Vector2.Max(screenMax, screenPoint);
        }

        // Screen Space - Overlay 캔버스이므로 카메라 인자는 null이어야 한다.
        RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screenMin, null, out Vector2 localMin);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screenMax, null, out Vector2 localMax);

        ApplyHoleRect(
            (localMin + localMax) * 0.5f,
            new Vector2(Mathf.Abs(localMax.x - localMin.x), Mathf.Abs(localMax.y - localMin.y)));
    }

    private void ApplyHoleRect(Vector2 center, Vector2 size)
    {
        hole.anchoredPosition = center;
        hole.sizeDelta = size + holePadding;
    }

    private void OnConfirmClicked()
    {
        PopupConfirmed?.Invoke();
    }
}