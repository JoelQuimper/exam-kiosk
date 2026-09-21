using ExamKiosk.Contracts;

namespace ExamKiosk.DeviceAgent.Tests;

internal static class EffectiveProfileTestData
{
    internal static EffectiveExamProfile Create(
        IReadOnlyList<ToolDefinition>? tools = null)
    {
        return new(
            1,
            "assignment-1",
            new EffectiveStudent("student@example.com"),
            new EffectiveExam(
                "exam-1",
                "Mathematics 101",
                "calculator",
                new Uri("https://example.com/exam")),
            tools ?? [],
            ["https://example.com"]);
    }
}
