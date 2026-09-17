namespace ExamKiosk.Web.ExamAssignments.Models;

internal sealed record StudentProfile(
    string StudentId,
    string UserPrincipalName,
    string DisplayName);
