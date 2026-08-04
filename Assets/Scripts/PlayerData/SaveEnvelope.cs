using System;

namespace NorthLand.Core
{
    /// <summary>
    /// 저장 파일의 버전과 한 판 데이터를 분리하는 봉투.
    /// </summary>
    [Serializable]
    public sealed class SaveEnvelope
    {
        public int Version = 1;

        public RunData Data = new RunData();
    }
}