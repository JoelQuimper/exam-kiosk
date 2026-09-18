using System.Security.Cryptography;
using System.Text;
using ExamKiosk.Contracts;
using ExamKiosk.WindowsConfiguration.Models;

namespace ExamKiosk.WindowsConfiguration.Tests;

internal static class WindowsConfigurationTestData
{
    internal static EffectiveExamProfile CreateProfile() =>
        new(
            1,
            "assignment-1",
            new EffectiveStudent("student@example.com"),
            new EffectiveExam(
                "exam-1",
                "Mathematics",
                "calculator",
                new Uri("https://example.com/exam"),
                new WebLaunchTarget(
                    new Uri("https://example.com/exam"),
                    "Open exam",
                    true,
                    true)),
            [],
            new EffectiveEdgePolicy(["*"], ["https://example.com"]));

    internal static WindowsClientVersion SupportedVersion() =>
        new(10, 0, 22621);

    internal static EffectiveWindowsConfiguration WithXml(
        EffectiveWindowsConfiguration configuration,
        string xml) =>
        configuration with
        {
            AssignedAccess = configuration.AssignedAccess with
            {
                Xml = xml,
                Sha256 = Convert.ToHexStringLower(
                    SHA256.HashData(Encoding.UTF8.GetBytes(xml))),
            },
        };
}
