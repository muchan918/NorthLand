using UnityEngine;
using UnityEngine.UI;

/// UI 버튼에 붙여, 클릭하면 배치(Placement)를 시작하는 컴포넌트. (요구사항 ①)
/// 버튼의 OnClick 이벤트에 StartPlacement()를 연결하면, 고스트가 마우스를 따라다니고
/// 배치 표면(Ground) 위에서 좌클릭하면 실제 오브젝트가 놓인다.
/// 실제 빌드 패널에서는 타워/건물마다 이 컴포넌트를 하나씩 두고 프리팹만 다르게 지정하면 된다.
/// TODO: 추후 타워/건물 데이터와 연결해, 배치 가능 여부·연속 배치 여부 등을 결정하도록 리팩토링
public class PlacementButton : MonoBehaviour
{
    [SerializeField] Button _button;
    [SerializeField] GameObject _ghostPrefab;    // 마우스를 따라다닐 프리뷰. Collider는 빼둘 것(배치 레이캐스트가 고스트를 맞지 않도록)
    [SerializeField] SelectableTest _placedPrefab;   // 실제로 배치될 프리팹(Collider + ISelectable 포함) // TODO: 추후 타워로 변경

    private void Awake()
    {
        _button.onClick.AddListener(OnPlacement);
    }

    private void OnPlacement()
    {
        MouseManager.Instance.BeginPlacement(new PlacementRequest
        {
            GhostPrefab = _ghostPrefab,

            // TODO: 해당 위치가 실제로 배치 가능한 셀인지(점유 여부·빌드 가능 영역 등) 검사 필요.
            //       현재는 배치 표면(_placementMask = Ground)에만 고스트가 올라가므로, Ground이면 무조건 배치 허용.
            //       실제 타워 배치의 그리드 검증은 TowerPlacer 참고.
            CanPlaceAt = hit => true,

            OnConfirmed = (hit, pos) =>
            {
                var placed = Instantiate(_placedPrefab, pos, Quaternion.identity);
                placed.Initialize($"배치된 타워", Color.green); // TODO: 실제 타워 데이터로 변경
            },
        });
    }
}
