namespace ExamKiosk.DeviceAgent.Tests;

internal sealed class TemporaryDirectory : IDisposable
{
    internal TemporaryDirectory()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "ExamKioskTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    internal string Path { get; }

    public void Dispose()
    {
        Directory.Delete(Path, true);
    }
}
