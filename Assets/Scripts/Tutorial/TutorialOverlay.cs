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
        confirmButton.onClick.AddListener(OnConfirmClicked);
        HideAll();
    }

    private void OnDestroy()
    {
        // 리스너를 남기면 씬을 다시 로드했을 때 죽은 대상을 호출한다.
        confirmButton.onClick.RemoveListener(OnConfirmClicked);
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