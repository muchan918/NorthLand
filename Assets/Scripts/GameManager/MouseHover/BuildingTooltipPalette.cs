using UnityEngine;

/// 건물 타입(Production/General/Skill/Store/Castle)별 툴팁 색을 담는 팔레트 에셋.
/// BuildingTooltipSource가 참조해 BuildingType → (헤더색, 배경색)을 룩업한다.
/// 색 튜닝을 코드 수정 없이 인스펙터에서 하기 위해 SO로 분리했다.
[CreateAssetMenu(fileName = "BuildingTooltipPalette", menuName = "Scriptable Objects/BuildingTooltipPalette")]
public class BuildingTooltipPalette : ScriptableObject
{
    [System.Serializable]
    public struct Entry
    {
        public BuildingType Type;
        public Color HeaderColor;
        public Color BackgroundColor;
    }

    [SerializeField] Entry[] _entries;

    [Header("타입 미등록 시 사용할 기본색")]
    [SerializeField] Color _fallbackHeaderColor = new(0.20f, 0.20f, 0.20f, 0.95f);
    [SerializeField] Color _fallbackBackgroundColor = new(0.10f, 0.10f, 0.10f, 0.95f);

    public void Resolve(BuildingType type, out Color headerColor, out Color backgroundColor)
    {
        if (_entries != null)
        {
            foreach (var e in _entries)
            {
                if (e.Type == type)
                {
                    headerColor = e.HeaderColor;
                    backgroundColor = e.BackgroundColor;
                    return;
                }
            }
        }

        headerColor = _fallbackHeaderColor;
        backgroundColor = _fallbackBackgroundColor;
    }
}
