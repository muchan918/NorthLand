using UnityEngine;

/// 건물에 붙는 호버 툴팁 공급자(어댑터). muchan의 BuildingInfo(클릭 선택 → 기능 패널)와 역할이 다르다 —
/// 이쪽은 '호버 시 이름·역할·설명을 요약해 보여주는' 툴팁만 담당한다.
/// muchan 코드(BuildingAsset/BuildingData/BuildingType)는 읽기만 하고 수정하지 않는다.
[RequireComponent(typeof(Collider))]
public class BuildingTooltipSource : MonoBehaviour, IHoverable
{
    [SerializeField] BuildingAsset _buildingAsset;
    [SerializeField] BuildingTooltipPalette _palette;

    // BuildingAsset.Data는 직렬화되지 않는 런타임 캐시(팀 규약: 호출부가 Start류에서 채운다).
    // 클릭용 BuildingInfo와 서로 의존하지 않도록 이쪽도 자체적으로 조회해 둔다.
    private BuildingData _data;

    private void Awake()
    {
        if (_buildingAsset == null)
        {
            Debug.LogError("BuildingTooltipSource: BuildingAsset이 할당되지 않았다.", this);
            return;
        }

        var table = DataTableManager.Get<BuildingTable>("BuildingTable");
        _data = table != null ? table.Get(_buildingAsset.BuildingID) : null;
        if (_data == null)
        {
            Debug.LogError($"BuildingTooltipSource: BuildingData를 찾지 못했다 ({_buildingAsset.BuildingID}).", this);
        }
    }

    public TooltipContent? GetTooltipContent()
    {
        // 데이터 조회 실패 시에도 최소한의 식별 정보는 보여준다.
        if (_data == null)
        {
            string fallbackHeader = _buildingAsset != null ? _buildingAsset.BuildingID : "?";
            return new TooltipContent(fallbackHeader, string.Empty, Color.gray, Color.black);
        }

        Color header, background;
        ResolveColors(out header, out background);

        return new TooltipContent($"{_data.DisplayName} - {_data.Role}", _data.Description, header, background);
    }

    // 건물 하이라이트는 이번 범위 밖 — 훅만 만족(#67).
    public void OnHoverEnter() { }
    public void OnHoverExit() { }

    private void ResolveColors(out Color header, out Color background)
    {
        if (_palette != null)
        {
            _palette.Resolve(_data.BuildingType, out header, out background);
            return;
        }

        // 팔레트 미할당 시 기본색 (팔레트를 붙이면 타입별로 구분됨)
        header = new(0.20f, 0.20f, 0.20f, 0.95f);
        background = new(0.10f, 0.10f, 0.10f, 0.95f);
    }
}
