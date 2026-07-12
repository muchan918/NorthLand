using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 경영 패널 뷰. <see cref="ManagementController"/>를 구독해 자원 HUD·주민 풀·페이즈·생산 라인을 렌더하고,
/// '밤으로' 버튼을 컨트롤러에 연결한다. 로직은 전혀 갖지 않는다(위젯 참조 + 렌더링만) —
/// 실제 UI 아트로 교체 시 이 뷰의 인스펙터 참조만 다시 연결하면 컨트롤러/모델은 그대로다.<br/>
/// NordHold 유사 배치: 상단 자원 HUD, 하단/측면 생산 라인 리스트, 낮/밤 표시 + 밤 전환 버튼.<br/>
/// (Docs/ManagementArea/Resources.md — 이슈 #43)
/// </summary>
public class ManagementPanelView : MonoBehaviour
{
    [Header("컨트롤러")]
    [SerializeField] ManagementController _controller;

    [Header("자원 HUD")]
    [SerializeField] TMP_Text _woodText;
    [SerializeField] TMP_Text _ironText;
    [SerializeField] TMP_Text _foodText;
    [SerializeField] TMP_Text _manaText;

    [Header("주민 풀 / 페이즈")]
    [SerializeField] TMP_Text _villagerPoolText;
    [SerializeField] TMP_Text _phaseText;
    [SerializeField] Button _endDayButton;

    [Header("생산 라인")]
    [SerializeField] Transform _lineContainer;
    [SerializeField] ProductionLineView _linePrefab;

    private readonly List<ProductionLineView> _lineViews = new();

    private void Start()
    {
        if (_controller == null)
        {
            _controller = FindFirstObjectByType<ManagementController>();
        }
        if (_controller == null)
        {
            Debug.LogError("[경영패널] ManagementController를 찾을 수 없습니다.");
            return;
        }

        BuildLines();

        if (_endDayButton != null)
        {
            _endDayButton.onClick.RemoveAllListeners();
            _endDayButton.onClick.AddListener(_controller.RequestEndDay);
        }

        _controller.OnChanged += Refresh;
        Refresh();
    }

    private void OnDestroy()
    {
        if (_controller != null)
        {
            _controller.OnChanged -= Refresh;
        }
    }

    private void BuildLines()
    {
        if (_lineContainer == null || _linePrefab == null)
        {
            Debug.LogError("[경영패널] lineContainer/linePrefab이 연결되지 않았습니다.");
            return;
        }

        for (int i = 0; i < _controller.LineCount; i++)
        {
            var view = Instantiate(_linePrefab, _lineContainer);
            view.Bind(_controller, i);
            _lineViews.Add(view);
        }
    }

    private void Refresh()
    {
        if (_woodText != null)
        {
            _woodText.text = _controller.ResourceCount(ResourceKind.Wood).ToString();
        }
        if (_ironText != null)
        {
            _ironText.text = _controller.ResourceCount(ResourceKind.Iron).ToString();
        }
        if (_foodText != null)
        {
            _foodText.text = _controller.ResourceCount(ResourceKind.Food).ToString();
        }
        if (_manaText != null)
        {
            _manaText.text = _controller.ResourceCount(ResourceKind.Mana).ToString();
        }

        if (_villagerPoolText != null)
        {
            _villagerPoolText.text = $"Villagers {_controller.AssignedTotal}/{_controller.MaxVillagers}";
        }
        if (_phaseText != null)
        {
            _phaseText.text = _controller.IsDay ? $"Day (Wave {_controller.WaveCount})" : "Night (Defense)";
        }
        if (_endDayButton != null)
        {
            _endDayButton.interactable = _controller.CanEndDay;
        }

        for (int i = 0; i < _lineViews.Count; i++)
        {
            _lineViews[i].Refresh();
        }
    }
}
