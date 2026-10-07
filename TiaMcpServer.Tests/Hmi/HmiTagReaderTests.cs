using Siemens.Engineering;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.HmiTags;
using Siemens.Engineering.HmiUnified.LoggingTags;
using TiaMcpServer.Contracts;
using TiaMcpServer.OpennessWorker;
using TiaMcpServer.OpennessWorker.Openness.Hmi;
using Xunit;
using static TiaMcpServer.Tests.Hmi.HmiTestProjects;

namespace TiaMcpServer.Tests.Hmi;

public class HmiTagReaderTests
{
    private static HmiTag Tag(string name, string table = "T1", string dataType = "Int") => new()
    {
        Name = name,
        TagTableName = table,
        DataType = dataType,
        Connection = "Conn",
        PlcTag = "plc_" + name,
        HmiDataType = "Int16",
        Address = "%DB1.DBW0",
        AcquisitionMode = HmiAcquisitionMode.CyclicContinuous,
        AcquisitionCycle = "1 s",
        AccessMode = HmiAccessMode.SymbolicAccess,
        Persistent = true,
        LinearScaling = false,
        Comment = new MultilingualText().With("en-US", "<body><p>c</p></body>").With("pl-PL", "k"),
    };

    private static HmiTagTable Table(string name, params HmiTag[] tags)
    {
        var table = new HmiTagTable { Name = name };
        table.Tags.Items.AddRange(tags);
        return table;
    }

    /// <summary>Software whose <c>Tags</c> is the union of the given tables, as in Openness.</summary>
    private static HmiSoftware SoftwareWith(params HmiTagTable[] rootTables)
    {
        var software = Unified("Panel_RT");
        software.TagTables.Items.AddRange(rootTables);
        foreach (var tag in rootTables.SelectMany(t => t.Tags.Items))
        {
            software.Tags.Items.Add(tag);
        }

        return software;
    }

    private static HmiTagTableGroup Group(string name, params HmiTagTable[] tables)
    {
        var group = new HmiTagTableGroup { Name = name };
        group.TagTables.Items.AddRange(tables);
        return group;
    }

    [Fact]
    public void TableTreeNestsGroupsWithTagCounts()
    {
        var software = SoftwareWith(Table("b_root", Tag("x")), Table("A_root"));
        var inner = Group("Inner", Table("deep", Tag("d1"), Tag("d2")));
        var outer = Group("Outer", Table("mid", Tag("m1")));
        outer.Groups.Items.Add(inner);
        software.TagTableGroups.Items.Add(outer);

        var info = HmiTagReader.ListTagTables(software, null);

        Assert.True(info.IsComplete);
        Assert.Null(info.GroupPath);
        Assert.Equal(new[] { "A_root", "b_root" }, info.Tables.Select(t => t.Name));
        Assert.Equal(new[] { 0, 1 }, info.Tables.Select(t => t.TagCount));
        var outerInfo = Assert.Single(info.Groups);
        Assert.Equal("Outer", outerInfo.Name);
        Assert.Equal(1, Assert.Single(outerInfo.Tables).TagCount);
        var innerInfo = Assert.Single(outerInfo.Groups);
        Assert.Equal(2, Assert.Single(innerInfo.Tables).TagCount);
    }

    [Fact]
    public void GroupPathNarrowsTreeAndMissingGroupIsTargetNotFound()
    {
        var software = SoftwareWith();
        var inner = Group("Inner", Table("deep", Tag("d1")));
        var outer = Group("Outer");
        outer.Groups.Items.Add(inner);
        software.TagTableGroups.Items.Add(outer);

        var narrowed = HmiTagReader.ListTagTables(software, "Outer/Inner");

        Assert.Equal("Outer/Inner", narrowed.GroupPath);
        Assert.Equal("deep", Assert.Single(narrowed.Tables).Name);
        Assert.Empty(narrowed.Groups);

        var ex = Assert.Throws<WorkerOperationException>(() => HmiTagReader.ListTagTables(software, "Outer/inner"));
        Assert.Equal(WorkerFailureCategories.TargetNotFound, ex.FailureCategory);
        Assert.Throws<WorkerOperationException>(() => HmiTagReader.ListTagTables(software, "Nope"));
    }

