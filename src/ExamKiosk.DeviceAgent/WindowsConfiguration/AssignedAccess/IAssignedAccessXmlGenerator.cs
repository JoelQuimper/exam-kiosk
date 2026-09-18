using ExamKiosk.Contracts;
using ExamKiosk.DeviceAgent.WindowsConfiguration.Models;

namespace ExamKiosk.DeviceAgent.WindowsConfiguration.AssignedAccess;

internal interface IAssignedAccessXmlGenerator
{
    bool Supports(WindowsClientVersion clientVersion);

    EffectiveWindowsConfiguration Generate(
        WindowsClientVersion clientVersion,
        EffectiveStudent student,
        EffectiveExam exam,
        IReadOnlyList<ToolDefinition> tools);
}
