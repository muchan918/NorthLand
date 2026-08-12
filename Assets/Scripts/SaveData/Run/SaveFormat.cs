namespace NorthLand.Core
{
    /// <summary>세이브 파일 포맷의 현재 버전과 지원 범위.</summary>
    public static class SaveFormat
    {
        public const int OldestSupportedVersion = 1;

        // v3(#337): ResourceKind에서 특수 자원 4종(Gold~Diamond = 값 4~7)이 삭제됐다.
        // ⚠ 스키마를 바꾸는 PR은 반드시 이 값을 올린다(WL-173). 올리지 않으면 구 세이브가
        //   버전 게이트와 역직렬화를 통과해 복원 도중에 실패하고, 그 경로가 세이브 파일을
        //   삭제한다(RunSaveManager.Persistence.cs) — 거절이 아니라 데이터 손실이다(WL-167).
        public const int CurrentVersion = 3;
    }
}
