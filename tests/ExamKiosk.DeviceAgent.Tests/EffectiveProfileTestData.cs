using ExamKiosk.Contracts;

namespace ExamKiosk.DeviceAgent.Tests;

internal static class EffectiveProfileTestData
{
    internal static EffectiveExamProfile Create()
    {
        return new(
            1,
            "assignment-1",
            new EffectiveStudent("student@example.com"),
            new EffectiveExam(
                "exam-1",
                "Mathematics 101",
                "calculator",
                new Uri("https://example.com/exam"),
                new WebLaunchTarget(
                    new Uri("https://example.com/exam"),
                    "Open exam",
                    true,
                    true)),
            [],
            new EffectiveEdgePolicy(["*"], ["https://example.com"]));
    }
}
