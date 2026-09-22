using ExamKiosk.Contracts;

namespace ExamKiosk.Web.Tests;

internal static class EffectiveProfileTestData
{
    internal static EffectiveExamProfile CreateProfile() =>
        new(
            1,
            "assignment-1",
            new EffectiveStudent("student@example.com"),
            new EffectiveExam(
                "exam-1",
                "Exam",
                "exam",
                new Uri("https://example.com/exam")),
            [],
            ["https://example.com"],
            []);
}
