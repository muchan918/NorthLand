using System;

namespace NorthLand.Core
{
    /// <summary>
    /// 현재 진행 중인 한 판의 전체 저장 데이터.
    /// 저장 항목은 기능을 구현할 때 영역별로 추가한다.
    /// </summary>
    [Serializable]
    public sealed class RunData
    {
        public RunSeedData SeedData = new RunSeedData();

    }
}