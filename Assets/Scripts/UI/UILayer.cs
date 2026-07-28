/// <summary>
/// UI 루트 Canvas의 표준 <c>sortingOrder</c> 값(Docs/Core/UIZOrder.md §3의 코드 측 단일 소스).
/// 새 루트 Canvas를 만들 때 임의의 큰 숫자를 쓰지 말고 이 중 하나를 쓴다. 새 범주가 필요하면
/// UIZOrder.md §3 표를 먼저 갱신하고 여기에 상수를 추가한다.
/// </summary>
public static class UILayer
{
    /// <summary>일반 HUD(UICanvas) — 미니맵, 관리·타워·스킬·정보 패널, 호버 툴팁.</summary>
    public const int Hud = 100;

    /// <summary>상위 모달(RewardCanvas) — 보상 선택.</summary>
    public const int Modal = 500;

    /// <summary>최상위 모달(ResultCanvas) — 게임오버·승리 결과.</summary>
    public const int Result = 900;
}