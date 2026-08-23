using System;
using UnityEngine;

// 경영 공간에서 지정한 건물을 클릭해 선택하면 충족된다.
// 정보 패널(BuildingInfoUI)이 실제로 열렸는지는 보지 않는다 — 패널을 여는 주체는 BuildingInfo이고,
// 어느 패널이 열리는지도 그쪽이 정한다(본진·상점·기본 정보). 튜토리얼이 알아야 하는 것은
// "플레이어가 그 건물을 골랐다"뿐이다.
//
// ⚠ 클래스 이름을 바꾸면 [SerializeReference]로 저장된 기존 스텝 데이터가 깨진다.
[Serializable]
public class BuildingSelectedCondition : TutorialCondition
{
    [Tooltip("특정 건물만 인정하려면 지정한다. 비우면 아무 건물이나 인정한다.")]
    [SerializeField]
    private BuildingAsset targetBuilding;

    private MouseManager _mouse;

    public override void Begin(TutorialContext context)
    {
        // MouseManager는 static Instance를 갖고 있어 Context를 거치지 않는다.
        _mouse = MouseManager.Instance;

        if (_mouse == null)
        {
            Debug.LogWarning($"[{nameof(BuildingSelectedCondition)}] MouseManager가 없어 이 단계를 넘어갈 수 없다.");

            return;
        }

        // OnSelectionChanged가 아니라 OnPrimarySelect를 구독한다 — 전자는 _selected 변화만 통지하므로
        // 그 건물이 이미 선택된 채로 이 단계에 들어오면 다시 클릭해도 신호가 오지 않는다(WL-085).
        _mouse.OnPrimarySelect += OnPrimarySelect;
    }

    public override void End()
    {
        if (_mouse != null)
        {
            _mouse.OnPrimarySelect -= OnPrimarySelect;
            _mouse = null;
        }
    }

    private void OnPrimarySelect(ISelectable selected)
    {
        // 빈 곳 클릭(null)이나 타워 선택은 그냥 흘려보낸다.
        if (selected is not BuildingInfo building)
        {
            return;
        }

        // 월드 클릭에서 건물 SO로 건너오는 정본 경로가 BuildingInfo.Asset이다
        // (컨트롤러는 건물을 SO로만 알기 때문에 이 변환이 필요하다).
        if (targetBuilding == null || building.Asset == targetBuilding)
        {
            Fire();
        }
    }
}
