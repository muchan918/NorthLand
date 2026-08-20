using System.Collections.Generic;
using UnityEngine;

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

    readonly List<SkillEffectContainerView> _spawned = new();

    void Start()
    {
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
            view.Bind(reward, level);
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
}