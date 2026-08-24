namespace NorthLand.Core
{
    public readonly struct SaveResult
    {
        public bool Success { get; }
        public string Error { get; }

        private SaveResult(bool success, string error)
        {
            Success = success;
            Error = error;
        }

        public static SaveResult Succeeded()
        {
            return new SaveResult(true, null);
        }

        public static SaveResult Failed(string error)
        {
            return new SaveResult(false, error);
        }
    }
}