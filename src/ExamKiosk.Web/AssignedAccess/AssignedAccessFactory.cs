using ExamKiosk.Contracts;
using ExamKiosk.Web.AssignedAccess.Generators;
using ExamKiosk.Web.AssignedAccess.Validation;

namespace ExamKiosk.Web.AssignedAccess;

public sealed class AssignedAccessFactory(
    IAssignedAccessConfigurationValidator configurationValidator,
    IEnumerable<IAssignedAccessXmlGenerator> generators)
    : IAssignedAccessFactory
{
    private readonly IReadOnlyList<IAssignedAccessXmlGenerator> generators =
        generators.ToArray();

    public EffectiveWindowsConfiguration Create(
        WindowsClientVersion clientVersion,
        EffectiveExam exam,
        IReadOnlyList<ToolDefinition> tools)
    {
        ArgumentNullException.ThrowIfNull(clientVersion);
        ArgumentNullException.ThrowIfNull(exam);
        ArgumentNullException.ThrowIfNull(tools);

        configurationValidator.Validate(exam, tools);

        var matchingGenerators = generators
            .Where(generator => generator.Supports(clientVersion))
            .ToArray();
        if (matchingGenerators.Length == 1)
        {
            return matchingGenerators[0].Generate(clientVersion, exam, tools);
        }

        if (matchingGenerators.Length > 1)
        {
            throw new InvalidOperationException(
                $"Multiple Assigned Access generators support Windows version "
                + $"{FormatVersion(clientVersion)}.");
        }

        throw new NotSupportedException(
            $"Windows version {FormatVersion(clientVersion)} "
            + "does not have a supported Assigned Access schema.");
    }

    private static string FormatVersion(WindowsClientVersion version) =>
        $"{version.Major}.{version.Minor}.{version.Build}";
}
