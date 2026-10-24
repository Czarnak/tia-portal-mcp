using System.Text.Json;
using TiaMcpServer.Contracts.Hmi;
using TiaMcpServer.Hmi;
using Xunit;

namespace TiaMcpServer.Tests.Hmi;

public class HmiWriteCatalogTests
{
    [Theory]
    [InlineData(256)]
    [InlineData(257)]
    [InlineData(200000)]
    public void OperationNameDiagnosticLimit_PreservesBoundaryAndBoundsUnknownNames(int length)
    {
        var name = new string('n', length);
        var item = new HmiWriteOperationRequest { OperationId = "id", Operation = name };

        var validation = HmiWriteCatalog.ValidateWrite([item]);

        Assert.False(validation.IsValid);
        Assert.Equal("validation_error", validation.Category);
        if (length <= 256)
            Assert.Contains(name, validation.Error);
        else
        {
            Assert.Equal("Each operation name must be at most 256 characters.", validation.Error);
            Assert.DoesNotContain(name, validation.Error);
        }
    }

    [Fact]
    public void WorkerMethods_EqualSharedWriteList()
    {
        var methods = HmiWriteCatalog.OperationNames.Select(HmiOperationCatalog.WorkerMethod).ToList();
        Assert.True(methods.ToHashSet(StringComparer.Ordinal).SetEquals(HmiWriteMethods.All));
        Assert.Equal(9, methods.Count);
    }

