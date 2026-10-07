using TiaMcpServer.Contracts;
using TiaMcpServer.Hmi;
using Xunit;

namespace TiaMcpServer.Tests.Hmi;

public class HmiOperationCatalogTests
{
    // Wire field name -> setter. The same names the catalog and the tool schema use.
    private static readonly Dictionary<string, Action<HmiOperationRequest>> Setters = new()
    {
        ["hmiName"] = r => r.HmiName = "HMI_1",
        ["language"] = r => r.Language = "en-US",
        ["offset"] = r => r.Offset = 0,
        ["limit"] = r => r.Limit = 10,
        ["groupPath"] = r => r.GroupPath = "A/B",
        ["tableName"] = r => r.TableName = "T",
        ["tagName"] = r => r.TagName = "Tag",
        ["dataLogName"] = r => r.DataLogName = "Log",
        ["alarmName"] = r => r.AlarmName = "Alarm",
        ["alarmKind"] = r => r.AlarmKind = "discrete",
        ["screenName"] = r => r.ScreenName = "Screen",
        ["category"] = r => r.Category = "tags",
        ["name"] = r => r.Name = "Obj",
    };

    // Spec §2.3: operation -> (required, optional).
    private static readonly Dictionary<string, (string[] Required, string[] Optional)> Specs = new()
    {
        ["list_hmi_devices"] = (new string[0], new string[0]),
        ["list_tag_tables"] = (new string[0], new[] { "hmiName", "groupPath" }),
        ["list_tags"] = (new string[0], new[] { "hmiName", "tableName", "language", "offset", "limit" }),
        ["get_tag"] = (new[] { "tagName" }, new[] { "hmiName", "language" }),
        ["list_system_tags"] = (new string[0], new[] { "hmiName", "offset", "limit" }),
        ["list_connections"] = (new string[0], new[] { "hmiName", "language" }),
        ["list_alarms"] = (new string[0], new[] { "hmiName", "alarmKind", "language", "offset", "limit" }),
        ["get_alarm"] = (new[] { "alarmName", "alarmKind" }, new[] { "hmiName", "language" }),
        ["list_alarm_classes"] = (new string[0], new[] { "hmiName", "language" }),
        ["list_logs"] = (new string[0], new[] { "hmiName" }),
        ["list_logging_tags"] = (new string[0], new[] { "hmiName", "dataLogName", "tagName", "offset", "limit" }),
        ["list_screens"] = (new string[0], new[] { "hmiName", "groupPath", "language" }),
        ["list_screen_items"] = (new[] { "screenName" }, new[] { "hmiName", "offset", "limit" }),
        ["list_faceplate_instances"] = (new string[0], new[] { "hmiName", "screenName", "offset", "limit" }),
        ["get_screen_navigation"] = (new string[0], new[] { "hmiName" }),
        ["get_runtime_settings"] = (new string[0], new[] { "hmiName" }),
        ["list_script_modules"] = (new string[0], new[] { "hmiName" }),
        ["list_text_and_graphic_lists"] = (new string[0], new[] { "hmiName" }),
        ["list_project_languages"] = (new string[0], new string[0]),
        ["validate"] = (new[] { "category" }, new[] { "hmiName", "name", "offset", "limit" }),
    };

    private static HmiOperationRequest Build(string operation, IEnumerable<string> fields)
    {
        var request = new HmiOperationRequest { OperationId = "a", Operation = operation };
        foreach (var field in fields)
        {
            Setters[field](request);
        }

        return request;
    }

    public static TheoryData<string> AllOperations()
    {
        var data = new TheoryData<string>();
        foreach (var name in Specs.Keys)
        {
            data.Add(name);
        }

        return data;
    }

    [Fact]
    public void CatalogDeclaresExactlyTheTwentySpecOperations()
        => Assert.Equal(
            Specs.Keys.OrderBy(k => k, StringComparer.Ordinal),
            HmiOperationCatalog.OperationNames.OrderBy(k => k, StringComparer.Ordinal));

    [Theory]
    [MemberData(nameof(AllOperations))]
    public void EveryOperationAcceptsItsRequiredAndOptionalFields(string operation)
    {
        var (required, optional) = Specs[operation];

        Assert.True(HmiOperationCatalog.ValidateRead(new[] { Build(operation, required) }).IsValid);
        Assert.True(
            HmiOperationCatalog.ValidateRead(new[] { Build(operation, required.Concat(optional)) }).IsValid,
            HmiOperationCatalog.ValidateRead(new[] { Build(operation, required.Concat(optional)) }).Error);
    }

    [Theory]
    [MemberData(nameof(AllOperations))]
    public void EveryOperationRejectsUndeclaredFields(string operation)
    {
        var (required, optional) = Specs[operation];
        foreach (var undeclared in Setters.Keys.Except(required).Except(optional))
        {
            var result = HmiOperationCatalog.ValidateRead(new[] { Build(operation, required.Append(undeclared)) });

            Assert.False(result.IsValid, $"{operation} must reject '{undeclared}'.");
            Assert.Contains($"'{undeclared}' is not valid for {operation}", result.Error);
        }
    }

    [Theory]
    [MemberData(nameof(AllOperations))]
    public void EveryOperationRejectsAMissingRequiredField(string operation)
    {
        var (required, _) = Specs[operation];
        foreach (var missing in required)
        {
            var result = HmiOperationCatalog.ValidateRead(
                new[] { Build(operation, required.Where(f => f != missing)) });

            Assert.False(result.IsValid);
            Assert.Contains(missing, result.Error);
        }
    }

