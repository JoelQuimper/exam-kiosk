using ExamKiosk.Contracts;

namespace ExamKiosk.DeviceAgent.Tests;

internal static class EffectiveProfileTestData
{
    internal static EffectiveExamProfile Create() =>
        new(
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
            new EffectiveEdgePolicy(["*"], ["https://example.com"]),
            new EffectiveWindowsConfiguration(
                new WindowsClientVersion(10, 0, 22621),
                new AssignedAccessArtifact(
                    "windowsAssignedAccessXml",
                    "2022",
                    new AssignedAccessSource("generated", 1),
                    "utf-8",
                    "digest",
                    "<AssignedAccessConfiguration />"),
                []));
}
