using UnityEngine;

/// 툴팁에 표시할 순수 표시 데이터. 건물·버프 등 구체 개념은 전혀 모른다.
/// IHoverable.GetTooltipContent()가 호버 시점에 만들어 반환하고, TooltipUI가 그대로 그린다.
/// 헤더/본문 포맷과 색은 전적으로 공급자(소스)가 정한다 → 건물이든 버프든 같은 툴팁을 재사용.
public struct TooltipContent
{
    public string Header;          // 상단 헤더 (예: "나무꾼의 집 - 나무 생산")
    public string Body;            // 본문 (예: 설명, 버프 효과 텍스트)
    public Color HeaderColor;      // 헤더 영역 배경색
    public Color BackgroundColor;  // 본문 영역 배경색

    public TooltipContent(string header, string body, Color headerColor, Color backgroundColor)
    {
        Header = header;
        Body = body;
        HeaderColor = headerColor;
        BackgroundColor = backgroundColor;
    }
}
