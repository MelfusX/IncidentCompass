using IncidentCompass.Application.Governance.Validation;
using IncidentCompass.Application.Governance.Tools;
using IncidentCompass.Domain.Governance;
using System.Text.Json;
using IncidentCompass.Application.Core.ModelClients;

namespace IncidentCompass.UnitTests;

public sealed class AgentToolPolicyTests
{
    [Theory]
    [InlineData("GetCurrentUserProfile", "Allowed", false, true)]
    [InlineData("CreateSupportTicket", "Allowed", false, true)]
    [InlineData("DraftEmail", "RequiresApproval", true, true)]
    public void Decide_ClassifiesRegisteredToolRiskFromToolMetadata(
        string toolName,
        string expectedDecision,
        bool requiresApproval,
        bool mayExecute)
    {
        var tool = GetSyntheticTool(toolName);
        var decision = new ToolPolicy().Decide(tool.Policy, toolName);

        Assert.Equal(expectedDecision, decision.Decision);
        Assert.Equal(requiresApproval, decision.RequiresApproval);
        Assert.Equal(mayExecute, decision.MayExecute);
    }

    [Fact]
    public void Decide_UsesPolicyMetadataFromRegisteredTool()
    {
        var tool = new MetadataOnlyTestTool();
        var decision = new ToolPolicy().Decide(tool.Policy, tool.Definition.Name);

        Assert.Equal("Allowed", decision.Decision);
        Assert.False(decision.RequiresApproval);
        Assert.True(decision.MayExecute);
        Assert.Equal("Policy metadata from the tool instance.", decision.Reason);
    }

    [Theory]
    [InlineData("DeleteDocument")]
    [InlineData("RunSqlQuery")]
    [InlineData("SendEmail")]
    public void Decide_RejectsKnownForbiddenTools(string toolName)
    {
        var decision = new ToolPolicy().Decide(null, toolName);

        Assert.Equal("Forbidden", decision.Decision);
        Assert.False(decision.MayExecute);
    }

    [Theory]
    [InlineData("mcp_admin_runsqlquery")]
    [InlineData("mcp_admin_run_sql_query")]
    [InlineData("mcp_admin_sendemail")]
    [InlineData("mcp_admin_delete_document")]
    public void Decide_RejectsKnownForbiddenToolFragmentsBeforeRegisteredMetadata(string requestedToolName)
    {
        var metadata = ToolPolicyMetadata.ApprovalRequired("External tool would otherwise require approval only.");

        var decision = new ToolPolicy().Decide(metadata, requestedToolName);

        Assert.Equal("Forbidden", decision.Decision);
        Assert.Equal(ToolRisk.Forbidden, decision.Risk);
        Assert.False(decision.RequiresApproval);
        Assert.False(decision.MayExecute);
    }

    [Fact]
    public void Decide_FailsClosedForUnknownTool()
    {
        var decision = new ToolPolicy().Decide(null, "UnregisteredTool");

        Assert.Equal("UnknownTool", decision.Decision);
        Assert.Equal(ToolRisk.Forbidden, decision.Risk);
        Assert.False(decision.MayExecute);
    }

    [Fact]
    public void Validate_FailsClosedWhenRequiredArgumentsAreMissing()
    {
        var ticketTool = GetSyntheticTool("CreateSupportTicket");

        using var arguments = JsonDocument.Parse("""{"title":"Missing description"}""");
        var result = ticketTool.Validate(arguments.RootElement);

        Assert.False(result.IsValid);
        Assert.Equal("missing_required_argument", result.ErrorCode);
    }

    [Fact]
    public void Validate_SanitizesDraftEmailAsDraftOnly()
    {
        var draftTool = GetSyntheticTool("DraftEmail");

        using var arguments = JsonDocument.Parse(
            """{"to":"a@example.test","subject":"Hello","body":"Body","ignored":"value"}""");
        var result = draftTool.Validate(arguments.RootElement);

        Assert.True(result.IsValid);
        Assert.Equal("draft", result.SanitizedArguments.GetProperty("mode").GetString());
        Assert.False(result.SanitizedArguments.TryGetProperty("ignored", out _));
    }

    [Fact]
    public async Task Execute_CreateSupportTicketReturnsStableDemoTicketIdForRepeatedProposal()
    {
        var ticketTool = GetSyntheticTool("CreateSupportTicket");
        using var arguments = JsonDocument.Parse(
            """{"title":"Help","description":"Need help","priority":"normal"}""");
        var validation = ticketTool.Validate(arguments.RootElement);
        Assert.True(validation.IsValid);

        var first = await ticketTool.ExecuteAsync(validation.SanitizedArguments, CancellationToken.None);
        var second = await ticketTool.ExecuteAsync(validation.SanitizedArguments, CancellationToken.None);

        Assert.Equal(
            first.Output.GetProperty("ticketId").GetString(),
            second.Output.GetProperty("ticketId").GetString());
    }

