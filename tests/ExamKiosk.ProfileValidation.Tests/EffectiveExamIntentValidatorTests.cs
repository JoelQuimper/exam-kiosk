using ExamKiosk.Contracts;

namespace ExamKiosk.ProfileValidation.Tests;

public sealed class EffectiveExamIntentValidatorTests
{
    [Fact]
    public void Validate_WithValidIntent_Succeeds()
    {
        EffectiveExamIntentValidator.Validate(CreateProfile());
    }

    [Fact]
    public void Validate_WhenUpnHasNoAlias_Throws()
    {
        var profile = CreateProfile() with
        {
            Student = new EffectiveStudent("invalid"),
        };

        var exception = Assert.Throws<ProfileValidationException>(
            () => EffectiveExamIntentValidator.Validate(profile));

        Assert.Contains("user principal name", exception.Message);
    }

    [Fact]
    public void Validate_WhenExamUrlsDiffer_Throws()
    {
        var profile = CreateProfile();
        profile = profile with
        {
            Exam = profile.Exam with
            {
                LaunchTarget = profile.Exam.LaunchTarget with
                {
                    EntryUrl = new Uri("https://example.com/other"),
                },
            },
        };

        var exception = Assert.Throws<ProfileValidationException>(
            () => EffectiveExamIntentValidator.Validate(profile));

        Assert.Contains("does not match", exception.Message);
    }

    [Fact]
    public void Validate_WhenToolIdsAreDuplicated_Throws()
    {
        var tool = CreateWebTool();
        var profile = CreateProfile() with { Tools = [tool, tool] };

        var exception = Assert.Throws<ProfileValidationException>(
            () => EffectiveExamIntentValidator.Validate(profile));

        Assert.Contains("duplicate tool IDs", exception.Message);
    }

    [Fact]
    public void Validate_WhenEdgePolicyOmitsToolDestination_Throws()
    {
        var profile = CreateProfile() with
        {
            Tools = [CreateWebTool()],
        };

        var exception = Assert.Throws<ProfileValidationException>(
            () => EffectiveExamIntentValidator.Validate(profile));

        Assert.Contains("omits a Web tool destination", exception.Message);
    }

    [Fact]
    public void Validate_WithMoreThan128AggregatedToolDestinations_Succeeds()
    {
        var tools = Enumerable.Range(0, 5)
            .Select(index => CreateWebTool(index, 32))
            .Cast<ToolDefinition>()
            .ToArray();
        var allowlist = tools
            .OfType<WebToolDefinition>()
            .SelectMany(tool => tool.Configuration.EdgeAllowlist)
            .ToArray();
        var profile = CreateProfile(
            tools,
            new EffectiveEdgePolicy(["*"], allowlist));

        EffectiveExamIntentValidator.Validate(profile);
    }

    [Fact]
    public void Validate_WhenWebToolHasMoreThan32Destinations_Throws()
    {
        var tool = CreateWebTool(0, 33);
        var profile = CreateProfile(
            [tool],
            new EffectiveEdgePolicy(["*"], tool.Configuration.EdgeAllowlist));

        var exception = Assert.Throws<ProfileValidationException>(
            () => EffectiveExamIntentValidator.Validate(profile));

        Assert.Contains("bounded Edge allowlist", exception.Message);
    }

    internal static EffectiveExamProfile CreateProfile(
        IReadOnlyList<ToolDefinition>? tools = null,
        EffectiveEdgePolicy? edgePolicy = null) =>
        new(
            1,
            "assignment-1",
            new EffectiveStudent("student@example.com"),
            new EffectiveExam(
                "exam-1",
                "Mathematics",
                "calculator",
                new Uri("https://example.com/exam"),
                new WebLaunchTarget(
                    new Uri("https://example.com/exam"),
                    "Open exam",
                    true,
                    true)),
            tools ?? [],
            edgePolicy
                ?? new EffectiveEdgePolicy(
                    ["*"],
                    ["https://example.com"]));

    internal static WebToolDefinition CreateWebTool() =>
        new(
            "dictionary",
            "Dictionary",
            "dictionary",
            true,
            new WebToolConfiguration(
                ["https://.dictionary.example"],
                new WebLaunchTarget(
                    new Uri("https://dictionary.example/"),
                    "Dictionary",
                    true,
                    true)));

    private static WebToolDefinition CreateWebTool(
        int toolIndex,
        int destinationCount)
    {
        var host = $"tool-{toolIndex}.example";
        var destinations = Enumerable.Range(0, destinationCount - 1)
            .Select(index => $"https://destination-{toolIndex}-{index}.example")
            .Prepend($"https://.{host}")
            .ToArray();

        return new WebToolDefinition(
            $"tool-{toolIndex}",
            $"Tool {toolIndex}",
            "tool",
            true,
            new WebToolConfiguration(
                destinations,
                new WebLaunchTarget(
                    new Uri($"https://{host}/"),
                    $"Tool {toolIndex}",
                    true,
                    true)));
    }
}
