using System;

namespace NorthLand.Core
{
    /// <summary>
    /// 슬롯 도입 전 루트의 run-save.json을
    /// 현재 선택된 슬롯 폴더로 이전한다.
    /// </summary>
    public sealed class LegacyRunSaveLocationMigrator
    {
        private readonly SaveFileStore legacyStore;

        public LegacyRunSaveLocationMigrator(string legacySaveRootPath)
        {
            if (string.IsNullOrWhiteSpace(legacySaveRootPath))
            {
                throw new ArgumentException("기존 세이브 경로가 비어 있습니다.",nameof(legacySaveRootPath));
            }

            legacyStore = new SaveFileStore(legacySaveRootPath);
        }

        public bool TryMigrate(string targetSlotPath,out bool migrated,out string error)
        {
            migrated = false;
            error = null;

            if (string.IsNullOrWhiteSpace(targetSlotPath))
            {
                error = "이전할 슬롯 경로가 비어 있습니다.";
                return false;
            }

            // 기존 루트 세이브가 없으면 할 일이 없다.
            if (!legacyStore.Exists)
            {
                return true;
            }

            var targetStore =
                new SaveFileStore(targetSlotPath);

            // 슬롯의 새 저장 파일을 덮어쓰지 않는다.
            if (targetStore.Exists)
            {
                // 현재 슬롯의 저장을 우선하며 기존 파일은 보존한다.
                return true;
            }

            if (!legacyStore.TryRead(out string json,out error))
            {
                return false;
            }

            if (!targetStore.TryWrite(json, out error))
            {
                return false;
            }

            // 새 위치 저장이 성공한 뒤 기존 파일을 삭제한다.
            if (!legacyStore.TryDelete(out error))
            {
                return false;
            }

            migrated = true;
            return true;
        }
    }
}