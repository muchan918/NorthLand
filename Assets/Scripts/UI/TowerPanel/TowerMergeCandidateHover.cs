using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// 합성 패널 후보 버튼에 붙는 UI 호버 감지기(#213 §5.3). 커서를 올리면 그 레시피가 **실제로 소모할 재료
/// 타워만** 핑크 아웃라인으로 바뀐다. 대상이 캔버스 UI 버튼이라 MouseManager 레이캐스트 경로가 아니라
/// EventSystem(<see cref="IPointerEnterHandler"/>/<see cref="IPointerExitHandler"/>)으로 호버를 잡는다
/// — <see cref="TowerTooltipSource"/>와 같은 선례.
/// <br/>
/// 판정·월드 하이라이트 구동 권한은 코디네이터가 단독으로 갖는다(뷰는 파사드만 호출하는 현행 계약 유지).
/// <see cref="TowerMergePanelView"/>가 버튼 생성 시 런타임으로 AddComponent + <see cref="Init"/>한다.
/// </summary>
[DisallowMultipleComponent]
public class TowerMergeCandidateHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    private TowerMergeCoordinator _coordinator;
    private TowerRecipe _recipe;

    /// <summary>이 버튼이 대표하는 레시피와 코디네이터를 주입한다.</summary>
    public void Init(TowerMergeCoordinator coordinator, TowerRecipe recipe)
    {
        _coordinator = coordinator;
        _recipe = recipe;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (_coordinator == null || _recipe == null) return;
        _coordinator.PreviewMerge(_recipe);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (_coordinator != null) _coordinator.ClearMergePreview();
    }

    // 호버 중 버튼이 꺼지면(매칭 해제로 SetActive(false), 패널 닫힘, 밤 전환) EventSystem이 OnPointerExit를
    // 주지 않아 핑크가 월드에 잔류한다 — 비활성 시 명시적으로 해제한다(TowerTooltipSource와 같은 함정).
    private void OnDisable()
    {
        if (_coordinator != null) _coordinator.ClearMergePreview();
    }
}
