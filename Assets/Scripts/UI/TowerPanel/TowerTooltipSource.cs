using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// 타워 선택 패널의 버튼에 붙는 UI 호버 감지기(#141). 대상이 캔버스 UI 버튼이라 MouseManager 레이캐스트
/// 경로가 아니라 EventSystem(<see cref="IPointerEnterHandler"/>/<see cref="IPointerExitHandler"/>)으로
/// 호버를 잡고, 타워 전용 뷰(<see cref="TowerTooltipView"/>)에 표시를 위임한다.
/// <br/>
/// <see cref="TowerSelectPanelView.AddTowerButton"/>이 버튼 생성 시 런타임으로 AddComponent + <see cref="Init"/>한다
/// — 버튼 프리팹/씬을 편집할 필요가 없다.
/// </summary>
[DisallowMultipleComponent]
public class TowerTooltipSource : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    private TowerAsset _tower;

    /// <summary>버튼이 대표하는 TowerAsset을 주입한다. 패널이 이미 <c>tower.Data</c>를 채운 뒤 호출한다.</summary>
    public void Init(TowerAsset tower)
    {
        _tower = tower;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (_tower == null) return;
        // 뷰는 씬에 미리 없어도 EnsureExists가 자체 생성한다(씬 배치 불필요).
        // 자기 버튼(UI RectTransform)을 앵커로 넘겨 툴팁이 이 버튼 위에 뜨게 한다.
        TowerTooltipView.EnsureExists().Show(_tower, transform as RectTransform);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (TowerTooltipView.Instance != null) TowerTooltipView.Instance.Hide();
    }
}
