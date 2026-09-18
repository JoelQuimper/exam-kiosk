using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using ExamKiosk.Contracts;
using ExamKiosk.DeviceAgent.WindowsConfiguration;
using ExamKiosk.DeviceAgent.WindowsConfiguration.Models;

namespace ExamKiosk.DeviceAgent.WindowsConfiguration.AssignedAccess;

public static class AssignedAccessArtifactValidator
{
    public const int MaximumAssignedAccessXmlBytes = 128 * 1024;
    private const string ExamShortcutFileName = "exam.lnk";

    private static readonly XNamespace AssignedAccessNamespace =
        AssignedAccessProfileConventions.AssignedAccessNamespace;
    private static readonly XNamespace Rs5Namespace =
        AssignedAccessProfileConventions.Rs5Namespace;

    public static void Validate(
        EffectiveExamProfile profile,
        EffectiveWindowsConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(configuration);
        if (configuration.ClientVersion is null
            || configuration.AssignedAccess is null
            || configuration.Shortcuts is null)
        {
            throw new WindowsConfigurationException(
                "The generated Windows configuration is incomplete.");
        }

        ValidateArtifact(profile, configuration.AssignedAccess);
        ValidateShortcuts(configuration.Shortcuts);
    }

    private static void ValidateArtifact(
        EffectiveExamProfile profile,
        AssignedAccessArtifact artifact)
    {
        if (artifact.Format is null
            || artifact.SchemaVersion is null
            || artifact.ContentEncoding is null
            || artifact.Source is null
            || artifact.Source.Type is null
            || artifact.Sha256 is null
            || artifact.Xml is null)
        {
            throw new WindowsConfigurationException(
                "The Assigned Access artifact is incomplete.");
        }

        if (!string.Equals(
                artifact.Format,
                AssignedAccessProfileConventions.ArtifactFormat,
                StringComparison.Ordinal)
            || !string.Equals(
                artifact.SchemaVersion,
                AssignedAccessProfileConventions.ArtifactSchemaVersion,
                StringComparison.Ordinal)
            || !string.Equals(
                artifact.ContentEncoding,
                AssignedAccessProfileConventions.ContentEncoding,
                StringComparison.OrdinalIgnoreCase)
            || !string.Equals(
                artifact.Source.Type,
                AssignedAccessProfileConventions.ArtifactSourceType,
                StringComparison.Ordinal)
            || artifact.Source.GeneratorVersion
                != AssignedAccessProfileConventions.ArtifactGeneratorVersion)
        {
            throw new WindowsConfigurationException(
                "The Assigned Access artifact metadata is not supported.");
        }

        var xmlBytes = Encoding.UTF8.GetBytes(artifact.Xml);
        if (xmlBytes.Length is 0 or > MaximumAssignedAccessXmlBytes)
        {
            throw new WindowsConfigurationException(
                "The Assigned Access XML size is invalid.");
        }

        ValidateSha256(artifact.Sha256, xmlBytes);
        var document = ParseXml(artifact.Xml);
        if (artifact.Xml.Contains(
                ExamShortcutFileName,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new WindowsConfigurationException(
                "The Assigned Access XML must not reference an exam shortcut.");
        }

        ValidateDocument(document, profile);
    }

    private static void ValidateSha256(
        string declaredSha256,
        byte[] xmlBytes)
    {
        if (declaredSha256.Length != 64
            || declaredSha256.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new WindowsConfigurationException(
                "The Assigned Access SHA-256 value is invalid.");
        }

        var declaredBytes = Convert.FromHexString(declaredSha256);
        var computedBytes = SHA256.HashData(xmlBytes);
        if (!CryptographicOperations.FixedTimeEquals(
                declaredBytes,
                computedBytes))
        {
            throw new WindowsConfigurationException(
                "The Assigned Access SHA-256 does not match the XML content.");
        }
    }

    private static XDocument ParseXml(string xml)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaximumAssignedAccessXmlBytes,
            MaxCharactersFromEntities = 0,
        };

