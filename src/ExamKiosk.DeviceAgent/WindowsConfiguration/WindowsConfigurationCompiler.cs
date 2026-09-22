using ExamKiosk.Contracts;
using ExamKiosk.DeviceAgent.WindowsConfiguration.AssignedAccess;
using ExamKiosk.DeviceAgent.WindowsConfiguration.Edge;
using ExamKiosk.DeviceAgent.WindowsConfiguration.Models;

namespace ExamKiosk.DeviceAgent.WindowsConfiguration;

public sealed class WindowsConfigurationCompiler
{
    private readonly IReadOnlyList<IAssignedAccessXmlGenerator> generators =
        [new AssignedAccess2022XmlGenerator()];

    public EffectiveWindowsConfiguration Compile(
        WindowsClientVersion clientVersion,
        EffectiveExamProfile profile)
    {
        ArgumentNullException.ThrowIfNull(clientVersion);
        ArgumentNullException.ThrowIfNull(profile);

        var matchingGenerators = generators
            .Where(generator => generator.Supports(clientVersion))
            .ToArray();
        if (matchingGenerators.Length == 0)
        {
            throw new NotSupportedException(
                $"Windows version {FormatVersion(clientVersion)} does not have a supported Assigned Access schema.");
        }
        if (matchingGenerators.Length > 1)
        {
            throw new InvalidOperationException(
                $"Multiple Assigned Access generators support Windows version {FormatVersion(clientVersion)}.");
        }

        var assignedAccessConfiguration = matchingGenerators[0].Generate(
            clientVersion,
            profile.Student,
            profile.Exam,
            profile.Tools);
        var configuration = new EffectiveWindowsConfiguration(
            assignedAccessConfiguration.AssignedAccess,
            assignedAccessConfiguration.Shortcuts,
            EdgePolicyCompiler.Compile(profile.AllowedUrls));
        AssignedAccessArtifactValidator.Validate(profile, configuration);
        return configuration;
    }

    private static string FormatVersion(WindowsClientVersion version) =>
        $"{version.Major}.{version.Minor}.{version.Build}";
}
