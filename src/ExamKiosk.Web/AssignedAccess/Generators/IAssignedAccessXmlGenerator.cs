using ExamKiosk.Contracts;

namespace ExamKiosk.Web.AssignedAccess.Generators;

public interface IAssignedAccessXmlGenerator
{
    bool Supports(WindowsClientVersion clientVersion);

    EffectiveWindowsConfiguration Generate(
        WindowsClientVersion clientVersion,
        EffectiveExam exam,
        IReadOnlyList<ToolDefinition> tools);
}
