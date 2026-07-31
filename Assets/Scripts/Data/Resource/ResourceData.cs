public enum ResourceKind
{
    Wood,
    Iron,
    Food,
    Mana,

    // 미개척 영지(영토 확장) 해금 자원 — 주민 배치 없이 매일 정산 시 자동 수급(#166, GDD §3.2).
    // 기존 값(0~3) 뒤에 append — 직렬화된 enum 인덱스가 밀리지 않도록 순서 변경 금지.
    Gold,
    Ruby,
    Sapphire,
    Diamond,
}

public class ResourceData
{
    public string ResourceID { get; set; }
    public string NameKey { get; set; }
    public ResourceKind Kind { get; set; }
}
