using ExamKiosk.Contracts;
using ExamKiosk.Web.ExamAssignments.Models;

namespace ExamKiosk.Web.ExamAssignments;

public interface IExamProfileOrchestrator
{
    EffectiveExamProfile Create(
        string userPrincipalName,
        AssignedExam assignment);
}
