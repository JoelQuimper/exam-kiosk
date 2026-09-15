namespace ExamKiosk.Web.Exams;

public sealed class ExamCatalog : IExamCatalog
{
    private static readonly IReadOnlyList<ExamSummary> AssignedExams =
    [
        new(
            "Bogus exam",
            "Prototype assessment",
            "The initial Exam Kiosk assessment used to validate the secure exam experience.",
            60,
            "Available now",
            ["Microsoft Edge", "Calculator"],
            "Ready"),
    ];

    public IReadOnlyList<ExamSummary> GetAssignedExams() => AssignedExams;
}
