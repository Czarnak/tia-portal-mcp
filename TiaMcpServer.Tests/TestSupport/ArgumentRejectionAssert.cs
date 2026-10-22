using ModelContextProtocol.Protocol;
using Xunit;

namespace TiaMcpServer.Tests.TestSupport;

/// <summary>An argument-shape rejection is one canonical structured document with category validation_error.</summary>
internal static class ArgumentRejectionAssert
{
    public static void IsCanonicalValidationError(CallToolResult reply, string tool)
    {
        var text = string.Join(Environment.NewLine, reply.Content.OfType<TextContentBlock>().Select(block => block.Text));
        var violations = StructuredContractInspector.FindViolations(reply);
        Assert.True(violations.Count == 0, $"{tool}: {string.Join("; ", violations)} Response: {text}");
        Assert.True(reply.IsError == true, $"{tool}: expected isError. Response: {text}");
        var document = reply.StructuredContent!.Value;
        var failure = document.GetProperty(tool == "browse_project_tree" ? "failure" : "error");
        var category = failure.GetProperty("category").GetString();
        Assert.True(category == "validation_error", $"{tool}: expected validation_error, got {category}. Response: {text}");
    }
}
