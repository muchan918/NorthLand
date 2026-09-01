using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Localization.Settings;
using UnityEngine.UI;
using NorthLand.UI;

// 경영 공간 전용 건물 정보 패널. 선택된 생산 건물의 이름·레벨·업그레이드 정보를 표시하고,
// 업그레이드 버튼으로 ManagementController.TryUpgrade를 호출한다(로직/뷰 분리 — 계산·차감은 컨트롤러).
// TowerInfoUI(전투 공간)와 같은 계보의 별도 싱글톤.
// (주의) 이 오브젝트는 씬에서 '활성' 상태로 둬야 Awake가 실행되어 Instance가 등록된다. 인스펙터에서 미리 꺼두지 말 것.
public class BuildingInfoUI : MonoBehaviour, IDisabledClickFeedback
{
    public static BuildingInfoUI Instance { get; private set; }

    // NorthLand_default 스트링 테이블 키 (짧은 라벨을 넣고 코드에서 조합 — ManagementPanelView 계보).
    private const string k_PerVillagerKey = "building.upgrade.per_villager";
    private const string k_MaxKey = "building.upgrade.max";
    private const string k_LevelKey = "building.upgrade.level";
    // 마법 연구소의 구체적인 강화 효과를 만들 수 없을 때(SO에 레벨 없음·씬에 SkillManager 없음)
    // ProduceRow 밖의 강화 효과 줄에 표시하는 폴백 문구다.
    private const string k_SkillPendingKey = "building.upgrade.skill_pending";
    // 본진 레벨 부족으로 다음 레벨이 잠긴 상태(#229). 진짜 최대(k_MaxKey)와 구분해야
    // "왜 못 올리는지"가 보인다 — Smart String {0}에 필요한 본진 레벨이 들어간다.
    private const string k_RequireCastleKey = "building.upgrade.require_castle";

    [Tooltip("업그레이드 데이터 소스. 비우면 씬에서 자동 탐색.")]
    [SerializeField] ManagementController _controller;

    [Header("BuildingInfoPanel 연결")]
    [Tooltip("건물명 (Lv 현재/최대)")]
    [SerializeField] TextMeshProUGUI _nameLevelText;
    [Tooltip("건물 설명 (BuildingTable.DescriptionKey)")]
    [SerializeField] TextMeshProUGUI _descriptionText;
    [Tooltip("업그레이드 버튼 위 강화 효과 문구 (마법 연구소 등 스킬 강화 건물에서만 채워진다)")]
    [SerializeField] TextMeshProUGUI _skillEffectText;
    [Tooltip("마법연구소 강화 효과 영역 전체")]
    [SerializeField] private GameObject _skillEffectBox;

    [Header("주민당 생산 (ProduceRow — 생산 건물에서만 표시)")]
    [Tooltip("생산 줄 전체. 생산 건물이 아니면 통째로 숨긴다.")]
    [SerializeField] GameObject _produceRow;
    [Tooltip("산출 자원 아이콘 (ResourceAsset.Icon)")]
    [SerializeField] Image _produceIcon;
    [Tooltip("주민당 현재 생산량")]
    [SerializeField] TextMeshProUGUI _produceAmountText;

    [Header("업그레이드 비용 (ScrollView)")]
    [Tooltip("비용 Row들이 생성될 ScrollView의 Content Transform")]
    [SerializeField] Transform _costContent;
    [Tooltip("비용 한 줄 프리팹 (BuildingCostRow)")]
    [SerializeField] BuildingCostRow _costRowPrefab;

    [SerializeField] Button _upgradeButton;