    [Fact]
    public void ListTagsPagesByNameAcrossAllTables()
    {
        var software = SoftwareWith(Table("T1", Tag("c"), Tag("a")), Table("T2", Tag("b", "T2"), Tag("D", "T2")));

        var first = HmiTagReader.ListTags(software, null, null, 0, 3);
        var second = HmiTagReader.ListTags(software, null, null, 3, 3);
        var past = HmiTagReader.ListTags(software, null, null, 10, 3);

        Assert.Equal(new[] { "a", "b", "c" }, first.Tags.Select(t => t.Name));
        Assert.Equal(new HmiPage(0, 3, 4, 3), first.Page);
        Assert.Equal(new[] { "D" }, second.Tags.Select(t => t.Name));
        Assert.Null(second.Page.NextOffset);
        Assert.Empty(past.Tags);
        Assert.Equal(4, past.Page.Total);
        var row = first.Tags[0];
        Assert.Equal("T1", row.Table);
        Assert.Equal("Conn", row.Connection);
        Assert.Equal("plc_a", row.PlcTag);
        Assert.Equal("Int", row.DataType);
        Assert.Equal("%DB1.DBW0", row.Address);
        Assert.Equal("CyclicContinuous", row.AcquisitionMode);
        Assert.Equal("1 s", row.AcquisitionCycle);
        Assert.Equal("SymbolicAccess", row.AccessMode);
        Assert.Equal(true, row.Persistent);
        Assert.Equal(false, row.LinearScaling);
        Assert.Equal(new[] { "en-US", "pl-PL" }, row.Comment.Select(c => c.Culture));
        Assert.Equal("<body><p>c</p></body>", row.Comment[0].Text);
    }

    [Fact]
    public void LanguageNarrowsTheCommentTexts()
    {
        var software = SoftwareWith(Table("T1", Tag("a")));

        var row = Assert.Single(HmiTagReader.ListTags(software, null, "pl-PL", 0, 10).Tags);

        Assert.Equal("k", Assert.Single(row.Comment).Text);
    }

    [Fact]
    public void TableNameNarrowsAndMissingTableIsTargetNotFound()
    {
        var software = SoftwareWith(Table("T1", Tag("a")));
        var group = Group("G", Table("t2", Tag("z", "t2")));
        software.TagTableGroups.Items.Add(group);
        software.Tags.Items.Add(group.TagTables.Items[0].Tags.Items[0]);

        var inGroup = HmiTagReader.ListTags(software, "T2", null, 0, 10);
        var root = HmiTagReader.ListTags(software, "t1", null, 0, 10);

        Assert.Equal(new[] { "z" }, inGroup.Tags.Select(t => t.Name));
        Assert.Equal(new[] { "a" }, root.Tags.Select(t => t.Name));
        var ex = Assert.Throws<WorkerOperationException>(() => HmiTagReader.ListTags(software, "nope", null, 0, 10));
        Assert.Equal(WorkerFailureCategories.TargetNotFound, ex.FailureCategory);
    }

    [Fact]
    public void UnreadablePropertyNullsFieldAndMarksIncomplete()
    {
        var bad = Tag("b");
        bad.Failures["Connection"] = new EngineeringTargetInvocationException("no connection on this device");
        var software = SoftwareWith(Table("T1", Tag("a"), bad, Tag("c")));

        var info = HmiTagReader.ListTags(software, null, null, 0, 10);

        Assert.False(info.IsComplete);
        Assert.Equal(new[] { "a", "b", "c" }, info.Tags.Select(t => t.Name));
        Assert.Null(info.Tags[1].Connection);
        Assert.Equal("Conn", info.Tags[0].Connection);
        Assert.Equal("Int", info.Tags[1].DataType);
        Assert.Contains("no connection on this device", Assert.Single(info.Messages));
    }

