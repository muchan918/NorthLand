using System;

namespace NorthLand.Core
{
    /// <summary>
    /// 한 판에서 사용하는 시드 저장 데이터.
    /// RunData에 포함되어 세이브/로드된다.
    /// </summary>
    [Serializable]
    public sealed class RunSeedData
    {
        // 파생 알고리즘 변경 여부를 구분한다.
        public int SeedVersion = 1;

        // 플레이어에게 표시하고 공유하는 대표 시드.
        public int MasterSeed;

        // 전투맵 파생 요청 시드와 실제 성공 시드.
        public int CombatMapRequestedSeed;
        public int CombatMapUsedSeed;
    }
}