using System;
using UnityEngine;

// 타워 패널에서 타워를 골랐으면 충족된다. 배치까지는 보지 않는다 —
// "패널만 열어 두고 고르게 한 뒤 딤을 걷는" 단계를 만들기 위한 조건이다.
//
// ⚠ 클래스 이름을 바꾸면 [SerializeReference]로 저장된 기존 스텝 데이터가 깨진다.
[Serializable]
public class TowerSelectedCondition : TutorialCondition
{
    [Tooltip("특정 타워만 인정하려면 지정한다. 비우면 아무 타워나 인정한다.")]
    [SerializeField]
    private TowerAsset targetTower;

    private TowerSelectPanelView _panel;

    public override void Begin(TutorialContext context)
    {
        _panel = context.TowerPanel;

        if (_panel == null)
        {
            Debug.LogWarning($"[{nameof(TowerSelectedCondition)}] 씬에서 타워 패널을 찾지 못해 이 단계를 넘어갈 수 없다.");

            return;
        }

        _panel.OnTowerSelected += OnTowerSelected;
    }

    public override void End()
    {
        if (_panel != null)
        {
            _panel.OnTowerSelected -= OnTowerSelected;
            _panel = null;
        }
    }

    private void OnTowerSelected(TowerAsset tower)
    {
        if (targetTower == null || targetTower == tower)
        {
            Fire();
        }
    }
}
