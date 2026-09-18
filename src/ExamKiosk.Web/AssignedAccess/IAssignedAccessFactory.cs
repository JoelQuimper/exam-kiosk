using ExamKiosk.Contracts;

namespace ExamKiosk.Web.AssignedAccess;

public interface IAssignedAccessFactory
{
    EffectiveWindowsConfiguration Create(
        WindowsClientVersion clientVersion,
        EffectiveStudent student,
        EffectiveExam exam,
        IReadOnlyList<ToolDefinition> tools);
}
