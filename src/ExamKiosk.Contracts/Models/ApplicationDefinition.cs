using System.Text.Json.Serialization;

namespace ExamKiosk.Contracts;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(DesktopExecutableDefinition), "desktopExecutable")]
[JsonDerivedType(typeof(PackagedApplicationDefinition), "packagedApp")]
public abstract record ApplicationDefinition(
    string ApplicationId);