    private BuildingAsset _building;
    private int _lineIndex = -1;      // 생산 라인(주민당량 업그레이드) index. -1 = 생산 라인 아님
    private int _upgradeIndex = -1;   // 업그레이드 전용 건물(마법 연구소 등) index. -1 = 업그레이드 건물 아님
    private bool _subscribed;
    private ResourceTable _resourceTable; // 비용 자원 Data 채움용(호출부 채움 규약, SystemMap §2)

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        LocalizationSettings.SelectedLocaleChanged += HandleLocaleChanged;
        if (_upgradeButton != null)
        {
            _upgradeButton.onClick.AddListener(HandleUpgradeClicked);

            // 업그레이드음·거절음을 스스로 내므로 공용 클릭음에서 뺀다(CastlePanelUI와 같은 이유).
            UiClickSfxIgnore.ApplyTo(_upgradeButton);
        }
        HideInfo(); // Instance 등록 후 숨기므로 안전
    }

    private void OnDestroy()
    {
        if (Instance != this)
        {
            return;
        }
        LocalizationSettings.SelectedLocaleChanged -= HandleLocaleChanged;
        Unsubscribe();
        if (_upgradeButton != null)
        {
            _upgradeButton.onClick.RemoveListener(HandleUpgradeClicked);
        }
        Instance = null;
    }

    // 건물 선택 시 호출(BuildingInfo.OnSelected). 컨트롤러에서 업그레이드 상태를 pull해 표시한다.
    public void ShowInfo(BuildingAsset building)
    {
        if (building == null || !EnsureController())
        {
            HideInfo();
            return;
        }

        _building = building;
        _lineIndex = _controller.LineIndexOf(building);           // 생산 라인(나무·철·식량)
        _upgradeIndex = _controller.UpgradeIndexOf(building);     // 업그레이드 전용 건물(마법 연구소 등)
        Subscribe(); // 업그레이드·정산으로 상태가 바뀌면 자동 갱신
        gameObject.SetActive(true);
        Refresh();
    }

    public void HideInfo()
    {
        Unsubscribe();
        _building = null;
        _lineIndex = -1;
        _upgradeIndex = -1;
        gameObject.SetActive(false);
    }

    private void HandleUpgradeClicked()
    {
        if (!TutorialInputGate.Allows(TutorialAction.UpgradeBuilding))
        {
            return;
        }

        // 성공 시 컨트롤러 OnChanged → Refresh가 자동으로 다시 그린다(실패해도 무해).
        if (_controller == null)
        {
            return;
        }
        // **성공음은 여기서 내지 않는다**(WL-208). 컨트롤러가 OnBuildingAction으로 알리고 씬 큐가
        // 구독한다 — 업그레이드 진입점이 늘어도(예: `Test/BuildingsUpgradeHelper`) 소리가 따라온다.
        // 실패는 이벤트로 오지 않으므로 거절음만 호출부 몫이다.
        // ⚠ 자원이 모자라면 버튼이 **회색**이라(Refresh — CanUpgrade/CanUpgradeBuilding이 CanAfford를 포함)
        //   여기까지 오지 않는다. 「자원 부족」의 정상 경로는 OnDisabledClick이다.
        if (_lineIndex >= 0)
        {
            PlayUpgradeFeedback(_controller.TryUpgrade(_lineIndex));            // 생산 라인: 주민당량 업그레이드
        }
        else if (_upgradeIndex >= 0)
        {
            PlayUpgradeFeedback(_controller.TryUpgradeBuilding(_upgradeIndex)); // 마법 연구소 등: 레벨 업그레이드(마나석)
        }
    }

    // 반려된 이유를 소리로 가른다. 「자원을 더 모아라」와 「지금은 안 된다」(밤 페이즈·본진 레벨
    // 미달·최대 레벨)는 플레이어가 할 다음 행동이 달라, 한 소리로 뭉치면 안내가 되지 않는다.
    //
    // ⚠ 실제로 자주 도달하는 쪽은 아래 OnDisabledClick이다 — 자원이 모자라면 Refresh가 버튼을
    //   회색으로 내려(CanUpgrade/CanUpgradeBuilding이 CanAfford를 포함한다) onClick이 아예 안 난다.
    //   이 경로는 튜토리얼 강제 클릭·경합처럼 회색 판정과 실행 사이에 상태가 바뀐 경우의 방어다.
    private void PlayUpgradeFeedback(bool upgraded)
    {
        if (upgraded)
        {
            return;
        }

        if (BlockedByCostOnly())
        {
            Sfx.InsufficientResources();
        }
        else
        {
            Sfx.Rejected();
        }
    }

    /// <summary>
    /// 회색이 된 업그레이드 버튼을 눌렀을 때(<see cref="IDisabledClickFeedback"/>). 이 패널은 버튼 위가
    /// 아니라 **패널 루트**에 있고, 훅 탐색이 눌린 <c>Selectable</c>에서 부모로 올라오기 때문에 닿는다 —
    /// 그래서 <paramref name="pressed"/>로 자기 버튼인지 먼저 확인한다(패널 안 다른 회색 버튼이 생겨도
    /// 이 소리가 새어 나가지 않는다).
    ///
    /// ⚠ 게임 상태를 바꾸지 않는다 — <see cref="IDisabledClickFeedback"/>의 제약이다.
    /// </summary>
    public void OnDisabledClick(Selectable pressed)
    {
        if (_upgradeButton == null || pressed != _upgradeButton) return;
        if (!BlockedByCostOnly()) return;

        Sfx.InsufficientResources();
    }

    // 지금 업그레이드가 막힌 사유가 **비용 하나뿐**인가. 비활성 클릭 피드백과 핸들러 반려가 같은 술어를
    // 쓴다 — 갈라두면 "회색일 때와 눌렸을 때가 다른 소리"가 된다.
    //
    // 비용 외 사유를 먼저 전부 걷어낸 뒤 마지막에 CanUpgrade*로 되묻는 방식이다. CanAfford를 여기서
    // 다시 계산하지 않는 이유: 무료 처리(EffectiveCost)가 컨트롤러 안에 있어 밖에서 재현하면 갈라진다.
    //   · 밤 페이즈 — 기다려도 자원이 느는 문제가 아니다(낮이 와야 한다)
    //   · 튜토리얼 제한 — "모으면 된다"가 거짓 안내가 된다
    //   · 최대 레벨 — 비용 조회가 null이다(더 올릴 것이 없다)
    //   · 본진 레벨 미달 — RequiredCastleLevel > 0. 자원이 아니라 본진을 올려야 열린다(#229)
    private bool BlockedByCostOnly()
    {
        if (_controller == null || !_controller.IsDay) return false;
        if (!TutorialInputGate.AllowsForDisplay(TutorialAction.UpgradeBuilding)) return false;

        if (_lineIndex >= 0)
        {
            return _controller.LineUpgradeCost(_lineIndex) != null
                && _controller.LineRequiredCastleLevel(_lineIndex) == 0
                && !_controller.CanUpgrade(_lineIndex);
        }
        if (_upgradeIndex >= 0)
        {
            return _controller.UpgradeBuildingCost(_upgradeIndex) != null
                && _controller.UpgradeBuildingRequiredCastleLevel(_upgradeIndex) == 0
                && !_controller.CanUpgradeBuilding(_upgradeIndex);
        }
        return false;
    }

    private void Refresh()
    {
        // 설명은 건물 종류와 무관하게 같은 소스라 분기 이전에 한 번만 채운다.
        SetText(_descriptionText, BuildingDescription());

        // 업그레이드 전용 건물(현재는 마법 연구소)을 선택했을 때만
        // 스킬 강화 효과 영역 전체를 표시한다.
        // 텍스트만 비우면 배경 이미지는 계속 남으므로 박스 자체를 켜고 끈다.
        bool showSkillEffect = _upgradeIndex >= 0;

        if (_skillEffectBox != null)
        {
            _skillEffectBox.SetActive(showSkillEffect);
        }

        // 같은 패널을 여러 건물이 재사용하므로 먼저 기존 강화 문구를 비운다.
        // 마법 연구소를 본 다음 생산 건물을 선택했을 때
        // 이전 스킬 강화 내용이 남는 것을 방지한다.
        SetText(_skillEffectText, string.Empty);

        // 표시 대상 분기:
        // 생산 라인(주민당 생산량) → 업그레이드 전용 건물(마법 연구소 등)
        // → 그 외 업그레이드할 수 없는 건물 순서로 처리한다.
        if (_lineIndex >= 0)
        {
            RefreshProductionLine();
        }
        else if (_upgradeIndex >= 0)
        {
            RefreshUpgradeBuilding();
        }
        else
        {
            RefreshNonUpgradeable();
        }
    }

    // 업그레이드 대상이 아닌 건물(본진 등): 이름만 표시하고 업그레이드 행/버튼은 감춘다.
    private void RefreshNonUpgradeable()
    {
        SetText(_nameLevelText, BuildingName());
        HideProduceRow();
        ClearCostRows();
        if (_upgradeButton != null) _upgradeButton.gameObject.SetActive(false);
    }

    // 마법 연구소 등 업그레이드 전용 건물: 이름(Lv 현재/최대) + 효과 안내 + 비용(마나석) + 업그레이드 버튼.
    // 생산 라인과 달리 "주민당 자원" 같은 즉시 효과값이 없어, 대신 업그레이드 버튼 위에 이번 업그레이드로
    // 스킬 스탯이 몇 % 변하는지를 문장으로 보여준다(#375).
    private void RefreshUpgradeBuilding()
    {
        int level = _controller.UpgradeBuildingLevel(_upgradeIndex);
        int max = _controller.UpgradeBuildingMaxLevel(_upgradeIndex);
        bool isMax = level >= max;

        SetText(_nameLevelText, $"{BuildingName()} ({L(k_LevelKey)} {level}/{max})");
        // 마법 연구소는 ProduceRow를 숨기므로 안내를 그 안의 _produceAmountText에 쓰면 함께 사라진다.
        // ProduceRow 밖에 있는 강화 효과 줄을 공용 안내 자리로 사용해 잠금 사유와 폴백도 항상 보이게 한다.
        string lockNotice = LockNotice(_controller.UpgradeBuildingRequiredCastleLevel(_upgradeIndex));
        // 본진 제한에 걸리면 컨트롤러의 현재 허용 max와 level이 같아 isMax도 참이 될 수 있다.
        // 따라서 잠금 사유를 MAX보다 먼저 판정해야 "본진 Lv n 필요"가 가려지지 않는다.
        string skillNotice = lockNotice;
        if (string.IsNullOrEmpty(skillNotice) && !isMax)
        {
            skillNotice = UpgradeEffect(level + 1);
            if (string.IsNullOrEmpty(skillNotice))
            {
                skillNotice = L(k_SkillPendingKey);
            }
        }
        SetText(_skillEffectText, skillNotice);
        HideProduceRow(); // 주민당 산출이 없는 건물
        if (isMax)
        {
            ClearCostRows();
        }
        else
        {
            RebuildCostRows(_controller.UpgradeBuildingCost(_upgradeIndex));
        }

        if (_upgradeButton != null)
        {
            _upgradeButton.gameObject.SetActive(true);
            _upgradeButton.interactable = _controller.CanUpgradeBuilding(_upgradeIndex)
                && TutorialInputGate.AllowsForDisplay(TutorialAction.UpgradeBuilding);
        }
    }

    private void RefreshProductionLine()
    {
        int level = _controller.LineLevel(_lineIndex);
        int max = _controller.LineMaxLevel(_lineIndex);
        int cur = _controller.LineAmountPerVillager(_lineIndex);
        bool isMax = level >= max;

        SetText(_nameLevelText, $"{BuildingName()} ({L(k_LevelKey)} {level}/{max})");
        // 현재 산출과 업그레이드 증감은 ProduceRow의 _produceAmountText에 함께 표시한다.
        // 자원 종류는 아이콘이 말하므로 여기 텍스트에는 자원명을 넣지 않는다.
        ShowProduceRow(_building != null && _building.Production != null ? _building.Production.OutputResource : null,
            $"{L(k_PerVillagerKey)} {cur}");
        // 잠긴 경우 "MAX"가 아니라 "본진 Lv n 필요"가 된다 — 더 올릴 수 있다는 사실이 드러나야 한다(#229).
        string lockNotice = LockNotice(_controller.LineRequiredCastleLevel(_lineIndex));
        int nextAmount = _controller.LineNextAmountPerVillager(_lineIndex);
        SetText(_produceAmountText, isMax ? (lockNotice ?? L(k_MaxKey)) : $"{cur} > <color={UiPalette.PositiveHex}>{nextAmount}</color>");
        if (isMax)
        {
            ClearCostRows();
        }
        else
        {
            RebuildCostRows(_controller.LineUpgradeCost(_lineIndex));
        }

        if (_upgradeButton != null)
        {
            _upgradeButton.gameObject.SetActive(true);
            _upgradeButton.interactable = _controller.CanUpgrade(_lineIndex)
                && TutorialInputGate.AllowsForDisplay(TutorialAction.UpgradeBuilding);
        }
    }

    // 다음 레벨이 본진 레벨 부족으로 잠겼으면 "본진 Lv n 필요", 아니면 null(호출부가 기존 MAX 표기로 폴백).
    // 진짜 최대(더 이상 행이 없음)와 잠김(본진을 올리면 열림)은 플레이어에게 전혀 다른 정보다.
    //
    // ⚠ 인자는 컨트롤러가 주는 '내부' 레벨(0 = 미업그레이드)이고, 화면에 쓰는 본진 레벨은 +1한 값이다
    //   (CastlePanelUI가 GetUpgradeLevel + 1로 제목을 그리는 것과 같은 규약). 여기서 변환하지 않으면
    //   본진 Lv2인데 "본진 Lv2 필요"처럼 이미 만족한 조건을 요구하는 것으로 보인다.
    private static string LockNotice(int requiredCastleLevel) =>
        requiredCastleLevel > 0
            ? LocalizationHelper.Get(LocalizationHelper.k_DefaultTable, k_RequireCastleKey, requiredCastleLevel + 1)
            : null;

    // 이번 업그레이드로 감전 스탯이 **어떤 값이 되는지**(#398). 예전에는 "기본 대비 40% 증가합니다"였는데,
    // %는 플레이어가 체감하는 값이 아니라 "데미지 30 → 36" 형태의 실수치로 바꿨다. 표기 규약은 생산 건물의
    // "주민당 5 → 7"(RefreshProductionLine)·보상 카드(SkillStatsFormatter)와 같다.
    //
    // **강화 수치는 선택된 건물에서, 베이스 스탯은 SkillManager에서** 가져온다. SkillManager가 문자열까지
    // 만들게 하면 자기 _magicLabAsset을 기준으로 계산하므로, 두 번째 스킬 강화 건물이 생겼을 때
    // 클릭한 건물과 다른 수치를 보여주게 된다(PR#378 리뷰가 짚은 "같은 리스트를 읽어야 한다"는 축).
    // 피해·쿨타임은 절대값, 범위는 SkillManager.Scale을 태운 배율이다 — 실제 적용과 같은 식을 통과한다.
    //
    // 인자는 '지금 누르면 도달할' 레벨(1-based)이다.
    private string UpgradeEffect(int nextLevel)
    {
        BuildingAsset.SkillUpgradeLevel next = Scaling(nextLevel);
        // 스킬 강화 건물이 아니거나, authoring된 레벨이 없거나, 베이스 스탯 출처(씬의 SkillManager)가
        // 없다 — 어느 쪽이든 호출부의 스킬 강화 안내 폴백(k_SkillPendingKey)이 받는다.
        if (next == null || SkillManager.Instance == null)
        {
            return string.Empty;
        }

        // 미강화(Lv0)면 Scaling이 null을 주고, 그건 곧 베이스가 현재값이라는 뜻이다.
        BuildingAsset.SkillUpgradeLevel current = Scaling(nextLevel - 1);
        SkillManager skill = SkillManager.Instance;

        var lines = new List<string>(3);
        AddAbsoluteStatLine(lines, skill.BaseDamage, DamageValue(current, skill.BaseDamage),
            DamageValue(next, skill.BaseDamage), SkillStatsFormatter.BuildSkillDamageLine);
        AddScaledStatLine(lines, skill.BaseRadius, RadiusMult(current), RadiusMult(next),
            SkillStatsFormatter.BuildSkillRadiusLine);
        AddAbsoluteStatLine(lines, skill.BaseCooldown, CooldownValue(current, skill.BaseCooldown),
            CooldownValue(next, skill.BaseCooldown), SkillStatsFormatter.BuildSkillCooldownLine);
        return string.Join("\n", lines);
    }

    // 값이 변하는 스탯만 줄로 만든다. 세 줄을 무조건 채우면 그 레벨에서 안 건드리는 스탯이 "30 → 30"으로
    // 나온다 — 수치가 아직 placeholder(TBD)라 밸런싱 패스에서 한 레벨에 한 스탯만 올리는 순간 바로
    // 나타난다(PR#378 리뷰가 %표기에서 지적한 것과 같은 문제).
    private static void AddScaledStatLine(List<string> lines, float baseValue, float currentMultiplier,
                                          float nextMultiplier, Func<float, float, string> build)
    {
        float current = SkillManager.Scale(baseValue, currentMultiplier);
        float next = SkillManager.Scale(baseValue, nextMultiplier);
        if (!Mathf.Approximately(current, next))
        {
            lines.Add(HighlightNextValue(build(current, next)));
        }
    }

    private static void AddAbsoluteStatLine(List<string> lines, float baseValue, float currentValue,
                                            float nextValue, Func<float, float, string> build)
    {
        float current = currentValue > 0f ? currentValue : baseValue;
        float next = nextValue > 0f ? nextValue : baseValue;
        if (!Mathf.Approximately(current, next))
        {
            lines.Add(HighlightNextValue(build(current, next)));
        }
    }

    // SkillStatsFormatter가 만든 "현재 → 다음" 형식은 유지하면서 다음 값만 긍정 색으로 강조한다.
    // 화살표 뒤 공백은 기본색으로 남겨 색상 영역이 실제 수치부터 시작하도록 한다.
    private static string HighlightNextValue(string line)
    {
        if (string.IsNullOrEmpty(line))
        {
            return line;
        }

        int arrowIndex = line.LastIndexOf('>');
        if (arrowIndex < 0)
        {
            return line;
        }

        int valueStart = arrowIndex + 1;
        while (valueStart < line.Length && char.IsWhiteSpace(line[valueStart]))
        {
            valueStart++;
        }

        return valueStart >= line.Length
            ? line
            : $"{line.Substring(0, valueStart)}<color={UiPalette.PositiveHex}>{line.Substring(valueStart)}</color>";
    }

    // 배율 엔트리가 없으면(미강화 Lv0 · 범위 밖) 1.0 = 베이스 그대로. SkillManager.RefreshUpgrade의
    // "레벨 0/범위 밖 = 배율 1.0"과 같은 규약이다.
    private static float DamageValue(BuildingAsset.SkillUpgradeLevel s, float fallback)
        => s == null || s.DamageValue <= 0f ? fallback : s.DamageValue;
    private static float RadiusMult(BuildingAsset.SkillUpgradeLevel s) => s == null ? 1f : s.RadiusMultiplier;
    private static float CooldownValue(BuildingAsset.SkillUpgradeLevel s, float fallback)
        => s == null || s.CooldownSeconds <= 0f ? fallback : s.CooldownSeconds;

    // 레벨 n(1-based)의 배율 엔트리. 0이나 범위 밖이면 null = 강화 없음(SkillManager.RefreshUpgrade와 같은 규약).
    //
    // 컨트롤러가 레벨·MAX를 세는 것과 **물리적으로 같은 리스트**를 읽어야 한다(PR#378 리뷰). `UpgradeSteps`는
    // Castle→Skill→Production→Exchange 중 첫 비어있지 않은 그룹 하나만 돌려주므로(BuildingAsset.cs:31),
    // 여기서 `Skill`을 고정으로 읽으면 두 그룹을 동시에 authoring한 건물에서 "Lv 2/3인데 4행째의 %"가 뜬다.
    // 고른 그룹이 스킬 강화가 아니면 `as`가 null → 문구 없음(폴백)이라 타입 판정도 겸한다.
    private BuildingAsset.SkillUpgradeLevel Scaling(int level)
    {
        IReadOnlyList<BuildingAsset.UpgradeStep> steps = _building?.UpgradeSteps;
        return (steps == null || level < 1 || level > steps.Count)
            ? null
            : steps[level - 1] as BuildingAsset.SkillUpgradeLevel;
    }

    private string BuildingName()
    {
        if (_building == null || _building.Data == null)
        {
            return "-";
        }
        return LocalizationHelper.Get(LocalizationHelper.k_BuildingsTable, _building.Data.NameKey);
    }

    private string BuildingDescription()
    {
        if (_building == null || _building.Data == null)
        {
            return string.Empty;
        }
        return LocalizationHelper.Get(LocalizationHelper.k_BuildingsTable, _building.Data.DescriptionKey);
    }

    // 비용 자원마다 Row 프리팹을 하나씩 생성해 ScrollView Content에 채운다.
    // 각 Row는 지갑 보유량 대비 감당 여부(자원별)로 초록/회색이 갈린다.
    private void RebuildCostRows(IReadOnlyList<ResourceCost> costs)
    {
        ClearCostRows();
        if (costs == null || _costContent == null || _costRowPrefab == null)
        {
            return;
        }

        foreach (ResourceCost c in costs)
        {
            if (c == null || c.Resource == null || c.Amount <= 0)
            {
                continue;
            }
            ResourceData data = ResolveData(c.Resource);
            if (data == null)
            {
                continue;
            }

            // 자원 종류는 아이콘으로만 표기한다 — Data는 지갑 대조(Kind)에만 쓰인다.
            bool affordable = _controller.ResourceCount(data.Kind) >= c.Amount;

            BuildingCostRow row = Instantiate(_costRowPrefab, _costContent, false);
            row.Set(c.Resource.Icon, c.Amount, affordable);
        }
    }

    // Content의 기존 Row(초기 예시 포함)를 모두 제거한다.
    private void ClearCostRows()
    {
        if (_costContent == null)
        {
            return;
        }
        for (int i = _costContent.childCount - 1; i >= 0; i--)
        {
            Destroy(_costContent.GetChild(i).gameObject);
        }
    }

    // ResourceAsset.Data 채움(호출부 채움 규약, SystemMap §2) — 비용 자원 종류·표시명 해석에 필요.
    private ResourceData ResolveData(ResourceAsset resource)
    {
        if (resource == null)
        {
            return null;
        }
        if (resource.Data == null)
        {
            _resourceTable ??= DataTableManager.Get<ResourceTable>("ResourceTable");
            if (_resourceTable != null)
            {
                resource.Data = _resourceTable.Get(resource.ResourceID);
            }
        }
        return resource.Data;
    }

    // 생산 줄을 켜고 아이콘·수량을 채운다. 아이콘 미할당 SO는 흰 사각형 대신 빈 칸으로 둔다(BuildingCostRow와 같은 규약).
    private void ShowProduceRow(ResourceAsset output, string amountLabel)
    {
        if (_produceRow != null)
        {
            _produceRow.SetActive(true);
        }
        if (_produceIcon != null)
        {
            Sprite icon = output != null ? output.Icon : null;
            _produceIcon.enabled = icon != null;
            _produceIcon.sprite = icon;
        }
        SetText(_produceAmountText, amountLabel);
    }

    private void HideProduceRow()
    {
        if (_produceRow != null)
        {
            _produceRow.SetActive(false);
        }
    }

    private static void SetText(TextMeshProUGUI label, string value)
    {
        if (label != null)
        {
            label.text = value;
        }
    }

    // NorthLand_default 키를 현재 로케일로 조회(짧은 라벨). 지속 표시지만 OnChanged마다 재조회돼 갱신된다(ManagementPanelView와 동일 pull 방식).
    private static string L(string key) => LocalizationHelper.Get(LocalizationHelper.k_DefaultTable, key);

    private bool EnsureController()
    {
        if (_controller != null)
        {
            return true;
        }
        _controller = FindFirstObjectByType<ManagementController>();
        if (_controller == null)
        {
            Debug.LogWarning("[BuildingInfoUI] ManagementController를 찾지 못해 업그레이드 정보를 표시할 수 없습니다.", this);
            return false;
        }
        return true;
    }

    private void Subscribe()
    {
        if (_subscribed || _controller == null)
        {
            return;
        }
        _controller.OnChanged += Refresh;
        _subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!_subscribed || _controller == null)
        {
            return;
        }
        _controller.OnChanged -= Refresh;
        _subscribed = false;
    }

    private void HandleLocaleChanged(UnityEngine.Localization.Locale locale)
    {
        if (_building != null)
        {
            Refresh();
        }
    }
}
