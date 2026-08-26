/// <summary>
/// UI 루트 Canvas의 표준 <c>sortingOrder</c> 값(Docs/Core/UIZOrder.md §3의 코드 측 단일 소스).
/// 새 루트 Canvas를 만들 때 임의의 큰 숫자를 쓰지 말고 이 중 하나를 쓴다. 새 범주가 필요하면
/// UIZOrder.md §3 표를 먼저 갱신하고 여기에 상수를 추가한다.
/// </summary>
public static class UILayer
{
    /// <summary>월드 오버레이(SelectionBoxView) — 드래그 선택 사각형. 입력을 받지 않으며 HUD 아래에 그린다.</summary>
    public const int SelectionBox = 50;

    /// <summary>일반 HUD(UICanvas) — 미니맵, 관리·타워·스킬·정보 패널, 호버 툴팁.</summary>
    public const int Hud = 100;

    /// <summary>상위 모달(RewardCanvas) — 보상 선택.</summary>
    public const int Modal = 500;

    /// <summary>튜토리얼 오버레이(TutorialCanvas) — 안내 팝업·말풍선. 보상 화면 위, 설정 화면 아래.</summary>
    public const int Tutorial = 600;

    /// <summary>최상위 모달(ResultCanvas) — 게임오버·승리 결과.</summary>
    public const int Result = 900;

    /// <summary>
    /// 로딩 커튼(LoadingScene의 Canvas) — 씬 전환을 덮는 커튼. **모든 게임 UI 위**여야 한다.
    ///
    /// LoadingScene은 GameScene을 Additive로 올린 채 살아 있고, Screen Space - Overlay 캔버스는
    /// 씬과 무관하게 sortingOrder로만 정렬된다. 그래서 이 값이 <see cref="Result"/>보다 낮으면
    /// 아직 커튼이 덮여 있어야 할 구간에 게임 씬 HUD가 커튼 위로 올라온다(#442-1 실측).
    /// </summary>
    public const int LoadingCurtain = 1000;
}