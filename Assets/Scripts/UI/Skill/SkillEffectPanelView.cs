using System.Collections.Generic;
using UnityEngine;
using TMPro;

// 획득한 스킬 특수효과를 아이콘으로 나열하는 패널(#397).
// 풀의 고정 순서를 유지하되 획득한 것만 앞으로 당겨 표시한다 — 자리가 고정돼야
// "화상은 항상 첫 칸"처럼 위치 자체가 정보가 된다. 당기는 동작은 HorizontalLayoutGroup이 맡고,
// 여기서는 미획득을 건너뛰며 순서대로 생성만 한다.
public class SkillEffectPanelView : MonoBehaviour
{
    [Tooltip("표시 순서의 기준이자 타입→아이콘의 출처. SO 에셋이라 프리팹에 그대로 직렬화된다.")]
    [SerializeField] WaveRewardPool _pool;

    [Tooltip("아이콘이 생성될 부모. HorizontalLayoutGroup이 붙은 오브젝트.")]
    [SerializeField] RectTransform _content;

    [SerializeField] SkillEffectContainerView _containerPrefab;

    [Header("툴팁")]
    [Tooltip("호버 라벨. Content 밖에 둔다 — 안에 넣으면 레이아웃 그룹이 한 칸으로 취급한다.")]
    [SerializeField] GameObject _tooltipPanel;

    [SerializeField] TextMeshProUGUI _tooltipText;

    [Tooltip("커서를 따라 움직일 RectTransform. pivot을 좌상단(0, 1)으로 둘 것.")]
    [SerializeField] RectTransform _tooltipRect;

    [Tooltip("스케일 팩터 계산용. 비워두면 부모에서 자동으로 찾는다 — 프리팹은 씬의 Canvas를 참조할 수 없다.")]
    [SerializeField] Canvas _canvas;

    [SerializeField] Vector2 _cursorOffset = new(16f, -16f);

    readonly List<SkillEffectContainerView> _spawned = new();

    void Start()
    {
        // 프리팹은 씬 오브젝트인 Canvas를 직렬화할 수 없어 인스펙터로 꽂을 방법이 없다.
        // 인스펙터 지정을 우선하되(다른 Canvas를 쓰고 싶을 때) 비어 있으면 부모에서 찾는다.
        if (_canvas == null) _canvas = GetComponentInParent<Canvas>();

        HideTooltip();

        // Awake가 아니라 Start에서 잡는다 — SkillEffectManager도 Awake에서 Instance를 세우므로
        // 실행 순서가 보장되지 않는다.
        if (SkillEffectManager.Instance != null)
            SkillEffectManager.Instance.EffectsChanged += Rebuild;

        Rebuild();
    }

    void OnDestroy()
    {
        if (SkillEffectManager.Instance != null)
            SkillEffectManager.Instance.EffectsChanged -= Rebuild;
    }

    // 최대 6칸이고 웨이브당 한 번 바뀌므로 통째로 지우고 다시 만든다.
    void Rebuild()
    {
        // 커서를 아이콘에 올려둔 채 보상을 획득하면 그 칸이 파괴되면서 OnPointerExit이 오지 않는다.
        // 라벨이 화면에 남는 #415와 같은 증상이라, 파괴하는 쪽에서 먼저 닫는다.
        HideTooltip();

        Clear();

        if (_pool == null || _content == null || _containerPrefab == null) return;

        SkillEffectManager manager = SkillEffectManager.Instance;

        if (manager == null) return;

        foreach (WaveRewardData reward in _pool.Rewards)
        {
            if (reward == null) continue;

            int level = manager.GetLevel(reward.RewardType);

            // 미획득은 건너뛴다 — 뒤에 있던 것이 자연히 앞으로 당겨진다.
            if (level <= 0) continue;

            SkillEffectContainerView view = Instantiate(_containerPrefab, _content);
            view.Bind(reward, level, this);
            _spawned.Add(view);
        }
    }

    void Clear()
    {
        foreach (SkillEffectContainerView view in _spawned)
        {
            if (view != null)
                Destroy(view.gameObject);
        }

        _spawned.Clear();
    }

    // 칸마다 툴팁을 두면 낭비라 패널이 하나만 갖고, 각 칸이 자기 정보를 넘겨 호출한다
    // (BuildingShortcutBar가 바로가기 버튼 6개에 툴팁 하나를 쓰는 것과 같은 구조).
    public void ShowTooltip(WaveRewardData reward, int level)
    {
        if (_tooltipPanel == null || _tooltipText == null || reward == null) return;

        string name = LocalizationHelper.Get(LocalizationHelper.k_RewardsTable, reward.DisplayName);

        _tooltipText.text = $"{name} {SkillStatsFormatter.BuildCurrentLevelLine(level)}";
        _tooltipPanel.SetActive(true);
    }

    public void HideTooltip()
    {
        if (_tooltipPanel != null)
            _tooltipPanel.SetActive(false);
    }

    void LateUpdate()
    {
        if (_tooltipPanel != null && _tooltipPanel.activeSelf) FollowCursor();
    }

    // 커서 위치는 MouseManager 경유로 얻는다 — Mouse.current 직접 폴링 금지(입력 단일 창구 계약).
    // _tooltipRect의 pivot이 좌상단(0, 1)이라고 가정한다. BuildingShortcutBar.FollowCursor와 같은 계산.
    void FollowCursor()
    {
        MouseManager mouse = MouseManager.Instance;

        if (mouse == null || _tooltipRect == null) return;

        Vector2 pos = mouse.PointerPosition + _cursorOffset;
        float scale = _canvas != null ? _canvas.scaleFactor : 1f;
        Vector2 size = _tooltipRect.rect.size * scale;

        pos.x = Mathf.Clamp(pos.x, 0f, Mathf.Max(0f, Screen.width - size.x));
        pos.y = Mathf.Clamp(pos.y, size.y, Screen.height);
        _tooltipRect.position = pos;
    }
}