    private static HmiWriteOperationRequest Read(string fields) =>
        JsonSerializer.Deserialize<HmiWriteOperationRequest>(
            "{\"operationId\":\"one\"," + fields + "}",
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })!;

    [Theory]
    [InlineData("\"operation\":\"create_tag_table\",\"tableName\":\"T\"", true)]
    [InlineData("\"operation\":\"create_tag_table\",\"tableName\":\"T\",\"tagName\":\"X\"", false)]
    [InlineData("\"operation\":\"create_tag\",\"tableName\":\"T\",\"tagName\":\"X\",\"dataType\":\"Bool\"", true)]
    [InlineData("\"operation\":\"create_tag\",\"tableName\":\"T\",\"tagName\":\"X\",\"connection\":\"C\",\"plcTag\":\"DB.M\"", true)]
    [InlineData("\"operation\":\"update_tag\",\"tagName\":\"X\",\"connection\":\"C\"", false)]
    [InlineData("\"operation\":\"update_tag\",\"tagName\":\"X\",\"plcTag\":\"DB.M\",\"dataType\":\"Bool\"", false)]
    [InlineData("\"operation\":\"delete_tag\",\"tagName\":\"T.M\"", true)]
    [InlineData("\"operation\":\"create_alarm\",\"alarmKind\":\"discrete\",\"alarmName\":\"A\",\"triggerTag\":\"T.M\"", true)]
    [InlineData("\"operation\":\"create_alarm\",\"alarmKind\":\"analog\",\"alarmName\":\"A\",\"triggerTag\":\"T\",\"condition\":\"Equal\",\"conditionValue\":50", true)]
    [InlineData("\"operation\":\"update_alarm\",\"alarmKind\":\"analog\",\"alarmName\":\"A\",\"bitNumber\":0", false)]
    [InlineData("\"operation\":\"update_alarm\",\"alarmKind\":\"discrete\",\"alarmName\":\"A\",\"condition\":\"Equal\"", false)]
    [InlineData("\"operation\":\"update_tag\",\"tagName\":\"X\",\"acquisitionCycle\":\"T3s\"", false)]
    [InlineData("\"operation\":\"update_alarm\",\"alarmKind\":\"discrete\",\"alarmName\":\"A\",\"priority\":256", false)]
    [InlineData("\"operation\":\"update_alarm\",\"alarmKind\":\"discrete\",\"alarmName\":\"A\",\"bitNumber\":-1", false)]
    public void FieldRules(string fields, bool valid) =>
        Assert.Equal(valid, HmiWriteCatalog.ValidateWrite([Read(fields)]).IsValid);

    [Theory]
    [InlineData("50", 50, null)]
    [InlineData("50.0", 50, null)]
    [InlineData("50.5", null, 50.5)]
    [InlineData("3e9", null, 3e9)]
    [InlineData("50.000000000000000001", null, 50.0)]
    [InlineData("2147483647.000000001", null, 2147483647.0)]
    [InlineData("-2147483648.000000001", null, -2147483648.0)]
    [InlineData("2147483647", 2147483647, null)]
    [InlineData("-2147483648", -2147483648, null)]
    [InlineData("2147483648", null, 2147483648.0)]
    [InlineData("-2147483649", null, -2147483649.0)]
    [InlineData("5e1", 50, null)]
    [InlineData("500e-1", 50, null)]
    [InlineData("50.00e+0", 50, null)]
    [InlineData("-0.0", 0, null)]
    [InlineData("1e-400", null, 0.0)]
    [InlineData("-1e-400", null, -0.0)]
    public void ConditionValue_UsesSpecifiedNumericType(string json, int? integer, double? real)
    {
        var op = Read("\"operation\":\"update_alarm\",\"alarmKind\":\"analog\",\"alarmName\":\"A\",\"conditionValue\":" + json);
        Assert.True(HmiWriteCatalog.ValidateWrite([op]).IsValid);
        var worker = HmiWriteCatalog.ToWorkerRequest(op, "H");
        Assert.Equal(integer, worker.ConditionValueInt32);
        Assert.Equal(real, worker.ConditionValueDouble);
    }

    [Theory]
    [InlineData("\"50\"")]
    [InlineData("1e400")]
    public void ConditionValue_RejectsNonFiniteOrNonNumber(string json) =>
        Assert.False(HmiWriteCatalog.ValidateWrite([Read("\"operation\":\"update_alarm\",\"alarmKind\":\"analog\",\"alarmName\":\"A\",\"conditionValue\":" + json)]).IsValid);

    [Theory]
    [InlineData("")]
    [InlineData(" X")]
    [InlineData("X ")]
    [InlineData("X.M")]
    [InlineData("X\"M")]
    [InlineData("X\nM")]
    public void NewNames_AreRestricted(string name)
    {
        var op = new HmiWriteOperationRequest { OperationId = "one", Operation = "create_tag_table", TableName = name };
        Assert.False(HmiWriteCatalog.ValidateWrite([op]).IsValid);
        op.Operation = "update_tag_table"; op.TableName = "T"; op.NewName = name;
        Assert.False(HmiWriteCatalog.ValidateWrite([op]).IsValid);
    }

    [Theory]
    [InlineData("a&b<c>", "<body><p>a&amp;b&lt;c&gt;</p></body>")]
    [InlineData("żółć \"quoted\" 'apostrophe'", "<body><p>żółć \"quoted\" 'apostrophe'</p></body>")]
    [InlineData("&quot; &apos; &amp; &#34; <p>", "<body><p>&amp;quot; &amp;apos; &amp;amp; &amp;#34; &lt;p&gt;</p></body>")]
    public void EventText_EscapesOnlyXmlTextCharacters(string plain, string expected) =>
        Assert.Equal(expected, HmiWriteCatalog.WrapEventText(plain));

    [Fact]
    public void EmptyTextArray_IsRejected_EmptyStringAllowed()
    {
        var op = new HmiWriteOperationRequest { OperationId = "one", Operation = "update_tag", TagName = "T", Comment = [] };
        Assert.False(HmiWriteCatalog.ValidateWrite([op]).IsValid);
        op.Comment = [new HmiText("en-US", "")];
        Assert.True(HmiWriteCatalog.ValidateWrite([op]).IsValid);
        op.Comment = [new HmiText("en-US", "a"), new HmiText("en-US", "b")];
        Assert.False(HmiWriteCatalog.ValidateWrite([op]).IsValid);
        op.Comment = [new HmiText("en-US", "<body><p>a</p></body>")];
        Assert.False(HmiWriteCatalog.ValidateWrite([op]).IsValid);
        op.Comment = null; op.Operation = "update_alarm"; op.TagName = null; op.AlarmName = "A"; op.AlarmKind = "discrete";
        op.EventText = [new HmiText("en-US", "a\nb")];
        Assert.False(HmiWriteCatalog.ValidateWrite([op]).IsValid);
    }

    [Fact]
    public void BatchLimitsAndUnknownJsonFields_AreRejected()
    {
        var op = Read("\"operation\":\"delete_tag\",\"tagName\":\"T\"");
        Assert.False(HmiWriteCatalog.ValidateWrite(Enumerable.Repeat(op, 51).ToArray()).IsValid);
        Assert.False(HmiWriteCatalog.ValidateWrite([op, op]).IsValid);
        Assert.Throws<JsonException>(() => Read("\"operation\":\"delete_tag\",\"mystery\":1"));
    }

    private static HmiWriteOperationRequest DeleteTag(string id) =>
        new() { OperationId = id, Operation = "delete_tag", TagName = "T" };

    [Theory]
    [InlineData("null batch", "Batch must contain at least one operation.")]
    [InlineData("empty batch", "Batch must contain at least one operation.")]
    [InlineData("51 items", "Batch exceeds the maximum of 50 operations (received 51).")]
    [InlineData("null item", "Batch contains a null operation.")]
    [InlineData("blank id", "Each operation requires a unique operationId.")]
    [InlineData("257-character id", "Each operationId must be at most 256 characters.")]
    [InlineData("duplicate id", "Duplicate operationId 'x'.")]
    public void EnvelopeMessages_UseSharedWording(string batchCase, string expected)
    {
        HmiWriteOperationRequest[]? batch = batchCase switch
        {
            "null batch" => null,
            "empty batch" => [],
            "51 items" => Enumerable.Range(0, 51).Select(i => DeleteTag($"id{i}")).ToArray(),
            "null item" => [null!],
            "blank id" => [DeleteTag(" ")],
            "257-character id" => [DeleteTag(new string('i', 257))],
            "duplicate id" => [DeleteTag("x"), DeleteTag("x")],
            _ => throw new ArgumentOutOfRangeException(nameof(batchCase)),
        };

        Assert.Equal(expected, HmiWriteCatalog.ValidateWrite(batch).Error);
    }

    [Fact]
    public void OversizedOperationId_IsNeverEchoed()
    {
        var id = new string('o', 300);
        var item = DeleteTag(id);
        item.TableName = "NotValidForDeleteTag";

        var error = HmiWriteCatalog.ValidateWrite([item]).Error;

        Assert.DoesNotContain(id, error);
        Assert.Equal("Each operationId must be at most 256 characters.", error);
    }

    [Theory]
    [InlineData("create_tag_table", "TableName", "GroupPath")]
    [InlineData("update_tag_table", "TableName NewName", "")]
    [InlineData("delete_tag_table", "TableName", "")]
    [InlineData("create_tag", "TableName TagName", "Connection PlcTag DataType Comment DisplayName AcquisitionCycle")]
    [InlineData("update_tag", "TagName", "NewName PlcTag Connection DataType Comment DisplayName AcquisitionCycle")]
    [InlineData("delete_tag", "TagName", "")]
    [InlineData("create_alarm", "AlarmKind AlarmName TriggerTag", "Condition ConditionValue AlarmClass Priority EventText InfoText BitNumber TriggerMode")]
    [InlineData("update_alarm", "AlarmKind AlarmName", "NewName TriggerTag Condition ConditionValue AlarmClass Priority EventText InfoText BitNumber TriggerMode")]
    [InlineData("delete_alarm", "AlarmKind AlarmName", "")]
    public void OperationFieldSets_RejectEveryForeignField(string operation, string required, string optional)
    {
        var allowed = (required + " " + optional).Split(' ');
        foreach (var property in typeof(HmiWriteOperationRequest).GetProperties()
            .Where(p => p.Name is not ("Operation" or "OperationId" or "HmiName")))
        {
            if (allowed.Contains(property.Name)) continue;
            var op = new HmiWriteOperationRequest
            {
                OperationId = "one", Operation = operation,
                TableName = required.Contains("TableName") ? "Table" : null,
                TagName = required.Contains("TagName") ? "Tag" : null,
                AlarmName = required.Contains("AlarmName") ? "Alarm" : null,
                AlarmKind = required.Contains("AlarmKind") ? "discrete" : null,
                TriggerTag = required.Contains("TriggerTag") ? "Tag" : null,
                NewName = required.Contains("NewName") ? "Renamed" : null,
                DataType = operation == "create_tag" ? "Bool" : null,
            };
            Assert.True(HmiWriteCatalog.ValidateWrite([op]).IsValid, operation);
            object value = property.PropertyType == typeof(string) ? "Extra"
                : property.PropertyType == typeof(List<HmiText>) ? new List<HmiText> { new("en-US", "text") }
                : property.PropertyType == typeof(JsonElement?) ? JsonSerializer.SerializeToElement(50)
                : 1;
            property.SetValue(op, value);
            Assert.False(HmiWriteCatalog.ValidateWrite([op]).IsValid, operation + "." + property.Name);
        }
    }

    [Fact]
    public void WorkerRequest_NormalizesOnlyEventTextAndResolvedName()
    {
        var op = new HmiWriteOperationRequest
        {
            OperationId = "one", Operation = "update_alarm", HmiName = "alias", AlarmName = "A", AlarmKind = "discrete",
            EventText = [new("en-US", "a&b<c")], InfoText = [new("en-US", "a&b<c")],
        };
        var result = HmiWriteCatalog.ToWorkerRequest(op, "resolved");
        Assert.Equal("resolved", result.HmiName);
        Assert.Equal("<body><p>a&amp;b&lt;c</p></body>", result.EventText![0].Text);
        Assert.Equal("a&b<c", result.InfoText![0].Text);
        Assert.Equal("a&b<c", op.EventText[0].Text);
    }    [Fact]
    public void PartialOutcome_SurvivesWorkerEnvelopeAndHostResultCopy()
    {
        var outcome = new HmiWriteOutcomeInfo
        {
            ObjectName = "Tag", AppliedProperties = ["Name", "Connection"],
            FailedProperty = "PlcTag", Cleanup = "deleted", CleanupMessage = null,
        };
        var failure = new TiaMcpServer.OpennessWorker.Worker.WorkerOperationException(
            TiaMcpServer.Contracts.Worker.WorkerFailureCategories.WorkerOperationFailed,
            "setter failed", hmiWriteOutcome: outcome);
        var wire = TiaMcpServer.Contracts.Json.WorkerJson.SerializePayload(new TiaMcpServer.Contracts.Worker.WorkerResponse
        {
            Success = false, HmiWriteOutcome = failure.HmiWriteOutcome,
            Error = failure.Message, FailureCategory = failure.FailureCategory,
        });
        var decoded = JsonSerializer.Deserialize<TiaMcpServer.Contracts.Worker.WorkerResponse>(
            wire, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        Assert.Equal("deleted", decoded.HmiWriteOutcome!.Cleanup);
        Assert.Equal(["Name", "Connection"], decoded.HmiWriteOutcome.AppliedProperties);
        var result = TiaMcpServer.Worker.WorkerCallResult.Fail(failure.FailureCategory, failure.Message)
            with { HmiWriteOutcome = decoded.HmiWriteOutcome };
        Assert.Same(decoded.HmiWriteOutcome, (result with { Error = "identity changed" }).HmiWriteOutcome);
        Assert.Null(result.HmiWriteOutcome.CleanupMessage);
    }    [Theory]
    [InlineData(TiaMcpServer.Contracts.Safety.McpAccessMode.ReadOnly, 9)]
    [InlineData(TiaMcpServer.Contracts.Safety.McpAccessMode.ReadWrite, 0)]
    [InlineData(TiaMcpServer.Contracts.Safety.McpAccessMode.Full, 0)]
    public void AccessMode_UsesSharedPolicy(TiaMcpServer.Contracts.Safety.McpAccessMode mode, int denied)
    {
        var operations = HmiWriteCatalog.OperationNames.Select((name, i) =>
            new HmiWriteOperationRequest { Operation = name, OperationId = i.ToString() }).ToArray();
        Assert.Equal(denied, HmiWriteCatalog.ValidateAccessMode(operations, mode).Count);
    }}
