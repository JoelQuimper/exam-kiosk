namespace ExamKiosk.Contracts;

public sealed record EffectiveExam(
    string Id,
    string Title,
    string Icon,
    Uri ExamTarget);