    [Fact]
    public void UnreadableCommentIsNullNotEmptyAndMarksIncomplete()
    {
        var bad = Tag("b");
        bad.Failures["Comment"] = new EngineeringTargetInvocationException("comment unavailable");
        var software = SoftwareWith(Table("T1", Tag("a"), bad));

        var info = HmiTagReader.ListTags(software, null, "de-DE", 0, 10);

        Assert.False(info.IsComplete);
        Assert.Contains("comment unavailable", Assert.Single(info.Messages));
        Assert.Null(info.Tags[1].Comment);
        Assert.NotNull(info.Tags[0].Comment);
        Assert.Empty(info.Tags[0].Comment!);
    }

    [Fact]
    public void DisposedObjectDuringARowReadFailsTheItem()
    {
        var bad = Tag("b");
        bad.Failures["Address"] = new EngineeringObjectDisposedException("TIA Portal has been disposed.");
        var software = SoftwareWith(Table("T1", bad));

        var ex = Assert.Throws<WorkerOperationException>(() => HmiTagReader.ListTags(software, null, null, 0, 10));

        Assert.Equal(WorkerFailureCategories.WorkerOperationFailed, ex.FailureCategory);
    }

    [Fact]
    public void RowReadsEachPropertyOnceAndNeverTheConfirmationFamily()
    {
        var tag = Tag("a");
        var messages = new List<string>();

        HmiTagReader.ReadRow(tag, null, messages);

        Assert.Empty(messages);
        Assert.Equal(tag.Reads.Count, tag.Reads.Distinct().Count());
        Assert.DoesNotContain(tag.Reads, r => r is "ConfirmationType" or "GmpRelevant" or "MandatoryCommenting");
    }

    [Fact]
    public void GetTagMissingIsTargetNotFound()
    {
        var software = SoftwareWith(Table("T1", Tag("a")));

        var ex = Assert.Throws<WorkerOperationException>(() => HmiTagReader.GetTag(software, "nope", null));

        Assert.Equal(WorkerFailureCategories.TargetNotFound, ex.FailureCategory);
        Assert.Equal("a", HmiTagReader.GetTag(software, "A", null).Tag.Name);
    }

    [Fact]
    public void GetTagDuplicateNameIsTargetAmbiguous()
    {
        var software = SoftwareWith(Table("T1", Tag("a")), Table("T2", Tag("A", "T2")));

        var ex = Assert.Throws<WorkerOperationException>(() => HmiTagReader.GetTag(software, "a", null));

        Assert.Equal(WorkerFailureCategories.TargetAmbiguous, ex.FailureCategory);
    }

    [Fact]
    public void MembersStopAtDepthCapWithTruncatedFlag()
    {
        // Chain: top -> m1 -> m2 ... -> mN. The cap lets m1..m8 be read.
        HmiTag Chain(int length)
        {
            var top = Tag("top");
            var parent = top;
            for (var i = 1; i <= length; i++)
            {
                var member = Tag("m" + i);
                parent.Members.Items.Add(member);
                parent = member;
            }

            return top;
        }

        int Depth(IReadOnlyList<HmiTagMemberInfo> members) => members.Count == 0 ? 0 : 1 + Depth(members[0].Members);

        var exact = HmiTagReader.GetTag(SoftwareWith(Table("T1", Chain(8))), "top", null);
        var tooDeep = HmiTagReader.GetTag(SoftwareWith(Table("T1", Chain(10))), "top", null);

        Assert.Equal(8, Depth(exact.Members));
        Assert.False(exact.MembersTruncated);
        Assert.Equal(8, Depth(tooDeep.Members));
        Assert.True(tooDeep.MembersTruncated);
        Assert.True(tooDeep.IsComplete);
    }