        try
        {
            using var textReader = new StringReader(xml);
            using var xmlReader = XmlReader.Create(textReader, settings);
            return XDocument.Load(xmlReader, LoadOptions.PreserveWhitespace);
        }
        catch (XmlException exception)
        {
            throw new WindowsConfigurationException(
                "The Assigned Access XML is not a safe, well-formed document.",
                exception);
        }
    }

    private static void ValidateDocument(
        XDocument document,
        EffectiveExamProfile effectiveProfile)
    {
        var root = document.Root;
        if (root?.Name
            != AssignedAccessNamespace + "AssignedAccessConfiguration")
        {
            throw new WindowsConfigurationException(
                "The Assigned Access XML root element or namespace is invalid.");
        }

        var assignedAccessProfile = RequireSingle(
            root.Element(AssignedAccessNamespace + "Profiles")
                ?.Elements(AssignedAccessNamespace + "Profile"),
            "The Assigned Access XML must contain exactly one profile.");
        ValidateProfileIdentity(assignedAccessProfile, effectiveProfile);
        ValidateAutoLaunch(assignedAccessProfile);

        var defaultProfile = RequireSingle(
            root.Element(AssignedAccessNamespace + "Configs")
                ?.Elements(AssignedAccessNamespace + "Config")
                .SelectMany(
                    config => config.Elements(
                        AssignedAccessNamespace + "DefaultProfile")),
            "The Assigned Access XML must contain exactly one default profile.");
        if (!string.Equals(
                (string?)defaultProfile.Attribute("Id"),
                AssignedAccessProfileConventions.ProfileId,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new WindowsConfigurationException(
                "The default Assigned Access profile ID is invalid.");
        }
    }

    private static void ValidateProfileIdentity(
        XElement assignedAccessProfile,
        EffectiveExamProfile effectiveProfile)
    {
        if (!string.Equals(
                (string?)assignedAccessProfile.Attribute("Id"),
                AssignedAccessProfileConventions.ProfileId,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new WindowsConfigurationException(
                "The Assigned Access profile ID is invalid.");
        }

        var expectedName = AssignedAccessProfileConventions.CreateProfileName(
            effectiveProfile.Student,
            effectiveProfile.Exam);
        if (!string.Equals(
                (string?)assignedAccessProfile.Attribute("Name"),
                expectedName,
                StringComparison.Ordinal))
        {
            throw new WindowsConfigurationException(
                "The Assigned Access profile name does not match the effective profile.");
        }
    }

    private static void ValidateAutoLaunch(XElement assignedAccessProfile)
    {
        var autoLaunchApps = assignedAccessProfile
            .Descendants(AssignedAccessNamespace + "App")
            .Where(
                app => string.Equals(
                    (string?)app.Attribute(Rs5Namespace + "AutoLaunch"),
                    "true",
                    StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (autoLaunchApps.Length != 1
            || !string.Equals(
                (string?)autoLaunchApps[0].Attribute("DesktopAppPath"),
                AssignedAccessProfileConventions.RestrictedClientPath,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new WindowsConfigurationException(
                "Restricted Client must be the only auto-launched application.");
        }
    }

    private static void ValidateShortcuts(
        IReadOnlyList<WindowsShortcutArtifact> shortcuts)
    {
        if (shortcuts.Any(
                shortcut => shortcut is null
                    || string.Equals(
                        shortcut.ShortcutId,
                        "exam",
                        StringComparison.OrdinalIgnoreCase)
                    || string.Equals(
                        Path.GetFileName(shortcut.LinkPath),
                        ExamShortcutFileName,
                        StringComparison.OrdinalIgnoreCase)))
        {
            throw new WindowsConfigurationException(
                "The exam must be opened by Restricted Client, not by an exam shortcut.");
        }
    }

    private static XElement RequireSingle(
        IEnumerable<XElement>? elements,
        string errorMessage)
    {
        var matches = elements?.Take(2).ToArray() ?? [];
        if (matches.Length != 1)
        {
            throw new WindowsConfigurationException(errorMessage);
        }

        return matches[0];
    }
}
