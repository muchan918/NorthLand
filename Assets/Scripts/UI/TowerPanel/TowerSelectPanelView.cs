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
[RequireComponent(typeof(TowerPlacer))]
public class TowerSelectPanelView : MonoBehaviour
{
    [Header("스크롤뷰")]
    [SerializeField] Transform _content;   // Scroll View의 Content
    [SerializeField] Button _buttonPrefab; // 타워 버튼 프리팹

    [Header("타워 목록")]
    [SerializeField] List<TowerAsset> _towers = new();

    /// <summary>버튼 클릭 시 선택된 TowerAsset을 발행. 추후 배치 툴이 구독한다.</summary>
    public event Action<TowerAsset> OnTowerSelected;

    private TowerPlacer _towerPlacer;

    private void Awake()
    {
        _towerPlacer = GetComponent<TowerPlacer>();

        if (_content == null || _buttonPrefab == null)
        {
            Debug.LogError("[타워선택패널] content/buttonPrefab이 연결되지 않았습니다.");
            return;
        }
    }

    private void Start()
    {
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

        // SO를 버튼에 주입하는 시점에 Data(에셋에 저장 안 되는 런타임 캐시)를 CSV에서 채운다.
        // (Building/Resource와 동일한 Data 채움 규약 — SystemMap §2. 실제 소비는 TowerPlacer)
        if (tower.Data == null)
        {
            tower.Data = DataTableManager.Get<TowerTable>("TowerTable")?.Get(tower.TowerID);
            if (tower.Data == null)
                Debug.LogWarning($"[타워선택패널] TowerData 없음(TowerID={tower.TowerID}) — TowerTable.csv 행을 확인하세요.");
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
        if (_towerPlacer == null)
        {
            Debug.LogError("[타워선택패널] TowerPlacer가 연결되지 않았습니다.");
            return;
        }

        _towerPlacer.BeginTowerPlacement(tower);

        OnTowerSelected?.Invoke(tower);
    }
}
