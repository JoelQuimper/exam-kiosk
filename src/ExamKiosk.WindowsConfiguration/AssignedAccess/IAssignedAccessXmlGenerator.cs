using ExamKiosk.Contracts;
using ExamKiosk.WindowsConfiguration.Models;

namespace ExamKiosk.WindowsConfiguration.AssignedAccess;

internal interface IAssignedAccessXmlGenerator
{
    bool Supports(WindowsClientVersion clientVersion);

    EffectiveWindowsConfiguration Generate(
        WindowsClientVersion clientVersion,
        EffectiveStudent student,
        EffectiveExam exam,
        IReadOnlyList<ToolDefinition> tools);
}
