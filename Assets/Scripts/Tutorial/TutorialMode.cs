// 게임 씬을 튜토리얼로 시작했는지 여부.
// 씬 로드를 넘어 유지되어야 하므로 static이다 — 튜토리얼이 끝나면 Exit() 후 게임 씬을 재로드해
// 같은 씬을 정상 게임으로 다시 시작한다.
//
// 정식 진입은 씬 로드 전에 Enter한다. 에디터 작업 씬의 직접 실행은 실행 순서가 가장 빠른
// TutorialController가 startOnPlay를 보고 Enter한다. 소비 시스템은 이 값만 읽는다.
// MonsterSpawnWaveProvider.forceTutorialWaves는 웨이브 구성만 바꾸는 별도 테스트 옵션이다.
public static class TutorialMode
{
    public const int MasterSeed = 15416;
    public const float EnemyHpScale = 0.5f;
    public const float SkillCooldownSeconds = 3f;
    public const int InitialBiscuit = 20;

    public static bool IsActive { get; private set; }

    // Enter Play Mode Options에서 Domain Reload를 끈 경우에도 직전 실행의 모드가 남지 않게 한다.
    [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset() => IsActive = false;

    public static void Enter()
    {
        IsActive = true;
    }

    public static void Exit()
    {
        IsActive = false;
    }
}
