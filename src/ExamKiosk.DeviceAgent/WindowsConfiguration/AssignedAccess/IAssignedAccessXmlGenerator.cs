using ExamKiosk.Contracts;
using ExamKiosk.DeviceAgent.WindowsConfiguration.AssignedAccess.Models;
using ExamKiosk.DeviceAgent.WindowsConfiguration.Models;

namespace ExamKiosk.DeviceAgent.WindowsConfiguration.AssignedAccess;

internal interface IAssignedAccessXmlGenerator
{
    bool Supports(WindowsClientVersion clientVersion);

    AssignedAccessConfiguration Generate(
        WindowsClientVersion clientVersion,
        EffectiveStudent student,
        EffectiveExam exam,
        IReadOnlyList<ToolDefinition> tools);
}
