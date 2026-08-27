using TMPro;
using UnityEngine;

// 말풍선을 문장 길이에 맞춘다. 최대 너비 전에는 좌우로 커지고,
// 최대 너비에 닿은 뒤에는 자동/수동 줄바꿈 수만큼 위아래로 커진다.
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

    [Tooltip("x는 좌우 각각의 여백, y는 상하 각각의 여백이다.")]
    [SerializeField]
    private Vector2 padding = new Vector2(40f, 24f);

    public void Rebuild()
    {
        if (bubble == null || text == null)
        {
            return;
        }

        float horizontalPadding = padding.x * 2f;
        float verticalPadding = padding.y * 2f;
        float maximumTextWidth = Mathf.Max(0f, maximumSize.x - horizontalPadding);

        // 줄바꿈하지 않았을 때의 너비로 먼저 가로 크기를 정한다. 명시적인 개행은 TMP가
        // 가장 긴 줄의 너비만 반환하므로 그대로 유지된다.
        float naturalTextWidth = text.GetPreferredValues(
            text.text,
            Mathf.Infinity,
            Mathf.Infinity).x;

        float bubbleWidth = Mathf.Clamp(
            naturalTextWidth + horizontalPadding,
            minimumSize.x,
            maximumSize.x);

        // 확정된 가로 폭으로 다시 계산해야 최대 너비 이후의 자동 줄바꿈 높이가 나온다.
        float textWidth = Mathf.Min(maximumTextWidth, Mathf.Max(0f, bubbleWidth - horizontalPadding));
        float preferredTextHeight = text.GetPreferredValues(
            text.text,
            textWidth,
            Mathf.Infinity).y;

        float bubbleHeight = Mathf.Clamp(
            preferredTextHeight + verticalPadding,
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
        padding.x = Mathf.Max(0f, padding.x);
        padding.y = Mathf.Max(0f, padding.y);
    }
}
