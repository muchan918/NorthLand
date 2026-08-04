// CSV(TowerTable.csv) 한 행. **메타데이터만 담는다** — 공격력·사거리 같은 수치는 TowerAsset(SO)에 있다.
//
// #274: 구 TowerType/MagicEffectType enum이 여기 있었다. "이 타워가 무엇을 하는가"의 정본이
// 프리팹의 Tower.Actions 리스트로 옮겨가면서 삭제됐다 — CSV·SO 2중으로 존재하면서 런타임은 SO만 읽어
// 둘이 어긋나도 무증상이던 문제도 함께 사라졌다.
public class TowerData
{
    public string TowerID { get; set; }
    public string NameKey { get; set; }
    public int GridWidth { get; set; }
    public int GridHeight { get; set; }
    public string RoleKey { get; set; }
    public string DescriptionKey { get; set; }
}
