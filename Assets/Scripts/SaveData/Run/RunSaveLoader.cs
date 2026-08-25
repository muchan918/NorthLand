using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace NorthLand.Core
{
    /// <summary>
    /// 슬롯 경로의 Run 세이브 파일을 읽고 현재 포맷의 데이터로 변환한다.
    /// </summary>
    public sealed class RunSaveLoader
    {
        private readonly SaveSerializer serializer;

        public RunSaveLoader()
        {
            serializer = new SaveSerializer();
        }

        public async UniTask<SaveResult<RunData>> LoadAsync(
            string slotPath,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(slotPath))
            {
                return SaveResult<RunData>.Failed("Run 세이브 슬롯 경로가 비어 있습니다.");
            }

            cancellationToken.ThrowIfCancellationRequested();

            SaveResult<string> readResult;

            try
            {
                var fileStore = new SaveFileStore(slotPath);
                readResult = await fileStore.ReadAsync(cancellationToken);
            }
            catch (ArgumentException exception)
            {
                return SaveResult<RunData>.Failed(exception.Message);
            }

            if (!readResult.Success)
            {
                return SaveResult<RunData>.Failed(readResult.Error);
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (!serializer.TryDeserialize(
                    readResult.Value,
                    out RunData data,
                    out string error))
            {
                return SaveResult<RunData>.Failed(error);
            }

            return SaveResult<RunData>.Succeeded(data);
        }
    }
}
