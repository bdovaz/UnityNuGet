namespace UnityNuGet
{
    public class RegistryLogEventArgs
    {
        public string Message { get; }

        public RegistryLogEventArgs(string message)
        {
            Message = message;
        }
    }
}
