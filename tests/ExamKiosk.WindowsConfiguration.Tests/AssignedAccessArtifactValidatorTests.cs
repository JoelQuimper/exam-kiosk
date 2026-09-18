using ExamKiosk.ProfileValidation;
using ExamKiosk.WindowsConfiguration.AssignedAccess;
using ExamKiosk.WindowsConfiguration.Models;

namespace ExamKiosk.WindowsConfiguration.Tests;

public sealed class AssignedAccessArtifactValidatorTests
{
    private readonly WindowsConfigurationCompiler compiler = new();

    [Fact]
    public void Validate_WhenSha256DoesNotMatch_Throws()
    {
        var profile = WindowsConfigurationTestData.CreateProfile();
        var configuration = compiler.Compile(
            WindowsConfigurationTestData.SupportedVersion(),
            profile);
        configuration = configuration with
        {
            AssignedAccess = configuration.AssignedAccess with
            {
                Sha256 = new string('0', 64),
            },
        };

        var exception = Assert.Throws<ProfileValidationException>(
            () => AssignedAccessArtifactValidator.Validate(
                profile,
                configuration));

        Assert.Contains("does not match", exception.Message);
    }

    [Fact]
    public void Validate_WhenDocumentContainsDtd_Throws()
    {
        var profile = WindowsConfigurationTestData.CreateProfile();
        var configuration = compiler.Compile(
            WindowsConfigurationTestData.SupportedVersion(),
            profile);
        var xml = configuration.AssignedAccess.Xml.Replace(
            "<AssignedAccessConfiguration",
            "<!DOCTYPE foo [<!ENTITY xxe SYSTEM \"file:///etc/passwd\">]>\n"
                + "<AssignedAccessConfiguration",
            StringComparison.Ordinal);
        configuration = WindowsConfigurationTestData.WithXml(
            configuration,
            xml);

        var exception = Assert.Throws<ProfileValidationException>(
            () => AssignedAccessArtifactValidator.Validate(
                profile,
                configuration));

        Assert.Contains("safe, well-formed", exception.Message);
    }

    [Fact]
    public void Validate_WhenXmlReferencesExamShortcut_Throws()
    {
        var profile = WindowsConfigurationTestData.CreateProfile();
        var configuration = compiler.Compile(
            WindowsConfigurationTestData.SupportedVersion(),
            profile);
        var xml = configuration.AssignedAccess.Xml.Replace(
            "</Profile>",
            "<!-- exam.lnk --></Profile>",
            StringComparison.Ordinal);
        configuration = WindowsConfigurationTestData.WithXml(
            configuration,
            xml);

        var exception = Assert.Throws<ProfileValidationException>(
            () => AssignedAccessArtifactValidator.Validate(
                profile,
                configuration));

        Assert.Contains("must not reference", exception.Message);
    }
}
