using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Library;
using TiaMcpServer.Library;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Library;

public sealed class LibraryWriteContractDecodeTests
{
    [Theory]
    [MemberData(nameof(LibraryPayloadContractTests.MalformedMetadata), MemberType = typeof(LibraryPayloadContractTests))]
    public void CreateMetadataMalformed_IsProtocolErrorWithoutEcho(string defect, string metadata)
    {
        var payload = WorkerJson.SerializePayload(new LibraryWriteResultInfo { Metadata = new() });
        payload = payload.Replace(WorkerJson.SerializePayload(new LibraryMetadataInfo()), metadata);
        var result = LibraryWritePayloadContract.Project(Item, WorkerCallResult.Ok(payload));
        Assert.True(result.Failure?.Category == "protocol_error", defect);
        Assert.Null(result.Result);
        Assert.DoesNotContain("leak-sentinel", TiaMcpServer.Json.CanonicalJson.Serialize(result));
    }

    [Fact]
    public void CreateMetadataNullableValuesRemainValid()
    {
        var result = LibraryWritePayloadContract.Project(Item,
            WorkerCallResult.Ok(WorkerJson.SerializePayload(new LibraryWriteResultInfo { Metadata = new()
                { Comments = [new()], UsedProducts = [new()], History = [new()] } })));
        Assert.Equal("succeeded", result.Status);
        Assert.Equal(System.Text.Json.JsonValueKind.Null,
            result.Result!.Value.GetProperty("metadata").GetProperty("supportedLanguages").ValueKind);
    }

