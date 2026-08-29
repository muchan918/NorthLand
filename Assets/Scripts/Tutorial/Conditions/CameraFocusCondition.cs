using System;
using UnityEngine;

// 팝업 없는 연출 Step에서 카메라를 지정 지점으로 이동시키고, 도착하면 다음 Step으로 넘긴다.
// 사용자 조작을 측정하는 CameraMovedCondition과 달리 이 조건이 이동 자체를 시작한다.
//
// ⚠ 클래스 이름을 바꾸면 [SerializeReference]로 저장된 기존 스텝 데이터가 깨진다.
[Serializable]
public sealed class CameraFocusCondition : TutorialCondition
{
    public enum TargetMode
    {
        WorldPosition,
        CombatGridCell
    }

    [Tooltip("월드 좌표를 직접 쓸지, 런타임 전투 그리드 셀을 월드 좌표로 변환할지 선택한다.")]
    [SerializeField]
    private TargetMode targetMode = TargetMode.CombatGridCell;

    [Tooltip("World Position 모드에서 화면 중앙에 둘 월드 좌표.")]
    [SerializeField]
    private Vector3 worldPosition;

    [Tooltip("Combat Grid Cell 모드에서 화면 중앙에 둘 전투 셀 좌표.")]
    [SerializeField]
    private Vector2Int combatGridCell;

    [Tooltip("켜면 이동과 함께 카메라 줌 크기도 변경한다.")]
    [SerializeField]
    private bool changeZoom;

    [Tooltip("변경할 오쏘그래픽 카메라 크기. CameraController2의 범위로 제한된다.")]
    [Min(0.01f)]
    [SerializeField]
    private float zoomSize = 20f;

    private CameraController2 _camera;

    public override void Begin(TutorialContext context)
    {
        _camera = context.Camera;

        if (_camera == null)
        {
            Debug.LogWarning($"[{nameof(CameraFocusCondition)}] 씬에서 CameraController2를 찾지 못해 카메라 연출을 시작할 수 없다.");
            return;
        }

        Vector3 target = worldPosition;

        if (targetMode == TargetMode.CombatGridCell)
        {
            CombatSpace.CombatMapTileSpawner spawner = context.TileSpawner;

            if (spawner == null)
            {
                Debug.LogWarning($"[{nameof(CameraFocusCondition)}] 전투 그리드를 찾지 못해 카메라 연출을 시작할 수 없다.");
                _camera = null;
                return;
            }

            target = spawner.GridToWorldPosition(combatGridCell);
        }

        _camera.TutorialFocusCompleted += OnFocusCompleted;
        _camera.FocusViewCenterForTutorial(target, target.y, changeZoom, zoomSize);
    }

    public override void End()
    {
        if (_camera == null)
        {
            return;
        }

        _camera.TutorialFocusCompleted -= OnFocusCompleted;
        _camera.CancelTutorialFocus();
        _camera = null;
    }

    private void OnFocusCompleted()
    {
        Fire();
    }
}
