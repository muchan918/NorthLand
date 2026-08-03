using System;

namespace NorthLand.Core
{
    /// <summary>
    /// 플레이어의 전체 저장 데이터.
    /// </summary>
    [Serializable]
    public sealed class PlayerData
    {
        public int SaveVersion = 1;

        public RunData CurrentRun = new RunData();
    }

    /// <summary>
    /// 현재 진행 중인 한 판의 저장 데이터.
    /// </summary>
    [Serializable]
    public sealed class RunData
    {
        public bool HasActiveRun;

        public RunSeedData SeedData = new RunSeedData();

        // 진행 저장 기능을 붙일 때 사용한다.
        public int CurrentDay = 1;
        public int CurrentWave;
    }
}