    [Theory]
    [InlineData("folderPath")]
    [InlineData("scope")]
    [InlineData("kind")]
    [InlineData("name")]
    public void FailureIdentityMalformed_IsProtocolErrorWithoutEcho(string defect)
    {
        var identity = new LibraryIdentityInfo { Name = "leak-sentinel" };
        if (defect == "folderPath") identity.FolderPath = [null!];
        if (defect == "scope") identity.Scope = null!;
        if (defect == "kind") identity.Kind = null!;
        if (defect == "name") identity.Name = null!;
        var result = LibraryWritePayloadContract.Project(Item,
            WorkerCallResult.Fail("worker_operation_failed", "failed") with { LibraryWriteOutcome = new() { Library = identity } });
        Assert.Equal("protocol_error", result.Failure!.Category);
        Assert.Null(result.Result);
        Assert.DoesNotContain("leak-sentinel", TiaMcpServer.Json.CanonicalJson.Serialize(result));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("unknown")]
    [InlineData("case")]
    [InlineData("duplicate")]
    [InlineData("nullElement")]
    [InlineData("wrongType")]
    public void RawFailureIdentityMalformed_IsRejected(string defect)
    {
        var identity = WorkerJson.SerializePayload(new LibraryIdentityInfo { Name = "leak-sentinel" });
        identity = defect switch
        {
            "missing" => identity.Replace("\"libraryName\":null,", ""),
            "unknown" => identity.Replace("\"name\":", "\"unknown\":null,\"name\":"),
            "case" => identity.Replace("\"name\":", "\"Name\":"),
            "duplicate" => identity.Replace("\"name\":", "\"name\":null,\"name\":"),
            "nullElement" => identity.Replace("\"folderPath\":[]", "\"folderPath\":[null]"),
            _ => identity.Replace("\"name\":\"leak-sentinel\"", "\"name\":42"),
        };
        using var envelope = System.Text.Json.JsonDocument.Parse("{\"libraryWriteOutcome\":{\"saved\":null,\"actualName\":null,\"library\":" + identity + "}}");
        Assert.Throws<System.Text.Json.JsonException>(() => LibraryWritePayloadContract.ValidateEnvelopeOutcome(envelope.RootElement));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("unknown")]
    [InlineData("case")]
    [InlineData("nullEntry")]
    public void Decode_TypeOutcome_RawEnvelopeRejectsMalformedNestedEvidence(string defect)
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(WorkerJson.SerializePayload(new LibraryWriteOutcomeInfo { Type = new() }))!;
        var type = node["type"]!.AsObject();
        if (defect == "missing") type.Remove("versionGuid");
        if (defect == "unknown") type["unexpected"] = true;
        if (defect == "case") { type["VersionGuid"] = type["versionGuid"]?.DeepClone(); type.Remove("versionGuid"); }
        if (defect == "nullEntry") type["instancePaths"] = new System.Text.Json.Nodes.JsonArray((System.Text.Json.Nodes.JsonNode?)null);
        using var envelope = System.Text.Json.JsonDocument.Parse("{\"libraryWriteOutcome\":" + node.ToJsonString() + "}");
        Assert.Throws<System.Text.Json.JsonException>(() => LibraryWritePayloadContract.ValidateEnvelopeOutcome(envelope.RootElement));
    }
    [Theory]
    [InlineData("nullEntry")]
    [InlineData("nullName")]
    [InlineData("nullGuid")]
    public void Decode_TypeOutcome_Malformed_ProtocolError(string defect)
    {
        var type = new LibraryTypeResultInfo();
        if (defect == "nullEntry") type.InstancePaths = [null!];
        if (defect == "nullName") type.TypeName = null!;
        if (defect == "nullGuid") type.TypeGuid = null!;
        var result = LibraryWritePayloadContract.Project(Item, WorkerCallResult.Fail("worker_operation_failed", "failed") with
        { LibraryWriteOutcome = new() { Type = type } });
        Assert.Equal("protocol_error", result.Failure!.Category);
        Assert.Null(result.Result);
    }
    [Theory]
    [InlineData("mixed")]
    [InlineData("null-after")]
    [InlineData("altered-after")]
    [InlineData("wrong-culture")]
    public void MetadataChange_SuccessRequiresRequestedShapeAndExactText(string defect)
    {
        var change = new LibraryMetadataChangeInfo { Comments = [new() { Culture = "pl-PL", Before = null, After = "  exact\ntext  " }] };
        if (defect == "mixed") change.ActivatedLanguages = [];
        if (defect == "null-after") change.Comments[0].After = null;
        if (defect == "altered-after") change.Comments[0].After = "leak-sentinel";
        if (defect == "wrong-culture") change.Comments[0].Culture = "de-DE";
        var item = new LibraryWriteItem { OperationId = "a", Operation = "set_comments", Scope = "global", LibraryName = "Fixture",
            Comments = [new() { Culture = "pl-PL", Text = "  exact\ntext  " }] };
        var result = LibraryWritePayloadContract.Project(item, WorkerCallResult.Ok(WorkerJson.SerializePayload(new LibraryWriteResultInfo { MetadataChange = change })));
        Assert.Equal("protocol_error", result.Failure?.Category);
        Assert.Null(result.Result);
        Assert.DoesNotContain("leak-sentinel", TiaMcpServer.Json.CanonicalJson.Serialize(result));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("unknown")]
    [InlineData("case")]
    [InlineData("duplicate")]
    [InlineData("null-entry")]
    [InlineData("null-culture")]
    [InlineData("mixed")]
    public void MetadataChange_StrictAcrossPlanSuccessAndOutcome(string defect)
    {
        var validChange = new LibraryMetadataChangeInfo { Comments = [new() { Culture = "pl-PL", Before = null, After = "leak-sentinel" }] };
        var request = new LibraryWriteItem { OperationId = "a", Operation = "set_comments", Comments = [new() { Culture = "pl-PL", Text = "leak-sentinel" }] };
        Assert.Equal("succeeded", LibraryWritePayloadContract.Project(request,
            WorkerCallResult.Ok(WorkerJson.SerializePayload(new LibraryWriteResultInfo { MetadataChange = validChange }))).Status);
        var change = WorkerJson.SerializePayload(validChange);
        change = defect switch
        {
            "missing" => change.Replace("\"before\":null,", ""),
            "unknown" => change.Replace("\"before\":", "\"unknown\":null,\"before\":"),
            "case" => change.Replace("\"before\":", "\"Before\":"),
            "duplicate" => change.Replace("\"before\":", "\"before\":null,\"before\":"),
            "null-entry" => change.Replace("[{", "[null,{"),
            "null-culture" => change.Replace("\"culture\":\"pl-PL\"", "\"culture\":null"),
            _ => change.Replace("\"activatedLanguages\":null", "\"activatedLanguages\":[]")
        };
        foreach (var wrapper in new[] { "plan", "success", "outcome" })
        {
            var payload = wrapper switch { "plan" => WorkerJson.SerializePayload(new LibraryWritePlanInfo()),
                "success" => WorkerJson.SerializePayload(new LibraryWriteResultInfo()), _ => WorkerJson.SerializePayload(new LibraryWriteOutcomeInfo()) };
            payload = "{\"metadataChange\":" + change + "," + payload[1..];
            if (wrapper == "plan")
                Assert.Throws<System.Text.Json.JsonException>(() => {
                    var plan = TiaMcpServer.Json.CanonicalJson.DeserializeWorkerPayload<LibraryWritePlanInfo>(payload);
                    LibraryWritePayloadContract.ValidateMetadataChange(plan.MetadataChange);
                });
            else if (wrapper == "outcome")
            {
                using var envelope = System.Text.Json.JsonDocument.Parse("{\"libraryWriteOutcome\":" + payload + "}");
                Assert.Throws<System.Text.Json.JsonException>(() => LibraryWritePayloadContract.ValidateEnvelopeOutcome(envelope.RootElement));
            }
            else
            {
                var result = LibraryWritePayloadContract.Project(request, WorkerCallResult.Ok(payload));
                Assert.Equal("protocol_error", result.Failure?.Category);
                Assert.Null(result.Result);
                Assert.DoesNotContain("leak-sentinel", TiaMcpServer.Json.CanonicalJson.Serialize(result));
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MetadataChange_AbsentAndConditionalNullRemainValid(bool explicitNull)
    {
        foreach (var payload in new[] { WorkerJson.SerializePayload(new LibraryWritePlanInfo()),
            WorkerJson.SerializePayload(new LibraryWriteResultInfo()), WorkerJson.SerializePayload(new LibraryWriteOutcomeInfo()) })
        {
            var json = explicitNull ? "{\"metadataChange\":null," + payload[1..] : payload;
            if (payload.Contains("masterCopyExists"))
                Assert.Null(TiaMcpServer.Json.CanonicalJson.DeserializeWorkerPayload<LibraryWritePlanInfo>(json).MetadataChange);
            else if (payload.Contains("archivePath"))
                Assert.Equal("succeeded", LibraryWritePayloadContract.Project(Item, WorkerCallResult.Ok(json)).Status);
            else
            {
                using var envelope = System.Text.Json.JsonDocument.Parse("{\"libraryWriteOutcome\":" + json + "}");
                LibraryWritePayloadContract.ValidateEnvelopeOutcome(envelope.RootElement);
            }
        }
    }

    [Fact]
    public void MetadataChange_FailureAllowsUnobservedAfterAndSuccessPreservesExactText()
    {
        var item = new LibraryWriteItem { OperationId = "a", Operation = "set_comments", Comments = [new() { Culture = "PL-pl", Text = "  exact\r\ntext  " }] };
        var change = new LibraryMetadataChangeInfo { Comments = [new() { Culture = "pl-PL", Before = null, After = null }] };
        var failed = LibraryWritePayloadContract.Project(item, WorkerCallResult.Fail("engineering_rejected", "refused") with { LibraryWriteOutcome = new() { MetadataChange = change } });
        Assert.Equal("engineering_rejected", failed.Failure!.Category);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, failed.Result!.Value.GetProperty("metadataChange").GetProperty("comments")[0].GetProperty("after").ValueKind);
        change.Comments[0].After = item.Comments[0].Text;
        var success = LibraryWritePayloadContract.Project(item, WorkerCallResult.Ok(WorkerJson.SerializePayload(new LibraryWriteResultInfo { MetadataChange = change })));
        Assert.Equal("succeeded", success.Status);
        Assert.Equal(item.Comments[0].Text, success.Result!.Value.GetProperty("metadataChange").GetProperty("comments")[0].GetProperty("after").GetString());
    }

    [Theory]
    [InlineData("comments")]
    [InlineData("null-language")]
    [InlineData("duplicate-language")]
    [InlineData("wrong-editing")]
    [InlineData("unexpected-reference")]
    [InlineData("unexpected-activation")]
    [InlineData("activation-null")]
    public void MetadataChange_LanguageSuccessRejectsWrongShape(string defect)
    {
        var change = new LibraryMetadataChangeInfo { ActivatedLanguages = ["pl-PL"], EditingLanguage = new() { Before = null, After = "pl-PL" } };
        if (defect == "comments") change.Comments = [new() { Culture = "pl-PL", After = "text" }];
        if (defect == "null-language") change.ActivatedLanguages = [null!];
        if (defect == "duplicate-language") change.ActivatedLanguages.Add("pl-PL");
        if (defect == "wrong-editing") change.EditingLanguage.After = "de-DE";
        if (defect == "unexpected-reference") change.ReferenceLanguage = new() { Before = null, After = "pl-PL" };
        if (defect == "unexpected-activation") change.ActivatedLanguages = ["de-DE"];
        if (defect == "activation-null") change.ActivatedLanguages = null;
        var item = new LibraryWriteItem { OperationId = "a", Operation = "set_languages", EditingLanguage = "PL-pl" };
        var result = LibraryWritePayloadContract.Project(item, WorkerCallResult.Ok(WorkerJson.SerializePayload(new LibraryWriteResultInfo { MetadataChange = change })));
        Assert.Equal("protocol_error", result.Failure?.Category);
        Assert.Null(result.Result);
    }

    [Theory]
    [InlineData("set_comments", false)]
    [InlineData("set_comments", true)]
    [InlineData("set_languages", false)]
    [InlineData("set_languages", true)]
    public void MetadataSuccess_RequiresNonNullReceipt(string operation, bool explicitNull)
    {
        var payload = WorkerJson.SerializePayload(new LibraryWriteResultInfo());
        if (explicitNull) payload = "{\"metadataChange\":null," + payload[1..];
        var item = new LibraryWriteItem { OperationId = "a", Operation = operation };
        var result = LibraryWritePayloadContract.Project(item, WorkerCallResult.Ok(payload));
        Assert.Equal("protocol_error", result.Failure?.Category);
        Assert.Null(result.Result);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MetadataFailure_LanguageObservationsCanBePartialOrAbsent(bool receipt)
    {
        var item = new LibraryWriteItem { OperationId = "a", Operation = "set_languages", ActivateLanguages = ["pl-PL"], EditingLanguage = "pl-PL", ReferenceLanguage = "pl-PL" };
        var outcome = new LibraryWriteOutcomeInfo { MetadataChange = receipt ? new()
            { ActivatedLanguages = ["de-DE"], EditingLanguage = new() { Before = "en-US", After = null }, ReferenceLanguage = new() { Before = "en-US", After = null } } : null };
        var result = LibraryWritePayloadContract.Project(item, WorkerCallResult.Fail("worker_operation_failed", "refused") with { LibraryWriteOutcome = outcome });
        Assert.Equal("worker_operation_failed", result.Failure?.Category);
        Assert.NotNull(result.Result);
        if (receipt) Assert.Equal(System.Text.Json.JsonValueKind.Null, result.Result.Value.GetProperty("metadataChange").GetProperty("editingLanguage").GetProperty("after").ValueKind);
        else Assert.False(result.Result.Value.TryGetProperty("metadataChange", out _));
    }

    [Fact]
    public void MetadataSuccess_RequiresNonNullLibrary()
    {
        var item = new LibraryWriteItem { OperationId = "a", Operation = "set_comments", Comments = [new() { Culture = "pl-PL", Text = "new" }] };
        var result = LibraryWritePayloadContract.Project(item, WorkerCallResult.Ok(WorkerJson.SerializePayload(new LibraryWriteResultInfo
            { Library = null!, MetadataChange = new() { Comments = [new() { Culture = "pl-PL", After = "new" }] } })));
        Assert.Equal("protocol_error", result.Failure?.Category);
        Assert.Null(result.Result);
    }

    private static readonly LibraryWriteItem Item = new() { OperationId = "a", Operation = "close_global" };

    [Fact]
    public void SuccessDecodesTypedResultWithExplicitNulls()
    {
        var result = LibraryWritePayloadContract.Project(Item, WorkerCallResult.Ok(WorkerJson.SerializePayload(new LibraryWriteResultInfo
        {
            Library = new() { Scope = "global", Kind = "library", Name = "Fixture", LibraryPath = @"C:\Libraries\Fixture.al21" }, Saved = true,
        })));
        Assert.Equal("succeeded", result.Status);
        Assert.True(result.Result!.Value.GetProperty("saved").GetBoolean());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, result.Result.Value.GetProperty("target").ValueKind);
        Assert.Equal(System.Text.Json.JsonValueKind.Object, result.Result.Value.GetProperty("library").ValueKind);
    }

    [Fact]
    public void FailureWithoutOutcomeHasNoInventedReceipt()
    {
        var result = LibraryWritePayloadContract.Project(Item, WorkerCallResult.Fail("target_not_found", "missing", ["warning"]));
        Assert.Equal("failed", result.Status);
        Assert.Null(result.Result);
        Assert.Equal("target_not_found", result.Failure!.Category);
        Assert.Equal(["warning"], result.Warnings);
    }

    [Theory]
    [InlineData("library")]
    [InlineData("target")]
    public void NullFolderSegmentInAnyIdentityRejectsResultEvidence(string position)
    {
        var identity = new LibraryIdentityInfo { Name = "leak-sentinel", FolderPath = [null!] };
        var payload = new LibraryWriteResultInfo();
        if (position == "library") payload.Library = identity;
        else payload.Target = identity;
        var result = LibraryWritePayloadContract.Project(Item, WorkerCallResult.Ok(WorkerJson.SerializePayload(payload)));
        Assert.Equal("failed", result.Status);
        Assert.Equal("protocol_error", result.Failure!.Category);
        Assert.Null(result.Result);
        Assert.DoesNotContain("leak-sentinel", TiaMcpServer.Json.CanonicalJson.Serialize(result));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EmptyFolderPathsAndOptionalNullTargetRemainValid(bool targetPresent)
    {
        var payload = new LibraryWriteResultInfo { Target = targetPresent ? new() : null };
        var result = LibraryWritePayloadContract.Project(Item, WorkerCallResult.Ok(WorkerJson.SerializePayload(payload)));
        Assert.Equal("succeeded", result.Status);
        var document = result.Result!.Value;
        Assert.Equal(0, document.GetProperty("library").GetProperty("folderPath").GetArrayLength());
        if (targetPresent) Assert.Equal(0, document.GetProperty("target").GetProperty("folderPath").GetArrayLength());
        else Assert.Equal(System.Text.Json.JsonValueKind.Null, document.GetProperty("target").ValueKind);
    }

    [Fact]
    public void FailurePreservesPartialReceipt()
    {
        var result = LibraryWritePayloadContract.Project(Item, WorkerCallResult.Fail("engineering_rejected", "close failed") with
            { LibraryWriteOutcome = new() { Saved = true, ActualName = null } });
        Assert.Equal("failed", result.Status);
        Assert.True(result.Result!.Value.GetProperty("saved").GetBoolean());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, result.Result.Value.GetProperty("actualName").ValueKind);
        Assert.Equal("engineering_rejected", result.Failure!.Category);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("not json")]
    [InlineData("{\"leak-sentinel\":1}")]
    public void InvalidSuccessFailsClosedWithoutEcho(string payload)
    {
        var result = LibraryWritePayloadContract.Project(Item, WorkerCallResult.Ok(payload));
        Assert.Equal("protocol_error", result.Failure!.Category);
        Assert.Null(result.Result);
        Assert.DoesNotContain("leak-sentinel", result.Failure.Message);
    }
}
