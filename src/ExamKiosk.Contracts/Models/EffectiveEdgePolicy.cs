namespace ExamKiosk.Contracts;

public sealed record EffectiveEdgePolicy(
    IReadOnlyList<string> UrlBlocklist,
    IReadOnlyList<string> UrlAllowlist);
