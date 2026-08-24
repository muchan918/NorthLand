using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace NorthLand.Core
{
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

            if (!legacyStore.Exists)
            {
                return true;
            }

            var targetStore = new SaveFileStore(targetSlotPath);

            if (targetStore.Exists)
            {
                return true;
            }

            if (!legacyStore.TryRead(out string json,out error))
            {
                return false;
            }

            if (!targetStore.TryWrite(json,out error))
            {
                return false;
            }

            if (!legacyStore.TryDelete(out error))
            {
                return false;
            }

            migrated = true;
            return true;
        }

        public async UniTask<SaveResult<bool>> MigrateAsync(string targetSlotPath,CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(targetSlotPath))
            {
                return SaveResult<bool>.Failed("이전할 슬롯 경로가 비어 있습니다.");
            }

            cancellationToken.ThrowIfCancellationRequested();

            return await UniTask.RunOnThreadPool(() =>
                {
                    if (!TryMigrate(
                            targetSlotPath,
                            out bool migrated,
                            out string error))
                    {
                        return SaveResult<bool>.Failed(
                            error);
                    }

                    return SaveResult<bool>.Succeeded(
                        migrated);
                },
                cancellationToken:CancellationToken.None);
        }
    }
}