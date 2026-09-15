using Microsoft.Extensions.Localization;

namespace ExamKiosk.Web.Exams;

public sealed class ExamCatalog(IStringLocalizer<SharedResource> localizer) : IExamCatalog
{
    public IReadOnlyList<ExamSummary> GetAssignedExams() =>
    [
        new(
            localizer["BogusExamTitle"],
            localizer["PrototypeAssessment"],
            localizer["BogusExamDescription"],
            60,
            localizer["AvailableNow"],
            [localizer["MicrosoftEdge"], localizer["Calculator"]],
            localizer["Ready"]),
    ];
}
