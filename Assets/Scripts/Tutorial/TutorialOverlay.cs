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
    }

    private void OnConfirmClicked()
    {
        PopupConfirmed?.Invoke();
    }
}