    [Fact]
    public void GetTagObjectValuesAreVariants()
    {
        var tag = Tag("a");
        tag.InitialValue = "";
        tag.HmiStartValue = 0.0;
        tag.HmiEndValue = 100.0;
        tag.PlcStartValue = 0.0;
        tag.PlcEndValue = 10.0;
        tag.InitialMaxValue = new UpperRange { Value = "", ValueType = HmiLimitValueType.None };
        tag.InitialMinValue = new LowerRange { Value = 5.0, ValueType = HmiLimitValueType.Constant };
        tag.SubstituteValue = new HmiSubstituteValue { SubstituteValueUsage = HmiSubstituteValueUsage.InvalidValue, Value = "x" };
        tag.Thresholds.Items.Add(new HmiThreshold { Name = "Hi", Mode = HmiThresholdMode.Upper, Value = 90.0, ValueType = HmiLimitValueType.Constant });
        tag.LoggingTags.Items.Add(new HmiLoggingTag
        {
            Name = "a_log", DataLog = "Log1", LoggingMode = HmiLoggingMode.Cyclic, Cycle = "1 s",
            TriggerMode = HmiTriggerMode.None, HighLimit = "", LowLimit = "",
        });

        var info = HmiTagReader.GetTag(SoftwareWith(Table("T1", tag)), "a", null);

        Assert.Equal("System.String", info.InitialValue!.Type);
        Assert.Equal("System.Double", info.HmiEndValue!.Type);
        Assert.Equal(100, info.HmiEndValue.Value!.Value.GetDouble());
        Assert.Equal(10, info.PlcEndValue!.Value!.Value.GetDouble());
        Assert.Equal("None", info.UpperRange!.ValueType);
        Assert.Equal("Constant", info.LowerRange!.ValueType);
        Assert.Equal(5, info.LowerRange.Value!.Value!.Value.GetDouble());
        Assert.Equal("InvalidValue", info.SubstituteValue!.Usage);
        Assert.Equal("x", info.SubstituteValue.Value!.Value!.Value.GetString());
        var threshold = Assert.Single(info.Thresholds);
        Assert.Equal(("Hi", "Upper", "Constant"), (threshold.Name, threshold.Mode, threshold.ValueType));
        Assert.Equal(90, threshold.Value!.Value!.Value.GetDouble());
        var logging = Assert.Single(info.LoggingTags);
        Assert.Equal(("a_log", "a", "Log1", "Cyclic"), (logging.Name, logging.Tag, logging.DataLog, logging.LoggingMode));
    }

    [Fact]
    public void GetTagUnreadableVariantIsNullWithMessage()
    {
        var tag = Tag("a");
        tag.Failures["PlcStartValue"] = new EngineeringTargetInvocationException("no plc start value");

        var info = HmiTagReader.GetTag(SoftwareWith(Table("T1", tag)), "a", null);

        Assert.False(info.IsComplete);
        Assert.Null(info.PlcStartValue);
        Assert.NotNull(info.PlcEndValue);
        Assert.Contains(info.Messages, m => m.Contains("no plc start value"));
    }

    [Fact]
    public void UnreadableMembersCompositionFailsTheItem()
    {
        var tag = Tag("a");
        tag.Members.EnumerationFailure = new EngineeringTargetInvocationException("members unavailable");

        var ex = Assert.Throws<WorkerOperationException>(
            () => HmiTagReader.GetTag(SoftwareWith(Table("T1", tag)), "a", null));

        Assert.Equal(WorkerFailureCategories.WorkerOperationFailed, ex.FailureCategory);
        Assert.Contains("members unavailable", ex.Message);
    }

