using TMPro;
using UnityEngine;

// 말풍선을 문장 길이에 맞춘다. 최대 너비 전에는 좌우로 커지고,
// 최대 너비에 닿은 뒤에는 자동/수동 줄바꿈 수만큼 위아래로 커진다.
// 텍스트 안전 영역은 말풍선 이미지 모양에 맞춘 Text RectTransform의 Anchor가 정본이다.
public class TutorialBubbleLayout : MonoBehaviour
{
    [SerializeField]
    private RectTransform bubble;

    [SerializeField]
    private TMP_Text text;

    [SerializeField]
    private Vector2 minimumSize = new Vector2(300f, 80f);

    [SerializeField]
    private Vector2 maximumSize = new Vector2(750f, 300f);

    public void Rebuild()
    {
        if (bubble == null || text == null)
        {
            return;
        }

        RectTransform textRect = text.rectTransform;
        Vector2 anchorSpan = textRect.anchorMax - textRect.anchorMin;

        // 0폭 Anchor는 말풍선 크기를 바꿔도 텍스트 영역이 늘지 않아 역산할 수 없다.
        if (anchorSpan.x <= Mathf.Epsilon || anchorSpan.y <= Mathf.Epsilon)
        {
            Debug.LogError(
                $"[{nameof(TutorialBubbleLayout)}] BubbleText Anchor 영역은 가로·세로 Stretch여야 합니다.",
                this);
            return;
        }

        // 줄바꿈하지 않았을 때의 너비로 먼저 가로 크기를 정한다. 명시적인 개행은 TMP가
        // 가장 긴 줄의 너비만 반환하므로 그대로 유지된다.
        float naturalTextWidth = text.GetPreferredValues(
            text.text,
            Mathf.Infinity,
            Mathf.Infinity).x;

        // 실제 텍스트 폭 = Bubble 폭 × Anchor span + sizeDelta다. 에셋의 꼬리·장식을 피해
        // 잡은 비율 안전 영역을 그대로 유지하면서 필요한 Bubble 폭을 역산한다.
        float requiredBubbleWidth = (naturalTextWidth - textRect.sizeDelta.x) / anchorSpan.x;
        float bubbleWidth = Mathf.Clamp(
            requiredBubbleWidth,
            minimumSize.x,
            maximumSize.x);

        // 확정된 가로 폭으로 다시 계산해야 최대 너비 이후의 자동 줄바꿈 높이가 나온다.
        float textWidth = Mathf.Max(0f, bubbleWidth * anchorSpan.x + textRect.sizeDelta.x);
        float preferredTextHeight = text.GetPreferredValues(
            text.text,
            textWidth,
            Mathf.Infinity).y;

        float requiredBubbleHeight = (preferredTextHeight - textRect.sizeDelta.y) / anchorSpan.y;
        float bubbleHeight = Mathf.Clamp(
            requiredBubbleHeight,
            minimumSize.y,
            maximumSize.y);

        bubble.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, bubbleWidth);
        bubble.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, bubbleHeight);
    }

    private void OnValidate()
    {
        minimumSize.x = Mathf.Max(0f, minimumSize.x);
        minimumSize.y = Mathf.Max(0f, minimumSize.y);
        maximumSize.x = Mathf.Max(minimumSize.x, maximumSize.x);
        maximumSize.y = Mathf.Max(minimumSize.y, maximumSize.y);
    }
}
