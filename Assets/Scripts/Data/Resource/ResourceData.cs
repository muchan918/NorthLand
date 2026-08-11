// 게임에 존재하는 자원은 이 4종뿐이다(#337 — 영토 시스템과 함께 특수 자원 금/루비/사파이어/다이아 제거).
// 직렬화된 enum 인덱스가 밀리지 않도록 기존 값(0~3)의 순서는 바꾸지 말고, 추가는 항상 뒤에 append 한다.
public enum ResourceKind
{
    Wood,
    Iron,
    Food,
    Mana,
}

public class ResourceData
{
    public string ResourceID { get; set; }
    public string NameKey { get; set; }
    public ResourceKind Kind { get; set; }
}
