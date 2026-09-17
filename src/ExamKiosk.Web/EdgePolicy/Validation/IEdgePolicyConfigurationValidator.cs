using ExamKiosk.Contracts;

namespace ExamKiosk.Web.EdgePolicy.Validation;

public interface IEdgePolicyConfigurationValidator
{
    void Validate(IReadOnlyList<WebToolDefinition> tools);
}
