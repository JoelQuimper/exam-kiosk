namespace ExamKiosk.Contracts;

public sealed record ExamSession(
    Guid SessionId,
    ExamSessionState State,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ExpiresAtUtc,
    EffectiveExamProfile Profile);
