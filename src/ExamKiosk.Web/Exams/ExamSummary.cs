namespace ExamKiosk.Web.Exams;

public sealed record ExamSummary(
    string Title,
    string Course,
    string Description,
    int DurationMinutes,
    string AvailabilityLabel,
    IReadOnlyList<string> AllowedTools,
    string Status);
