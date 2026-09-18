namespace ExamKiosk.DeviceAgent.WindowsConfiguration;

public sealed class WindowsConfigurationException : Exception
{
    public WindowsConfigurationException(string message)
        : base(message)
    {
    }

    public WindowsConfigurationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