    [Fact]
    public void LoggingTagsFilterByDataLogAndTag()
    {
        var a = Tag("a");
        a.LoggingTags.Items.Add(new HmiLoggingTag { Name = "a_l1", DataLog = "Log1", LoggingMode = HmiLoggingMode.OnChange, Cycle = "1 s", TriggerMode = HmiTriggerMode.None });
        a.LoggingTags.Items.Add(new HmiLoggingTag { Name = "a_l2", DataLog = "Log2", LoggingMode = HmiLoggingMode.Cyclic, Cycle = "5 s", TriggerMode = HmiTriggerMode.RisingEdge });
        var b = Tag("b");
        b.LoggingTags.Items.Add(new HmiLoggingTag { Name = "b_l1", DataLog = "log1", LoggingMode = HmiLoggingMode.OnDemand, Cycle = "1 s", TriggerMode = HmiTriggerMode.None });
        var software = SoftwareWith(Table("T1", a, b, Tag("c")));

        var all = HmiTagReader.ListLoggingTags(software, null, null, 0, 10);
        var byLog = HmiTagReader.ListLoggingTags(software, "LOG1", null, 0, 10);
        var byTag = HmiTagReader.ListLoggingTags(software, null, "a", 0, 10);
        var both = HmiTagReader.ListLoggingTags(software, "Log2", "a", 0, 10);
        var paged = HmiTagReader.ListLoggingTags(software, null, null, 1, 1);

        Assert.Equal(new[] { "a_l1", "a_l2", "b_l1" }, all.LoggingTags.Select(l => l.Name));
        Assert.Equal(3, all.Page.Total);
        Assert.Equal(new[] { "a_l1", "b_l1" }, byLog.LoggingTags.Select(l => l.Name));
        Assert.Equal(new[] { "a_l1", "a_l2" }, byTag.LoggingTags.Select(l => l.Name));
        Assert.Equal("a_l2", Assert.Single(both.LoggingTags).Name);
        var row = both.LoggingTags[0];
        Assert.Equal(("a", "Log2", "Cyclic", "5 s", "RisingEdge"), (row.Tag, row.DataLog, row.LoggingMode, row.Cycle, row.TriggerMode));
        Assert.Equal("a_l2", Assert.Single(paged.LoggingTags).Name);
        Assert.Equal(2, paged.Page.NextOffset);
        var ex = Assert.Throws<WorkerOperationException>(() => HmiTagReader.ListLoggingTags(software, null, "nope", 0, 10));
        Assert.Equal(WorkerFailureCategories.TargetNotFound, ex.FailureCategory);
    }

    [Fact]
    public void SystemTagsPaged()
    {
        var software = Unified("Panel_RT");
        software.SystemTags.Items.Add(new HmiSystemTag { Name = "@Zeta", DataType = "Int" });
        software.SystemTags.Items.Add(new HmiSystemTag { Name = "@Alpha", DataType = "Bool" });
        software.SystemTags.Items.Add(new HmiSystemTag { Name = "@Mid", DataType = "Real" });

        var first = HmiTagReader.ListSystemTags(software, 0, 2);
        var last = HmiTagReader.ListSystemTags(software, 2, 2);

        Assert.Equal(new[] { "@Alpha", "@Mid" }, first.SystemTags.Select(s => s.Name));
        Assert.Equal(new[] { "Bool", "Real" }, first.SystemTags.Select(s => s.DataType));
        Assert.Equal(new HmiPage(0, 2, 3, 2), first.Page);
        Assert.Equal("@Zeta", Assert.Single(last.SystemTags).Name);
        Assert.Null(last.Page.NextOffset);
    }

    [Fact]
    public void DispatchRoutesTheTagMethods()
    {
        var software = SoftwareWith(Table("T1", Tag("a")));
        var project = ProjectWith(DeviceWith("Panel", software));
        var query = new HmiQueryInfo { TagName = "a", Offset = 0, Limit = 5 };

        Assert.IsType<HmiTagTableTreeInfo>(HmiReadDispatch.Read(project, "hmi_list_tag_tables", query));
        Assert.IsType<HmiTagListInfo>(HmiReadDispatch.Read(project, "hmi_list_tags", query));
        Assert.IsType<HmiTagDetailInfo>(HmiReadDispatch.Read(project, "hmi_get_tag", query));
        Assert.IsType<HmiSystemTagListInfo>(HmiReadDispatch.Read(project, "hmi_list_system_tags", query));
        Assert.IsType<HmiLoggingTagListInfo>(HmiReadDispatch.Read(project, "hmi_list_logging_tags", query));
    }
}
