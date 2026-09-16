namespace ExamKiosk.Contracts;

public static class RestartSchedule
{
    public const int DelaySeconds = 5;

    public static int GetRemainingSeconds(
        DateTimeOffset restartAtUtc,
        DateTimeOffset currentUtc)
    {
        return Math.Max(
            0,
            (int)Math.Ceiling((restartAtUtc - currentUtc).TotalSeconds));
    }
}
