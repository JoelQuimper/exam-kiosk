using System.Globalization;
using System.Resources;

namespace ExamKiosk.Contracts;

public static class Resources
{
    private static readonly ResourceManager ResourceManager = new(
        "ExamKiosk.Contracts.Resources",
        typeof(Resources).Assembly);

    public static string AgentUnreachableFinish => Get(nameof(AgentUnreachableFinish));
    public static string AgentUnreachableStart => Get(nameof(AgentUnreachableStart));
    public static string AvailableExams => Get(nameof(AvailableExams));
    public static string Connected => Get(nameof(Connected));
    public static string ExamDescription => Get(nameof(ExamDescription));
    public static string ExamDetails => Get(nameof(ExamDetails));
    public static string ExamDone => Get(nameof(ExamDone));
    public static string ExamFinishFailed => Get(nameof(ExamFinishFailed));
    public static string ExamInProgress => Get(nameof(ExamInProgress));
    public static string ExamOpened => Get(nameof(ExamOpened));
    public static string ExamTitle => Get(nameof(ExamTitle));
    public static string FinishWarning => Get(nameof(FinishWarning));
    public static string FinishWarningTitle => Get(nameof(FinishWarningTitle));
    public static string KioskLabel => Get(nameof(KioskLabel));
    public static string KioskTitle => Get(nameof(KioskTitle));
    public static string LeavingExam => Get(nameof(LeavingExam));
    public static string NoExamRunning => Get(nameof(NoExamRunning));
    public static string OpenExam => Get(nameof(OpenExam));
    public static string PreparingDevice => Get(nameof(PreparingDevice));
    public static string PreparationFailed => Get(nameof(PreparationFailed));
    public static string Ready => Get(nameof(Ready));
    public static string RestrictedSession => Get(nameof(RestrictedSession));
    public static string StartExam => Get(nameof(StartExam));
    public static string StartWarning => Get(nameof(StartWarning));
    public static string StartWarningTitle => Get(nameof(StartWarningTitle));
    public static string Instructions => Get(nameof(Instructions));
    public static string EdgeOpenFailed => Get(nameof(EdgeOpenFailed));
    public static string ExamSessionTitle => Get(nameof(ExamSessionTitle));

    private static string Get(string name)
    {
        return ResourceManager.GetString(name, CultureInfo.CurrentUICulture) ?? name;
    }
}
