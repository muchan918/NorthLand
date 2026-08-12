using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// [테스트 전용] 웨이브 보상(스킬 특수효과) 레벨을 직접 조작하는 서브패널(#350).
/// 목록은 <see cref="SkillEffectManager"/> 오브젝트에 실제로 붙어 있는 <see cref="SkillEffect"/>
/// 컴포넌트를 그대로 나열한다 — 매니저가 Awake에서 쓰는 수집 방식과 같으므로, 게임에서 실제로
/// 획득 가능한 효과와 목록이 어긋날 수 없다(부착되지 않은 타입은 아예 뜨지 않는다).
/// 행 하나 = 버튼 하나이며, 누를 때마다 레벨이 1 오르고 상한에서 멈춘다.
/// 패널의 초기화 버튼이 전 효과를 한 번에 0으로 되돌린다.
///
/// ⚠ 이 컴포넌트가 붙은 오브젝트는 씬에서 **활성**이어야 한다 — 비활성이면 Start가 안 돌아
/// 목록이 통째로 빈다. 껐다 켜는 대상은 참조로 받은 <see cref="_panelRoot"/>다
/// (TowerRecipeDebugPanel과 동일한 함정).
/// 프로덕션 코드가 아니라 Assets/Scripts/Test 소속.
/// </summary>
public class DebugSkillEffectSection : MonoBehaviour
{
    [Tooltip("필수: 켜고 끌 서브패널 루트. 이 컴포넌트가 붙은 오브젝트 자신이면 안 된다.")]
    [SerializeField] GameObject _panelRoot;

    [Tooltip("필수: 행을 담을 부모(서브패널의 Content).")]
    [SerializeField] Transform _content;

    [Tooltip("필수: DebugRowButton 프리팹(Button + 자식 TMP_Text). 타워 패널과 공용.")]
    [SerializeField] GameObject _rowPrefab;

    [Tooltip("선택: 비우면 SkillEffectManager.Instance로 폴백한다.")]
    [SerializeField] SkillEffectManager _manager;

    // 보상 타입의 한글 이름. 로컬라이즈 테이블에 보상명 키가 없어 여기서 소유한다.
    // 비ASCII 기호는 쓰지 않는다 - 기본 한글 폰트가 Static 에셋이라 공유 폰트가 더티가 된다.
    static readonly Dictionary<WaveRewardType, string> k_Names = new()
    {
        { WaveRewardType.Burn, "화상" },
        { WaveRewardType.Bomb, "폭발" },
        { WaveRewardType.ExtraCast, "추가시전" },
        { WaveRewardType.Field, "장판" },
        { WaveRewardType.Execute, "처형" },
    };

    // 컴포넌트 참조는 세션 내 안정적이라 캐시한다.
    readonly List<(SkillEffect effect, TMP_Text label)> _rows = new();

    // 인스펙터 배선을 우선하고, 비었으면 씬 싱글톤으로 폴백한다.
    SkillEffectManager Manager => _manager != null ? _manager : SkillEffectManager.Instance;

    void Start()
    {
        // 먼저 만들고 나중에 숨긴다 - 생성 시점엔 계층이 활성이라 레이아웃이 정상 계산된다.
        BuildRows();

        if (_panelRoot != null) _panelRoot.SetActive(false);
        else Debug.LogError("[스킬디버그] _panelRoot가 연결되지 않았습니다.");
    }

    // Btn_Skill의 onClick에 연결한다.
    public void Toggle()
    {
        if (_panelRoot == null) return;

        _panelRoot.SetActive(!_panelRoot.activeSelf);

        // 정상 보상 획득으로 레벨이 올랐을 수 있으므로 열 때마다 다시 그린다.
        if (_panelRoot.activeSelf) RefreshRows();
    }

    // 초기화 버튼의 onClick에 연결한다. 전 효과를 한 번에 0으로 되돌린다.
    public void ResetAllLevels()
    {
        SkillEffectManager manager = Manager;
        if (manager == null)
        {
            Debug.LogWarning("[스킬디버그] SkillEffectManager가 없습니다.");
            return;
        }

        foreach ((SkillEffect effect, TMP_Text _) in _rows)
        {
            manager.TryRestoreLevel(effect.Type, 0);
        }

        RefreshRows();
        Debug.Log("[스킬디버그] 전체 효과 레벨 초기화");
    }

    void BuildRows()
    {
        if (_content == null || _rowPrefab == null)
        {
            Debug.LogError("[스킬디버그] Content/행 프리팹이 연결되지 않았습니다.");
            return;
        }

        SkillEffectManager manager = Manager;
        if (manager == null)
        {
            Debug.LogError("[스킬디버그] SkillEffectManager가 없어 목록을 만들 수 없습니다.");
            return;
        }

        SkillEffect[] effects = manager.GetComponents<SkillEffect>();
        if (effects.Length == 0)
        {
            Debug.LogWarning("[스킬디버그] SkillEffectManager에 SkillEffect가 하나도 붙어 있지 않습니다.", manager);
            return;
        }

        // 컴포넌트 부착 순서는 인스펙터 조작에 따라 흔들린다 - enum 선언 순서로 고정한다.
        Array.Sort(effects, (a, b) => a.Type.CompareTo(b.Type));

        foreach (SkillEffect effect in effects)
        {
            GameObject row = Instantiate(_rowPrefab, _content);
            Button button = row.GetComponentInChildren<Button>();

            if (button == null)
            {
                Debug.LogError($"[스킬디버그] 행 프리팹에 Button이 없습니다: {effect.Type}", row);
                continue;
            }

            SkillEffect captured = effect;   // 클로저가 루프 변수를 잡지 않도록 복사
            button.onClick.AddListener(() => LevelUp(captured));

            _rows.Add((effect, row.GetComponentInChildren<TMP_Text>()));
        }

        RefreshRows();
    }

    void LevelUp(SkillEffect effect)
    {
        // 레벨 변경은 반드시 매니저를 경유한다(효과 컴포넌트를 직접 만지지 않는다).
        // 상한을 넘기면 TryRestoreLevel이 LogError로 거부하므로 먼저 잘라낸다.
        Manager.TryRestoreLevel(effect.Type, Mathf.Min(effect.Level + 1, effect.MaxLevel));

        // 성공/실패와 무관하게 다시 그린다 - 구독 실패로 거부됐을 때 표시가 실제와 어긋나지 않게.
        RefreshRows();
    }

    void RefreshRows()
    {
        foreach ((SkillEffect effect, TMP_Text label) in _rows)
        {
            if (label != null)
            {
                label.text = $"{Name(effect.Type)} Lv {effect.Level}/{effect.MaxLevel}";
            }
        }
    }

    static string Name(WaveRewardType type)
        => k_Names.TryGetValue(type, out string name) ? name : type.ToString();
}
