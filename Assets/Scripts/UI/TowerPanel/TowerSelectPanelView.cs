using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 화면 하단 타워 선택 패널(가로 스크롤뷰) 뷰. Content에 <see cref="TowerAsset"/> 하나당 버튼을 동적으로 추가하고,
/// 버튼 클릭 시 해당 TowerAsset의 값을 로그로 남긴다. 추후 타워 배치 툴은 <see cref="OnTowerSelected"/>를
/// 구독해 선택된 TowerAsset으로 배치 로직을 연결하면 된다(현재는 로그만).<br/>
/// 라벨은 지금은 <c>TowerID</c>를 표시하지만, TowerAsset에 아이콘(Sprite) 필드가 생기면 이 자리를 아이콘으로 교체하면 된다.<br/>
/// 버튼 배치·스크롤 범위는 Content의 Horizontal Layout Group + Content Size Fitter가 담당하므로
/// 이 스크립트는 좌표를 계산하지 않는다.
/// </summary>
public class TowerSelectPanelView : MonoBehaviour
{
    [Header("스크롤뷰")]
    [SerializeField] Transform _content;   // Scroll View의 Content
    [SerializeField] Button _buttonPrefab; // 타워 버튼 프리팹

    [Header("타워 목록")]
    [SerializeField] List<TowerAsset> _towers = new();

    /// <summary>버튼 클릭 시 선택된 TowerAsset을 발행. 추후 배치 툴이 구독한다.</summary>
    public event Action<TowerAsset> OnTowerSelected;

    private void Start()
    {
        if (_content == null || _buttonPrefab == null)
        {
            Debug.LogError("[타워선택패널] content/buttonPrefab이 연결되지 않았습니다.");
            return;
        }

        foreach (var tower in _towers)
        {
            AddTowerButton(tower);
        }
    }

    /// <summary>타워 버튼 하나를 스크롤뷰에 추가한다. 런타임에 반복 호출해도 된다.</summary>
    public void AddTowerButton(TowerAsset tower)
    {
        if (tower == null)
        {
            Debug.LogError("[타워선택패널] null TowerAsset은 추가할 수 없습니다.");
            return;
        }

        var button = Instantiate(_buttonPrefab, _content);

        var label = button.GetComponentInChildren<TMP_Text>();
        if (label != null)
        {
            label.text = tower.TowerID;
        }

        button.onClick.AddListener(() => HandleClick(tower));
    }

    private void HandleClick(TowerAsset tower)
    {
        var attack = GetAttack(tower);
        if (attack != null)
        {
            Debug.Log($"[타워선택패널] {tower.TowerID} | 데미지={attack.AttackDamage}, 공격주기={attack.AttackInterval}s, 사거리={attack.AttackRange}");
        }
        else
        {
            Debug.Log($"[타워선택패널] {tower.TowerID} (type={tower.TowerType}) — 공격 스탯 없음(마법 타입)");
        }

        OnTowerSelected?.Invoke(tower);
    }

    // Single/Area/Chain은 각 타입 그룹의 Attack에 공격 스탯이 있고, Magic은 없다(null).
    private static TowerAsset.AttackFields GetAttack(TowerAsset tower)
    {
        switch (tower.TowerType)
        {
            case TowerType.Single: return tower.Single?.Attack;
            case TowerType.Area: return tower.Area?.Attack;
            case TowerType.Chain: return tower.Chain?.Attack;
            default: return null;
        }
    }
}
