namespace ExamKiosk.Contracts;

public sealed record AssignedAccessArtifact(
    string Format,
    string SchemaVersion,
    AssignedAccessSource Source,
    string ContentEncoding,
    string Sha256,
    string Xml);
