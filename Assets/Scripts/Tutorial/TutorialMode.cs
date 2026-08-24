// 게임 씬을 튜토리얼로 시작했는지 여부.
// 씬 로드를 넘어 유지되어야 하므로 static이다 — 튜토리얼이 끝나면 Exit() 후 게임 씬을 재로드해
// 같은 씬을 정상 게임으로 다시 시작한다.
//
// ⚠ MonoBehaviour의 Awake에서 세우면 안 된다. MonsterSpawnWaveProvider가 Awake에서 웨이브
//    순서를 확정하므로, 실행 순서에 따라 플래그를 읽기 전에 정상 웨이브로 굳어질 수 있다.
//    타이틀 화면처럼 '씬 로드 전'에 세우거나, 에디터 테스트용으로는
//    MonsterSpawnWaveProvider.forceTutorialMode 스위치를 쓴다.
public static class TutorialMode
{
    public static bool IsActive { get; private set; }

    public static void Enter()
    {
        IsActive = true;
    }

    public static void Exit()
    {
        IsActive = false;
    }
}
