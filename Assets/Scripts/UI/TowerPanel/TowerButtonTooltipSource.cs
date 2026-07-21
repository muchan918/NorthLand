using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// 타워 선택 패널의 타워 버튼(uGUI)에 붙어 호버 시 <see cref="TooltipUI"/>에 코스트+설명을 표시하는 공급자.
/// 건물 호버 툴팁(<see cref="BuildingTooltipSource"/>)은 3D 월드 호버(MouseManager+IHoverable) 경로를 타지만,
/// 버튼은 uGUI라 그 경로를 못 타므로 포인터 이벤트를 직접 받아 TooltipUI를 호출한다(#141).
/// </summary>
public class TowerButtonTooltipSource : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    // 건물 툴팁과 같은 팔레트 에셋을 공유한다 — BuildingType에 추가된 TowerPanel 항목을 색 키로 사용(BuildingTooltipSource 계보).
    [SerializeField] BuildingTooltipPalette _palette;

    private TowerAsset _tower;

    /// <summary>버튼 생성 시 TowerSelectPanelView가 주입한다.</summary>
    public void SetTower(TowerAsset tower) => _tower = tower;

    /// <summary>버튼 생성 시 TowerSelectPanelView가 팔레트를 주입한다. 미할당이면 팔레트 fallback색이 쓰인다.</summary>
    public void SetPalette(BuildingTooltipPalette palette) => _palette = palette;

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (_tower == null || TooltipUI.Instance == null) return;
        TooltipUI.Instance.Show(BuildContent());
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (TooltipUI.Instance != null) TooltipUI.Instance.Hide();
    }

    private TooltipContent BuildContent()
    {
        Color headerColor = new(0.20f, 0.20f, 0.20f, 0.95f);
        Color backgroundColor = new(0.10f, 0.10f, 0.10f, 0.95f);
        if (_palette != null)
        {
            _palette.Resolve(BuildingType.TowerPanel, out headerColor, out backgroundColor);
        }

        TowerData data = _tower.Data;
        if (data == null)
        {
            return new TooltipContent(_tower.TowerID, string.Empty, headerColor, backgroundColor);
        }

        string name = Localize(data.NameKey, _tower.TowerID);
        string role = Localize(data.RoleKey, string.Empty);
        string header = string.IsNullOrEmpty(role) ? name : $"{name} - {role}";

        var body = new StringBuilder();
        body.Append(Localize(data.DescriptionKey, string.Empty));
        AppendCost(body);

        return new TooltipContent(header, body.ToString(), headerColor, backgroundColor);
    }

    private void AppendCost(StringBuilder body)
    {
        if (_tower.Cost == null || _tower.Cost.Count == 0) return;

        if (body.Length > 0) body.Append("\n\n");
        for (int i = 0; i < _tower.Cost.Count; i++)
        {
            ResourceCost cost = _tower.Cost[i];
            if (cost.Resource == null) continue;

            // ResourceAsset.Data는 직렬화되지 않는 런타임 캐시 — 코스트로만 참조된 자원은
            // 아직 채워지지 않았을 수 있어 여기서 직접 조회한다(TowerSelectPanelView의 tower.Data 채움과 동일 규약).
            if (cost.Resource.Data == null)
            {
                cost.Resource.Data = DataTableManager.Get<ResourceTable>("ResourceTable")?.Get(cost.Resource.ResourceID);
            }

            string resourceName = cost.Resource.Data != null
                ? LocalizationHelper.Get(LocalizationHelper.k_DefaultTable, cost.Resource.Data.NameKey)
                : cost.Resource.ResourceID;

            if (i > 0) body.Append(", ");
            body.Append(resourceName).Append(" x").Append(cost.Amount);
        }
    }

    private static string Localize(string key, string fallback)
        => string.IsNullOrEmpty(key) ? fallback : LocalizationHelper.Get(LocalizationHelper.k_TowersTable, key);
}