    [Theory]
    [InlineData("list_hmi_devices")]
    [InlineData("list_project_languages")]
    public void HmiNameIsRejectedOnListHmiDevicesAndListProjectLanguages(string operation)
    {
        var result = HmiOperationCatalog.ValidateRead(new[] { Build(operation, new[] { "hmiName" }) });

        Assert.False(result.IsValid);
        Assert.Contains("'hmiName' is not valid", result.Error);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(2001)]
    public void LimitOutside1To2000IsValidationError(int limit)
    {
        var request = Build("list_tags", Array.Empty<string>());
        request.Limit = limit;

        var result = HmiOperationCatalog.ValidateRead(new[] { request });

        Assert.False(result.IsValid);
        Assert.Contains("limit", result.Error);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2000)]
    public void LimitBoundsAreInclusive(int limit)
    {
        var request = Build("list_tags", Array.Empty<string>());
        request.Limit = limit;

        Assert.True(HmiOperationCatalog.ValidateRead(new[] { request }).IsValid);
    }

    [Fact]
    public void NegativeOffsetIsValidationError()
    {
        var request = Build("list_tags", Array.Empty<string>());
        request.Offset = -1;

        var result = HmiOperationCatalog.ValidateRead(new[] { request });

        Assert.False(result.IsValid);
        Assert.Contains("offset", result.Error);
    }

    [Theory]
    [InlineData("not a culture!")]
    [InlineData("")]
    [InlineData("en_US_extra_bad")]
    public void MalformedCultureIsValidationError(string culture)
    {
        var request = Build("list_tags", Array.Empty<string>());
        request.Language = culture;

        var result = HmiOperationCatalog.ValidateRead(new[] { request });

        Assert.False(result.IsValid);
        Assert.Contains("language", result.Error);
    }

    [Fact]
    public void WellFormedCultureIsAccepted()
    {
        var request = Build("list_tags", Array.Empty<string>());
        request.Language = "pl-PL";

        Assert.True(HmiOperationCatalog.ValidateRead(new[] { request }).IsValid);
    }

    [Fact]
    public void UnknownCategoryOrAlarmKindIsValidationError()
    {
        var badCategory = Build("validate", Array.Empty<string>());
        badCategory.Category = "Tags";
        var badKind = Build("list_alarms", Array.Empty<string>());
        badKind.AlarmKind = "digital";

        var category = HmiOperationCatalog.ValidateRead(new[] { badCategory });
        var kind = HmiOperationCatalog.ValidateRead(new[] { badKind });

        Assert.False(category.IsValid);
        Assert.Contains("category", category.Error);
        Assert.False(kind.IsValid);
        Assert.Contains("alarmKind", kind.Error);
    }

    [Fact]
    public void NameWithCategoryAllIsValidationError()
    {
        var request = Build("validate", new[] { "name" });
        request.Category = "all";

        var result = HmiOperationCatalog.ValidateRead(new[] { request });

        Assert.False(result.IsValid);
        Assert.Contains("category all", result.Error);
    }

    [Theory]
    [InlineData("A//B")]
    [InlineData("/A")]
    [InlineData("A/")]
    [InlineData("")]
    public void GroupPathWithEmptySegmentIsValidationError(string groupPath)
    {
        var request = Build("list_tag_tables", Array.Empty<string>());
        request.GroupPath = groupPath;

        var result = HmiOperationCatalog.ValidateRead(new[] { request });

        Assert.False(result.IsValid);
        Assert.Contains("groupPath", result.Error);
    }

    [Fact]
    public void MoreThan50OrDuplicateIdsRejected()
    {
        var tooMany = Enumerable.Range(0, 51)
            .Select(i => new HmiOperationRequest { OperationId = $"id{i}", Operation = "list_hmi_devices" })
            .ToArray();
        var duplicates = new[]
        {
            new HmiOperationRequest { OperationId = "x", Operation = "list_hmi_devices" },
            new HmiOperationRequest { OperationId = "x", Operation = "list_project_languages" },
        };

        Assert.False(HmiOperationCatalog.ValidateRead(tooMany).IsValid);
        var duplicate = HmiOperationCatalog.ValidateRead(duplicates);
        Assert.False(duplicate.IsValid);
        Assert.Contains("Duplicate operationId 'x'", duplicate.Error);
        Assert.False(HmiOperationCatalog.ValidateRead(null).IsValid);
        Assert.False(HmiOperationCatalog.ValidateRead(Array.Empty<HmiOperationRequest>()).IsValid);
    }

    [Fact]
    public void UnknownOperationNamesTheValidOnes()
    {
        var result = HmiOperationCatalog.ValidateRead(
            new[] { new HmiOperationRequest { OperationId = "a", Operation = "create_tag" } });

        Assert.False(result.IsValid);
        Assert.Contains("Unknown operation 'create_tag'", result.Error);
        Assert.Contains("list_hmi_devices", result.Error);
    }

    [Theory]
    [MemberData(nameof(AllOperations))]
    public void WorkerMethodPrefixesHmi(string operation)
        => Assert.Equal("hmi_" + operation, HmiOperationCatalog.WorkerMethod(operation));

    [Theory]
    [MemberData(nameof(AllOperations))]
    public void EveryWorkerMethodIsObserveAndAllowedReadOnly(string operation)
    {
        var method = HmiOperationCatalog.WorkerMethod(operation);

        Assert.Equal(OperationCapability.Observe, OperationPolicyCatalog.GetCapability(method));
        foreach (var mode in new[] { McpAccessMode.ReadOnly, McpAccessMode.ReadWrite, McpAccessMode.Full })
        {
            Assert.True(OperationPolicyCatalog.IsAllowed(mode, method));
            Assert.Empty(HmiOperationCatalog.ValidateAccessMode(
                new[] { Build(operation, Specs[operation].Required) }, mode));
        }
    }
}
