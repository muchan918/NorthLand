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

    // 이 타워를 **합성으로** 얻는 레시피(배치 팔레트 버튼처럼 합성과 무관한 칸이면 null). 코스트 슬롯이
    // 자원 대신 재료 타워를 내야 하는지를 이 값이 가른다 — 합성으로만 얻는 타워는 자원 코스트가 비어 있어서
    // 넘기지 않으면 툴팁의 노란 줄이 통째로 빈다(#445).
    private TowerRecipe _recipe;

    /// <summary>버튼이 대표하는 TowerAsset을 주입한다. 패널이 이미 <c>tower.Data</c>를 채운 뒤 호출한다.</summary>
    /// <param name="recipe">이 타워를 만드는 합성 레시피(합성 후보 칸만 넘긴다). 넘기면 코스트 슬롯이 재료 타워를 낸다.</param>
    public void Init(TowerAsset tower, TowerRecipe recipe = null)
    {
        _tower = tower;
        _recipe = recipe;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (_tower == null) return;
        // 뷰는 씬에 미리 없어도 EnsureExists가 자체 생성한다(씬 배치 불필요).
        // 자기 버튼(UI RectTransform)을 앵커로 넘겨 툴팁이 이 버튼 위에 뜨게 한다.
        TowerTooltipView.EnsureExists().Show(_tower, transform as RectTransform, null, _recipe);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (TowerTooltipView.Instance != null) TowerTooltipView.Instance.Hide();
    }

    // 호버 중 버튼/패널이 비활성화되면(예: 낮→밤 전환 시 타워 패널 SetActive(false)) EventSystem이
    // OnPointerExit를 주지 않아 툴팁이 화면에 잔류한다 — 비활성 시 명시적으로 숨긴다.
    private void OnDisable()
    {
        if (TowerTooltipView.Instance != null) TowerTooltipView.Instance.Hide();
    }
}
