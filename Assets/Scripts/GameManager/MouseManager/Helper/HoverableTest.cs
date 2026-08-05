using UnityEngine;

/// IHoverable 동작 확인용 테스트 스크립트. SelectableTest(클릭 선택)와 짝이다.
///
/// 콜라이더 + `Selectable` 레이어가 있는 오브젝트에 붙이면 MouseManager가 호버 대상으로 집어
/// OnHoverChanged를 발행하고, OutlineInteractionDriver가 아웃라인을 켠다(#213).
/// MouseManager.UpdateHover가 `hit.collider.TryGetComponent(out IHoverable)`로 찾으므로
/// **콜라이더와 같은 GameObject에 붙어 있어야 한다.**
///
/// 툴팁은 이 테스트의 관심사가 아니라 null을 반환한다 — 표시 여부는 구독자(툴팁 UI) 책임이고
/// null이면 표시하지 않는다(IHoverable 규약). 실제 건물은 BuildingTooltipSource를 쓰며,
/// 그쪽은 BuildingAsset과 DataTableManager를 요구해 룩데브 씬에서는 쓸 수 없다.
[RequireComponent(typeof(Collider))]
public class HoverableTest : MonoBehaviour, IHoverable
{
    public TooltipContent? GetTooltipContent() => null;

    public void OnHoverEnter() { }

    public void OnHoverExit() { }
}
