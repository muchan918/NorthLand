using System;
using System.IO;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace NorthLand.Core
{
    /// <summary>
    /// 단일 Run 세이브 파일의 경로와 파일 IO를 담당한다.
    /// JSON 변환이나 게임 상태 수집·복원은 담당하지 않는다.
    /// </summary>
    public sealed class SaveFileStore
    {
        private const string DefaultSaveFileName = "run-save.json";

        public string SavePath { get; }

        public bool Exists => File.Exists(SavePath);

        public SaveFileStore(string directoryPath)
        : this(directoryPath, DefaultSaveFileName)
        {
        }

        public SaveFileStore(string directoryPath,string saveFileName)
        {
            if (string.IsNullOrWhiteSpace(directoryPath))
            {
                throw new ArgumentException("세이브 디렉터리 경로가 비어 있습니다.",nameof(directoryPath));
            }

            if (string.IsNullOrWhiteSpace(saveFileName))
            {
                throw new ArgumentException("세이브 파일 이름이 비어 있습니다.",nameof(saveFileName));
            }

            SavePath = Path.Combine(directoryPath,saveFileName);
        }


        /// <summary>
        /// 저장 파일의 JSON 문자열을 읽는다.
        /// 파일이 없거나 읽기에 실패하면 false를 반환한다.
        /// </summary>
        public bool TryRead(out string json, out string error)
        {
            json = null;
            error = null;

            if (!Exists)
            {
                error = "세이브 파일이 없습니다.";
                return false;
            }

            try
            {
                json = File.ReadAllText(SavePath);
                return true;
            }
            catch (Exception exception)
            {
                error = $"세이브 파일을 읽을 수 없습니다: {exception.Message}";

                return false;
            }
        }
        /// <summary>
        /// JSON을 임시 파일에 먼저 기록한 뒤 실제 세이브 파일로 교체한다.
        /// 기록 도중 실패하면 기존 세이브 파일은 유지한다.
        /// </summary>
        public bool TryWrite(string json, out string error)
        {
            error = null;

            if (string.IsNullOrWhiteSpace(json))
            {
                error = "저장할 JSON이 비어 있습니다.";
                return false;
            }

            string directoryPath = Path.GetDirectoryName(SavePath);

            string temporaryPath = SavePath + ".tmp";

            try
            {
                Directory.CreateDirectory(directoryPath);

                File.WriteAllText(temporaryPath,json,new UTF8Encoding(false));

                if (File.Exists(SavePath))
                {
                    File.Replace(temporaryPath,SavePath,null);
                }
                else
                {
                    File.Move(temporaryPath,SavePath);
                }

                return true;
            }
            catch (Exception exception)
            {
                error = $"세이브 파일을 기록할 수 없습니다: {exception.Message}";

                try
                {
                    if (File.Exists(temporaryPath))
                        File.Delete(temporaryPath);
                }
                catch (Exception cleanupException)
                {
                    error += $" 임시 파일 정리도 실패했습니다: {cleanupException.Message}";
                }

                return false;
            }
        }

        public async UniTask<SaveResult> WriteAsync(string json,CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return SaveResult.Failed("저장할 JSON이 비어 있습니다.");
            }

            try
            {
                return await UniTask.RunOnThreadPool(() =>
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        if (!TryWrite(json, out string error))
                        {
                            return SaveResult.Failed(error);
                        }

                        return SaveResult.Succeeded();
                    },
                    cancellationToken: cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
        }

        /// <summary>
        /// 현재 Run 세이브와 남아 있는 임시 파일을 삭제한다.
        /// 파일이 이미 없어도 성공으로 처리한다.
        /// </summary>
        public bool TryDelete(out string error)
        {
            error = null;

            string temporaryPath = SavePath + ".tmp";

            try
            {
                if (File.Exists(SavePath))
                    File.Delete(SavePath);

                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);

                return true;
            }
            catch (Exception exception)
            {
                error = $"세이브 파일을 삭제할 수 없습니다: {exception.Message}";

                return false;
            }
        }
    }
}