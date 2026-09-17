using ExamKiosk.Contracts;

namespace ExamKiosk.Web.AssignedAccess.Validation;

public interface IAssignedAccessConfigurationValidator
{
    void Validate(
        EffectiveExam exam,
        IReadOnlyList<ToolDefinition> tools);
}
