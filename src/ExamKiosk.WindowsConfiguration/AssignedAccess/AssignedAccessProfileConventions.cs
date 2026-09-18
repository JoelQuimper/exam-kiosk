using ExamKiosk.Contracts;
using ExamKiosk.ProfileValidation;

namespace ExamKiosk.WindowsConfiguration.AssignedAccess;

public static class AssignedAccessProfileConventions
{
    public const string ArtifactFormat = "windowsAssignedAccessXml";
    public const string ArtifactSchemaVersion = "2022";
    public const string ArtifactSourceType = "generated";
    public const int ArtifactGeneratorVersion = 1;
    public const string ContentEncoding = "utf-8";
    public const string ProfileId = "{9A2A490F-10F6-4764-974A-43B19E722C23}";
    public const string AssignedAccessNamespace =
        "http://schemas.microsoft.com/AssignedAccess/2017/config";
    public const string Rs5Namespace =
        "http://schemas.microsoft.com/AssignedAccess/201810/config";
    public const string RestrictedClientPath =
        @"%ProgramFiles%\ExamKiosk\RestrictedClient\ExamKiosk.RestrictedClient.exe";

    public static string CreateProfileName(
        EffectiveStudent student,
        EffectiveExam exam)
    {
        ArgumentNullException.ThrowIfNull(student);
        ArgumentNullException.ThrowIfNull(exam);

        if (string.IsNullOrWhiteSpace(student.UserPrincipalName))
        {
            throw new ProfileValidationException(
                "The student user principal name is required.");
        }

        var separatorIndex = student.UserPrincipalName.IndexOf('@');
        if (separatorIndex <= 0)
        {
            throw new ProfileValidationException(
                "The student user principal name does not contain an alias.");
        }

        return $"EXAM — {student.UserPrincipalName[..separatorIndex]} — {exam.Id}";
    }
}
