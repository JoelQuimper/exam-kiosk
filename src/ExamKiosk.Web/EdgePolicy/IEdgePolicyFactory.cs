using ExamKiosk.Contracts;

namespace ExamKiosk.Web.EdgePolicy;

public interface IEdgePolicyFactory
{
    EffectiveEdgePolicy Create(IReadOnlyList<ToolDefinition> tools);
}
