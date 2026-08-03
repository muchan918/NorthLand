using UnityEngine;
using UnityEngine.UI;

/// 드래그 선택 사각형의 화면 표시(#261). MouseManager가 노출하는 스크린 Rect를 그대로 그린다 —
/// 판정에는 일절 관여하지 않는 순수 뷰다.
///
/// <b>런타임 생성 오버레이</b>(TowerTooltipView 계보). 씬에 배치하지 않는 이유는 둘이다:
/// 1) <b>성능</b> — Canvas는 자식 Graphic이 하나라도 바뀌면 그 Canvas 전체의 메시를 다시 만든다.
///    사각형은 드래그 내내 매 프레임 움직이므로 공용 UICanvas에 얹으면 HUD 전체가 매 프레임
///    리빌드된다. 전용 Canvas로 분리해 리빌드 범위를 이 5개 쿼드 안에 가둔다.
/// 2) <b>씬 병합</b> — GameScene diff가 없어 다른 사람의 씬 작업과 충돌하지 않는다.
///
/// 좌표는 스크린 픽셀을 그대로 쓴다 → <b>CanvasScaler를 붙이지 않는다</b>(붙이면 좌표가 배율만큼 어긋난다).
/// GraphicRaycaster도 붙이지 않고 모든 Image의 raycastTarget을 끈다 — 켜져 있으면 커서가 항상 UI 위로
/// 잡혀 게임의 모든 클릭이 죽는다(명세 §5.5).
[DisallowMultipleComponent]
public class SelectionBoxView : MonoBehaviour
{
    // 색·두께는 전부 임시(아트 TBD). 그룹 선택 초록과 계열을 맞춰 "이 사각형이 잡는 것 = 초록으로 켜지는 것"이
    // 읽히게 한다. 아트가 확정되면 이 세 값만 고치면 된다.
    private static readonly Color k_FillColor = new(0.30f, 1.00f, 0.45f, 0.15f);
    private static readonly Color k_EdgeColor = new(0.40f, 1.00f, 0.55f, 0.90f);
    private const float k_EdgeThickness = 2f;

    private RectTransform _fill;
    private RectTransform _edgeTop;
    private RectTransform _edgeBottom;
    private RectTransform _edgeLeft;
    private RectTransform _edgeRight;

    private bool _visible;
    private Rect _lastRect;

    /// 씬 배치가 없으므로 스스로 부팅한다. 씬을 갈아타도 살아남아(DontDestroyOnLoad) MouseManager와 수명을 맞춘다.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        var go = new GameObject("SelectionBoxView (auto)");
        DontDestroyOnLoad(go);
        go.AddComponent<SelectionBoxView>();
    }

    private void Awake()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = UILayer.SelectionBox; // HUD 아래 — 사각형이 패널을 덮지 않는다

        _fill = CreateQuad("Fill", transform, k_FillColor);
        _edgeBottom = CreateQuad("Edge_Bottom", _fill, k_EdgeColor);
        _edgeTop = CreateQuad("Edge_Top", _fill, k_EdgeColor);
        _edgeLeft = CreateQuad("Edge_Left", _fill, k_EdgeColor);
        _edgeRight = CreateQuad("Edge_Right", _fill, k_EdgeColor);

        SetVisible(false);
    }

    // MouseManager.Update가 이번 프레임 사각형을 확정한 뒤에 읽어야 한 프레임 늦지 않는다.
    private void LateUpdate()
    {
        var mm = MouseManager.Instance;
        bool show = mm != null && mm.IsBoxSelecting;

        if (show != _visible)
        {
            SetVisible(show);
            // 다시 켜질 때는 직전 드래그의 잔상 좌표가 남아 있으므로 비교를 무조건 실패시켜 즉시 갱신한다.
            _lastRect = new Rect(float.NaN, float.NaN, 0f, 0f);
        }

        if (!show) return;

        Rect rect = mm.BoxSelectScreenRect;
        if (rect == _lastRect) return; // 커서가 멈춘 프레임은 건드리지 않는다 → 불필요한 Canvas 리빌드 0

        _lastRect = rect;
        Apply(rect);
    }

    private void Apply(Rect rect)
    {
        _fill.anchoredPosition = rect.min;
        _fill.sizeDelta = rect.size;

        // 변 4개는 채움 사각형의 로컬 좌표(좌하단 원점)에 얹는다. 두께만큼 안쪽으로 그려 바깥으로 삐지 않는다.
        float w = rect.width;
        float h = rect.height;
        float t = Mathf.Min(k_EdgeThickness, Mathf.Min(w, h) * 0.5f); // 아주 작은 사각형에서 변끼리 겹치지 않게

        SetRect(_edgeBottom, 0f, 0f, w, t);
        SetRect(_edgeTop, 0f, h - t, w, t);
        SetRect(_edgeLeft, 0f, 0f, t, h);
        SetRect(_edgeRight, w - t, 0f, t, h);
    }

    private void SetVisible(bool on)
    {
        _visible = on;
        if (_fill != null) _fill.gameObject.SetActive(on);
    }

    private static void SetRect(RectTransform rt, float x, float y, float w, float h)
    {
        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta = new Vector2(w, h);
    }

    private static RectTransform CreateQuad(string name, Transform parent, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);

        // 좌하단 고정 앵커 + 좌하단 피벗 → anchoredPosition/sizeDelta에 스크린 좌표를 그대로 넣을 수 있다.
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.zero;
        rt.pivot = Vector2.zero;

        var image = go.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false; // 필수 — 켜면 커서가 항상 UI 위로 잡혀 모든 클릭이 죽는다

        return rt;
    }
}
