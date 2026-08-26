using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 화면 하단 타워 선택 패널(가로 스크롤뷰) 뷰. Content에 <see cref="TowerAsset"/> 하나당 버튼을 동적으로 추가하고,
/// 버튼 클릭 시 해당 TowerAsset의 값을 로그로 남긴다. 추후 타워 배치 툴은 <see cref="OnTowerSelected"/>를
/// 구독해 선택된 TowerAsset으로 배치 로직을 연결하면 된다(현재는 로그만).<br/>
/// 칸은 <b>아이콘만</b> 그린다 — 이름은 호버 툴팁(<see cref="TowerTooltipSource"/>)이 낸다(#470).<br/>
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
    private ManagementController _management; // 자원 조회용(소비처는 컨트롤러 경유 — WL-017). null이면 permissive.
    private DayNightManager _dayNight;        // 해금 웨이브 조회용. null이면(테스트 씬) 전부 해금으로 본다.
    private readonly List<(Button button, TowerAsset tower)> _buttons = new(); // 버튼별 갱신용
    private TowerAsset _restrictedTo;         // [튜토리얼용] 이것만 고를 수 있다. null이면 제한 없음.

    private void Awake()
    {
        _towerPlacer = GetComponent<TowerPlacer>();
        _management = FindFirstObjectByType<ManagementController>(); // 없으면(테스트 씬) 자원 게이트 없이 permissive

        if (_content == null || _buttonPrefab == null)
        {
            Debug.LogError("[타워선택패널] content/buttonPrefab이 연결되지 않았습니다.");
            return;
        }
    }

    private void Start()
    {
        // Instance는 DayNightManager.Awake에서 잡히므로 Start에서 읽는다.
        _dayNight = DayNightManager.Instance != null
            ? DayNightManager.Instance
            : FindFirstObjectByType<DayNightManager>();

        foreach (var tower in _towers)
        {
            AddTowerButton(tower);
        }

        // 자원 변동 시 버튼 활성/비활성 갱신 — 못 사는 타워는 버튼이 죽어 고스트 진입 자체가 막힌다.
        if (_management != null) _management.OnChanged += RefreshButtons;
        // 해금 갱신도 같은 경로를 쓴다. 낮이 시작되는 모든 시점에 오므로(부트스트랩 포함)
        // 웨이브가 오른 직후의 낮 화면에서 잠금이 풀린 상태로 들어온다.
        if (_dayNight != null) _dayNight.OnDayStart += RefreshButtons;
        RefreshButtons();
    }

    private void OnDestroy()
    {
        if (_management != null) _management.OnChanged -= RefreshButtons;
        if (_dayNight != null) _dayNight.OnDayStart -= RefreshButtons;
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

        // 아이콘만 채운다 — 이름 배너는 끈다(#470). 이름·코스트·스탯은 바로 아래에서 붙이는 호버
        // 툴팁이 낸다(합성 정보 패널의 아이콘 전용 칸과 같은 판단 — TowerMerge.md §8.5).
        // 합성 후보 버튼도 같은 규약이다(그쪽은 TowerMergeCandidateHover가 툴팁을 낸다).
        var view = button.GetComponent<TowerButtonView>();
        if (view != null) view.Set(tower.Icon);

        // 호버 시 타워 코스트/스탯 툴팁(#141). 버튼 프리팹 편집 없이 런타임으로 부착 —
        // tower.Data는 위에서 이미 채웠으므로 이름 조회가 가능하다.
        button.gameObject.AddComponent<TowerTooltipSource>().Init(tower);

        button.onClick.AddListener(() => HandleClick(tower));
        _buttons.Add((button, tower));
        RefreshButton(button, tower); // 초기 활성 상태 반영
    }

    /// <summary>해금 여부와 보유 자원을 버튼에 반영한다. (자원 변동·낮 시작 시 재호출)</summary>
    /// <summary>
    /// [튜토리얼용] 이 타워 하나만 고를 수 있게 제한한다. null을 넘기면 제한이 풀린다.<br/>
    /// 배치 비용 면제(<see cref="TowerPlacer.FreePlacement"/>)와는 <b>별개 축</b>이라, 무료로 짓게
    /// 하려면 둘 다 걸어야 한다. 무료 스위치를 먼저 세우고 이 메서드를 부를 것 —
    /// 여기서 <see cref="RefreshButtons"/>가 돌기 때문에 순서가 반대면 버튼이 옛 자원 게이트로 한 번 그려진다.
    /// </summary>
    public void RestrictTo(TowerAsset tower)
    {
        _restrictedTo = tower;
        RefreshButtons();
    }

    private void RefreshButtons()
    {
        foreach ((Button button, TowerAsset tower) in _buttons)
        {
            RefreshButton(button, tower);
        }
    }

    /// <summary>
    /// 해금 상태는 <see cref="DayNightManager.CurrentWave"/>로 **매번 계산한다** — 이벤트로 갱신된 값을
    /// 들고 있으면 세이브 복원 경로에서 조용히 틀어진다(`TryRestoreState`는 의도적으로 페이즈 이벤트를
    /// 발행하지 않으므로, 해금됐어야 할 타워가 잠긴 채 남는다).
    /// </summary>
    private bool IsUnlocked(TowerAsset tower)
        => _dayNight == null || _dayNight.CurrentWave >= tower.UnlockWave;

    /// 살 수 있는가. 무료 배치(<see cref="TowerPlacer.FreePlacement"/>) 중이면 자원을 보지 않는다 —
    /// 그 스위치는 TowerPlacer 안에서만 비용을 0으로 만들기 때문에, 여기서 같이 보지 않으면
    /// 자원이 모자랄 때 버튼이 회색이라 무료 배치를 **시작조차 못 한다**.
    private bool CanAfford(TowerAsset tower)
        => (_towerPlacer != null && _towerPlacer.FreePlacement)
           || _management == null
           || _management.CanAfford(tower.Cost);

    /// [튜토리얼용] 제한에 걸리지 않았는가. 제한이 없으면 항상 true.
    private bool IsAllowed(TowerAsset tower)
        => _restrictedTo == null || tower == _restrictedTo;

    private void RefreshButton(Button button, TowerAsset tower)
    {
        if (button == null) return;

        bool unlocked = IsUnlocked(tower);

        // 셋의 AND. 하나로 덮어쓰면 나머지 게이트가 죽는다.
        bool selectable = unlocked && CanAfford(tower) && IsAllowed(tower);

        var view = button.GetComponent<TowerButtonView>();
        if (view != null)
        {
            // 자물쇠는 해금 여부만 본다 — 자원 부족이나 튜토리얼 제한까지 자물쇠로 보이면
            // "다음 웨이브에 열린다"로 잘못 읽히고 세 상태가 구별되지 않는다.
            view.SetLocked(!unlocked);

            // 회색 처리와 interactable은 셋 전부를 보고, **뷰가 함께 소유한다**. 여기서 interactable을
            // 따로 세우면 Button의 ColorTint가 테두리를 즉시 밝혀, 해제 연출이 끝나기도 전에
            // 칸이 살아난 것처럼 보인다(#470).
            view.SetSelectable(selectable);
        }
        else
        {
            button.interactable = selectable;   // 뷰 없는 프리팹 변형 폴백
        }
    }

    private void HandleClick(TowerAsset tower)
    {
        if (!TutorialInputGate.Allows(TutorialAction.ChooseTowerForPlacement))
        {
            return;
        }

        if (_towerPlacer == null)
        {
            Debug.LogError("[타워선택패널] TowerPlacer가 연결되지 않았습니다.");
            return;
        }

        // 방어: interactable 우회로 클릭돼도 미해금이면 배치 진입 차단.
        if (!IsUnlocked(tower))
        {
            Debug.Log($"[타워선택패널] '{tower.TowerID}'는 아직 해금되지 않았습니다(해금 웨이브 {tower.UnlockWave}).");
            return;
        }

        // 방어: interactable 우회로 클릭돼도 자원 부족이면 배치 진입 차단.
        if (!CanAfford(tower))
        {
            Debug.Log($"[타워선택패널] 자원이 부족해 '{tower.TowerID}'를 배치할 수 없습니다.");
            return;
        }

        // 방어: interactable 우회로 클릭돼도 튜토리얼 제한 중이면 배치 진입 차단.
        if (!IsAllowed(tower))
        {
            Debug.Log($"[타워선택패널] 지금은 '{_restrictedTo.TowerID}'만 배치할 수 있습니다.");
            return;
        }

        _towerPlacer.BeginTowerPlacement(tower);

        OnTowerSelected?.Invoke(tower);
    }
}