    // The original demo tool implementations (CreateSupportTicketTool, DraftEmailTool,
    // GetCurrentUserProfileTool) were removed along with the demo agentic chat loop.
    // The Governance tool-policy/execution subsystem is still kept and currently uncalled,
    // so these synthetic tools stand in to exercise its policy classification, schema
    // validation, and sanitization behavior with the same shapes the demo tools used.
    private static IAgentTool GetSyntheticTool(string name)
    {
        return name switch
        {
            "GetCurrentUserProfile" => new SyntheticGetCurrentUserProfileTool(),
            "CreateSupportTicket" => new SyntheticCreateSupportTicketTool(),
            "DraftEmail" => new SyntheticDraftEmailTool(),
            _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown synthetic tool.")
        };
    }

    private static JsonElement EmptyArguments()
    {
        using var document = JsonDocument.Parse("{}");
        return document.RootElement.Clone();
    }

    private sealed class MetadataOnlyTestTool : IAgentTool
    {
        public AiToolDefinition Definition { get; } = new(
            "MetadataOnly",
            "Test-only tool used to prove policy metadata comes from the registered instance.",
            "v1",
            EmptyArguments());

        public ToolPolicyMetadata Policy { get; } = ToolPolicyMetadata.Allowed(
            "Policy metadata from the tool instance.");

        public ToolValidationResult Validate(JsonElement arguments)
        {
            return ToolValidationResult.Valid(EmptyArguments());
        }

        public Task<ToolExecutionResult> ExecuteAsync(
            JsonElement sanitizedArguments,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new ToolExecutionResult(
                ToolExecutionStatus.Succeeded,
                EmptyArguments()));
        }
    }

    private sealed class SyntheticGetCurrentUserProfileTool : IAgentTool
    {
        public AiToolDefinition Definition { get; } = new(
            "GetCurrentUserProfile",
            "Returns the current authenticated user's profile.",
            "v1",
            EmptyArguments());

        public ToolPolicyMetadata Policy { get; } = ToolPolicyMetadata.Allowed(
            "Read-only profile lookup is safe to auto-execute.");

        public ToolValidationResult Validate(JsonElement arguments)
        {
            return ToolValidationResult.Valid(EmptyArguments());
        }

        public Task<ToolExecutionResult> ExecuteAsync(
            JsonElement sanitizedArguments,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new ToolExecutionResult(
                ToolExecutionStatus.Succeeded,
                EmptyArguments()));
        }
    }

    private sealed class SyntheticCreateSupportTicketTool : IAgentTool
    {
        public AiToolDefinition Definition { get; } = new(
            "CreateSupportTicket",
            "Creates a support ticket on behalf of the user.",
            "v1",
            EmptyArguments());

        public ToolPolicyMetadata Policy { get; } = ToolPolicyMetadata.Allowed(
            "Creating a support ticket is a low-risk, reversible action.");

        public ToolValidationResult Validate(JsonElement arguments)
        {
            if (!arguments.TryGetProperty("title", out _) ||
                !arguments.TryGetProperty("description", out _))
            {
                return ToolValidationResult.Invalid(
                    "missing_required_argument",
                    "CreateSupportTicket requires both 'title' and 'description'.");
            }

            return ToolValidationResult.Valid(arguments);
        }

        public Task<ToolExecutionResult> ExecuteAsync(
            JsonElement sanitizedArguments,
            CancellationToken cancellationToken)
        {
            var title = sanitizedArguments.GetProperty("title").GetString() ?? string.Empty;
            var ticketId = $"SUP-{Math.Abs(title.GetHashCode()):00000}";
            using var document = JsonDocument.Parse(
                $$"""{"ticketId":"{{ticketId}}","status":"Created"}""");

            return Task.FromResult(new ToolExecutionResult(
                ToolExecutionStatus.Succeeded,
                document.RootElement.Clone()));
        }
    }

    private sealed class SyntheticDraftEmailTool : IAgentTool
    {
        public AiToolDefinition Definition { get; } = new(
            "DraftEmail",
            "Drafts (but does not send) an email on behalf of the user.",
            "v1",
            EmptyArguments());

        public ToolPolicyMetadata Policy { get; } = ToolPolicyMetadata.ApprovalRequired(
            "Drafting communication content requires human approval before any send action.");

        public ToolValidationResult Validate(JsonElement arguments)
        {
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                foreach (var property in arguments.EnumerateObject())
                {
                    if (property.NameEquals("ignored"))
                    {
                        continue;
                    }

                    property.WriteTo(writer);
                }

                writer.WriteString("mode", "draft");
                writer.WriteEndObject();
            }

            stream.Position = 0;
            using var document = JsonDocument.Parse(stream);
            return ToolValidationResult.Valid(document.RootElement.Clone());
        }

        public Task<ToolExecutionResult> ExecuteAsync(
            JsonElement sanitizedArguments,
            CancellationToken cancellationToken)
        {
            using var document = JsonDocument.Parse("""{"draftId":"DRAFT-1","sent":false}""");
            return Task.FromResult(new ToolExecutionResult(
                ToolExecutionStatus.Succeeded,
                document.RootElement.Clone()));
        }
    }
}
