using System;
using System.Collections.Generic;
using UnityEngine;

// 마법 연구소(#205) 레벨에 따라 감전 스킬의 착탄 이펙트를 교체하기 위한 매핑 테이블.
// 스킬은 즉발형 그대로이고 바뀌는 건 "어떤 프리팹을 스폰하느냐"뿐이다 — 낙하/메테오처럼
// 움직이는 연출과 지연 데미지는 보상 특수효과 축(SkillEffect, #169)의 몫이다.
// 레벨 개수를 맞출 의무가 없는 희소 매핑: FromLevel 0/3/5 세 칸만 채워도 사이 레벨은 아래 엔트리를 쓴다.
[CreateAssetMenu(fileName = "SkillVisualSet", menuName = "Scriptable Objects/Skill/SkillVisualSet")]
public class SkillVisualSet : ScriptableObject
{
    [Serializable]
    public class LevelVisual
    {
        [Tooltip("이 레벨 이상에서 적용")]
        [Min(0)] public int FromLevel;

        [Tooltip("착탄 지점에 스폰할 이펙트. 파티클의 Stop Action이 Destroy인지 확인할 것")]
        public GameObject Prefab;

        [Tooltip("켜면 연구소 사거리 배율만큼 이펙트 크기도 키운다")]
        public bool ScaleWithRadius = true;

        [TextArea] public string Note;
    }

    [SerializeField] List<LevelVisual> _levels = new List<LevelVisual>();

    // level 이하 FromLevel 중 가장 큰 엔트리. 없거나 Prefab이 비어 있으면 null(호출부가 폴백).
    public LevelVisual Resolve(int level)
    {
        LevelVisual best = null;
        for (int i = 0; i < _levels.Count; i++)
        {
            LevelVisual entry = _levels[i];
            if (entry == null || entry.Prefab == null) continue;   // Missing 참조도 여기서 걸러진다
            if (entry.FromLevel > level) continue;
            if (best == null || entry.FromLevel > best.FromLevel) best = entry;
        }
        return best;
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        var seen = new HashSet<int>();
        for (int i = 0; i < _levels.Count; i++)
        {
            LevelVisual entry = _levels[i];
            if (entry == null) continue;
            if (!seen.Add(entry.FromLevel))
                Debug.LogWarning($"[SkillVisualSet] {name}: FromLevel {entry.FromLevel}이 중복입니다.", this);
            if (entry.Prefab == null)
                Debug.LogWarning($"[SkillVisualSet] {name}: FromLevel {entry.FromLevel} 엔트리의 Prefab이 비어 있습니다.", this);
        }
    }
#endif
}