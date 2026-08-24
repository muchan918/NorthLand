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

    public readonly struct SaveResult<T>
    {
        public bool Success { get; }
        public T Value { get; }
        public string Error { get; }

        private SaveResult(bool success, T value, string error)
        {
            Success = success;
            Value = value;
            Error = error;
        }

        public static SaveResult<T> Succeeded(T value)
        {
            return new SaveResult<T>(true, value, null);
        }

        public static SaveResult<T> Failed(string error)
        {
            return new SaveResult<T>(false,default,error);
        }
    }
}