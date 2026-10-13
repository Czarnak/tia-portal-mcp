using Siemens.Engineering;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.HmiTags;
using Siemens.Engineering.HmiUnified.LoggingTags;
using TiaMcpServer.Contracts.Hmi;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.Hmi;

/// <summary>
/// Operations 2, 3, 4, 5 and 11: tag tables, tags, system tags and logging tags. Reads enumerate
/// compositions (never <c>Find</c>); tag properties are read from explicit lists only. <c>ConfirmationType</c>
/// crashed TIA Portal in the spike, and <c>GmpRelevant</c> and <c>MandatoryCommenting</c> were never tested live, so none of the three is read.
/// An unreadable composition fails the item; an unreadable scalar property is null plus a message.
/// </summary>
public static class HmiTagReader
{
    /// <summary>Spike S9: the deepest observed member nesting is 4, so 8 leaves twice the headroom.</summary>
    public const int MaxMemberDepth = 8;

    /// <summary>
    /// Total member rows read for one tag, all levels combined. Live E6 measured about 397 characters per row,
    /// so 100 rows stay well under the 60,000-character value budget.
    /// </summary>
    public const int MaxMemberRows = 100;

    private sealed class MemberWalk
    {
        public int Rows;
        public bool Truncated;
        public bool RowCapHit;
    }

    public static HmiTagTableTreeInfo ListTagTables(HmiSoftware software, string? groupPath)
    {
        IEnumerable<HmiTagTable> tables;
        IEnumerable<HmiTagTableGroup> groups;
        if (groupPath is null)
        {
            tables = HmiReadLog.Guard(() => software.TagTables.ToList(), "The tag tables");
            groups = HmiReadLog.Guard(() => software.TagTableGroups.ToList(), "The tag table groups");
        }
        else
        {
            var group = ResolveGroup(software, groupPath);
            tables = HmiReadLog.Guard(() => group.TagTables.ToList(), $"The tag tables of group '{groupPath}'");
            groups = HmiReadLog.Guard(() => group.Groups.ToList(), $"The groups of group '{groupPath}'");
        }

        return new HmiTagTableTreeInfo
        {
            GroupPath = groupPath,
            Tables = TableInfos(tables),
            Groups = GroupInfos(groups),
        };
    }

    public static HmiTagListInfo ListTags(
        HmiSoftware software, string? tableName, string? language, HmiPaging paging, object project)
    {
        IEnumerable<(HmiTag Tag, string Name)> Scan()
            => (tableName is null
                    ? HmiReadLog.Guard(() => software.Tags.ToList(), "The tags")
                    : TagsOfTables(software, tableName))
                .Select(t => (Tag: t, Name: TagName(t)));

        var (items, page) = HmiPager.Page(Scan, n => n.Name, paging, project, software);
        var log = new HmiReadLog();
        var rows = items
            .Where(n => log.IsLive(paging, n.Tag, $"Tag '{n.Name}'"))
            .Select(n => ReadRow(n.Tag, language, log))
            .ToList();

        return new HmiTagListInfo { IsComplete = log.IsComplete, Messages = log.Messages, Page = page, Tags = rows };
    }

    public static HmiTagDetailInfo GetTag(HmiSoftware software, string tagName, string? language)
    {
        var tag = HmiTagLookup.FindUnique(software, tagName);
        var log = new HmiReadLog();
        var name = TagName(tag);
        var walk = new MemberWalk();
        var detail = new HmiTagDetailInfo
        {
            Tag = ReadRow(tag, language, log),
            Members = ReadMembers(tag, name, language, 0, log, walk),
            MembersTruncated = walk.Truncated,
            Thresholds = ReadThresholds(tag, name, log),
            LoggingTags = ReadLoggingTags(tag, name, log),
            UpperRange = ReadRange(log, name, "InitialMaxValue", () => tag.InitialMaxValue),
            LowerRange = ReadRange(log, name, "InitialMinValue", () => tag.InitialMinValue),
            SubstituteValue = ReadSubstitute(tag, name, log),
            InitialValue = log.ReadVariant(() => tag.InitialValue, $"Property InitialValue of '{name}'"),
            HmiStartValue = log.ReadVariant(() => tag.HmiStartValue, $"Property HmiStartValue of '{name}'"),
            HmiEndValue = log.ReadVariant(() => tag.HmiEndValue, $"Property HmiEndValue of '{name}'"),
            PlcStartValue = log.ReadVariant(() => tag.PlcStartValue, $"Property PlcStartValue of '{name}'"),
            PlcEndValue = log.ReadVariant(() => tag.PlcEndValue, $"Property PlcEndValue of '{name}'"),
        };
        // Like the depth cap, the row cap is a deliberate bound, not a failed read, so it leaves completeness alone.
        if (walk.RowCapHit)
        {
            log.Note($"Members of tag '{name}' stopped at the {MaxMemberRows}-row cap; the remaining members were not read.");
        }

        detail.Messages = log.Messages;
        detail.IsComplete = log.IsComplete;
        return detail;
    }

    public static HmiSystemTagListInfo ListSystemTags(HmiSoftware software, HmiPaging paging, object project)
    {
        var (items, page) = HmiPager.Page(
            () => HmiReadLog.Guard(() => software.SystemTags.ToList(), "The system tags")
                .Select(s => (Tag: s, Name: HmiReadLog.Guard(() => s.Name, "A system tag name"))),
            n => n.Name,
            paging,
            project,
            software);
        var log = new HmiReadLog();
        var rows = items
            .Where(n => log.IsLive(paging, n.Tag, $"System tag '{n.Name}'"))
            .Select(n => new HmiSystemTagInfo
            {
                Name = n.Name,
                DataType = log.Try(() => n.Tag.DataType, $"The data type of system tag '{n.Name}'"),
            })
            .ToList();

        return new HmiSystemTagListInfo { IsComplete = log.IsComplete, Messages = log.Messages, Page = page, SystemTags = rows };
    }

    public static HmiLoggingTagListInfo ListLoggingTags(
        HmiSoftware software, string? dataLogName, string? tagName, HmiPaging paging, object project)
    {
        var log = new HmiReadLog();
        IEnumerable<NamedLoggingTag> Scan()
        {
            if (dataLogName is not null)
            {
                HmiLogReader.RequireDataLog(software, dataLogName);
            }

            var tags = HmiReadLog.Guard(() => software.Tags.ToList(), "The tags");
            if (tagName is not null)
            {
                tags = tags.Where(t => string.Equals(TagName(t), tagName, StringComparison.OrdinalIgnoreCase)).ToList();
                if (tags.Count == 0)
                {
                    throw NotFound($"Tag '{tagName}' was not found.");
                }
            }

            var rows = tags.SelectMany(t => NamedLoggingTags(t, TagName(t)));
            return dataLogName is null
                ? rows
                : rows.Where(r => string.Equals(
                    log.Try(() => r.Source.DataLog, $"Property DataLog of logging tag '{r.Name}'"),
                    dataLogName,
                    StringComparison.OrdinalIgnoreCase));
        }

        // Logging tag names are only unique per owning tag, so the owner completes the sort key.
        var (items, page) = HmiPager.Page(Scan, r => r.Name + "\0" + r.Tag, paging, project, software);
        return new HmiLoggingTagListInfo
        {
            Page = page,
            LoggingTags = items
                .Where(r => log.IsLive(paging, r.Source, $"Logging tag '{r.Name}' of tag '{r.Tag}'"))
                .Select(r => ReadLoggingTag(r, log))
                .ToList(),
            IsComplete = log.IsComplete,
            Messages = log.Messages,
        };
    }

    private readonly record struct NamedLoggingTag(HmiLoggingTag Source, string Name, string Tag);

    private static HmiTagRowInfo ReadRow(HmiTag tag, string? language, HmiReadLog log)
    {
        var name = TagName(tag);
        string What(string property) => $"Property {property} of tag '{name}'";
        return new HmiTagRowInfo
        {
            Name = name,
            Table = log.Try(() => tag.TagTableName, What("TagTableName")),
            Connection = log.Try(() => tag.Connection, What("Connection")),
            PlcTag = log.Try(() => tag.PlcTag, What("PlcTag")),
            PlcName = log.Try(() => tag.PlcName, What("PlcName")),
            DisplayName = log.ReadText(() => tag.DisplayName, language, What("DisplayName")),
            DataType = log.Try(() => tag.DataType, What("DataType")),
            HmiDataType = log.Try(() => tag.HmiDataType, What("HmiDataType")),
            Address = log.Try(() => tag.Address, What("Address")),
            AcquisitionMode = log.Try(() => tag.AcquisitionMode.ToString(), What("AcquisitionMode")),
            AcquisitionCycle = log.Try(() => tag.AcquisitionCycle, What("AcquisitionCycle")),
            AccessMode = log.Try(() => tag.AccessMode.ToString(), What("AccessMode")),
            Persistent = log.Try(() => (bool?)tag.Persistent, What("Persistent")),
            LinearScaling = log.Try(() => (bool?)tag.LinearScaling, What("LinearScaling")),
            Comment = log.ReadText(() => tag.Comment, language, What("Comment")),
        };
    }

    private static List<HmiTagMemberInfo> ReadMembers(
        HmiTag parent, string parentName, string? language, int depth, HmiReadLog log, MemberWalk walk)
    {
        var members = HmiReadLog.Guard(() => parent.Members.ToList(), $"The members of tag '{parentName}'");
        if (members.Count == 0)
        {
            return new List<HmiTagMemberInfo>();
        }

        if (depth >= MaxMemberDepth)
        {
            walk.Truncated = true;
            return new List<HmiTagMemberInfo>();
        }

        var result = new List<HmiTagMemberInfo>();
        foreach (var member in members)
        {
            if (walk.Rows >= MaxMemberRows)
            {
                walk.Truncated = true;
                walk.RowCapHit = true;
                break;
            }

            walk.Rows++;
            var row = ReadRow(member, language, log);
            result.Add(new HmiTagMemberInfo
            {
                Tag = row,
                Members = ReadMembers(member, row.Name, language, depth + 1, log, walk),
            });
        }

        return result;
    }

    private static List<HmiThresholdInfo> ReadThresholds(HmiTag tag, string tagName, HmiReadLog log)
    {
        var thresholds = HmiReadLog.Guard(() => tag.Thresholds.ToList(), $"The thresholds of tag '{tagName}'");
        return thresholds
            .Select(t => new HmiThresholdInfo
            {
                Name = log.Try(() => t.Name, $"A threshold name of tag '{tagName}'"),
                Mode = log.Try(() => t.Mode.ToString(), $"A threshold mode of tag '{tagName}'"),
                Value = log.ReadVariant(() => t.Value, $"Property a threshold Value of '{tagName}'"),
                ValueType = log.Try(() => t.ValueType.ToString(), $"A threshold value type of tag '{tagName}'"),
            })
            .ToList();
    }

    private static List<HmiLoggingTagRowInfo> ReadLoggingTags(HmiTag tag, string tagName, HmiReadLog log)
        => NamedLoggingTags(tag, tagName).Select(l => ReadLoggingTag(l, log)).ToList();

    private static List<NamedLoggingTag> NamedLoggingTags(HmiTag tag, string tagName)
        => HmiReadLog.Guard(() => tag.LoggingTags.ToList(), $"The logging tags of tag '{tagName}'")
            .Select(l => new NamedLoggingTag(l, HmiReadLog.Guard(() => l.Name, $"A logging tag name of tag '{tagName}'"), tagName))
            .ToList();

    private static HmiLoggingTagRowInfo ReadLoggingTag(NamedLoggingTag named, HmiReadLog log)
    {
        var (l, name, tagName) = named;
        string What(string property) => $"Property {property} of logging tag '{name}'";
        return new HmiLoggingTagRowInfo
        {
            Name = name,
            Tag = tagName,
            DataLog = log.Try(() => l.DataLog, What("DataLog")),
            LoggingMode = log.Try(() => l.LoggingMode.ToString(), What("LoggingMode")),
            Cycle = log.Try(() => l.Cycle, What("Cycle")),
            TriggerMode = log.Try(() => l.TriggerMode.ToString(), What("TriggerMode")),
            HighLimit = log.ReadVariant(() => l.HighLimit, $"Property HighLimit of '{name}'"),
            LowLimit = log.ReadVariant(() => l.LowLimit, $"Property LowLimit of '{name}'"),
        };
    }

    private static HmiRangeInfo? ReadRange(HmiReadLog log, string tagName, string property, Func<Siemens.Engineering.HmiUnified.HmiTags.Range?> read)
    {
        var range = log.Try(read, $"Property {property} of tag '{tagName}'");
        if (range is null)
        {
            return null;
        }

        return new HmiRangeInfo
        {
            Value = log.ReadVariant(() => range.Value, $"Property {property}.Value of '{tagName}'"),
            ValueType = log.Try(() => range.ValueType.ToString(), $"Property {property}.ValueType of tag '{tagName}'"),
        };
    }

    private static HmiSubstituteValueInfo? ReadSubstitute(HmiTag tag, string tagName, HmiReadLog log)
    {
        var substitute = log.Try(() => tag.SubstituteValue, $"Property SubstituteValue of tag '{tagName}'");
        if (substitute is null)
        {
            return null;
        }

        return new HmiSubstituteValueInfo
        {
            Usage = log.Try(() => substitute.SubstituteValueUsage.ToString(), $"Property SubstituteValue.SubstituteValueUsage of tag '{tagName}'"),
            Value = log.ReadVariant(() => substitute.Value, $"Property SubstituteValue.Value of '{tagName}'"),
        };
    }

    private static List<HmiTag> TagsOfTables(HmiSoftware software, string tableName)
    {
        var matches = new List<HmiTagTable>();
        CollectTables(HmiReadLog.Guard(() => software.TagTables.ToList(), "The tag tables"), tableName, matches);
        CollectTables(HmiReadLog.Guard(() => software.TagTableGroups.ToList(), "The tag table groups"), tableName, matches);
        if (matches.Count == 0)
        {
            throw NotFound($"Tag table '{tableName}' was not found. Use list_tag_tables to see the tag tables.");
        }

        return matches.SelectMany(t => HmiReadLog.Guard(() => t.Tags.ToList(), $"The tags of table '{tableName}'")).ToList();
    }

    private static void CollectTables(IEnumerable<HmiTagTable> tables, string tableName, List<HmiTagTable> matches)
    {
        matches.AddRange(tables.Where(t => string.Equals(HmiReadLog.Guard(() => t.Name, "A tag table name"), tableName, StringComparison.OrdinalIgnoreCase)));
    }

    private static void CollectTables(IEnumerable<HmiTagTableGroup> groups, string tableName, List<HmiTagTable> matches)
    {
        foreach (var group in groups)
        {
            CollectTables(HmiReadLog.Guard(() => group.TagTables.ToList(), "The tag tables of a group"), tableName, matches);
            CollectTables(HmiReadLog.Guard(() => group.Groups.ToList(), "The groups of a group"), tableName, matches);
        }
    }

    /// <summary>The tags with their names, for <c>validate</c>; a name or composition failure fails the item.</summary>
    internal static List<(object Item, string Name)> NamedTags(HmiSoftware software)
        => HmiReadLog.Guard(() => software.Tags.ToList(), "The tags").Select(t => ((object)t, TagName(t))).ToList();

    /// <summary>The members of a tag at every depth up to <see cref="MaxMemberDepth"/>, named <c>Parent.Member</c>, for <c>validate</c>.</summary>
    internal static IEnumerable<(object Item, string Name)> MemberObjects(HmiTag tag, string tagName)
    {
        var members = HmiReadLog.Guard(() => tag.Members.ToList(), $"The members of tag '{tagName}'");
        return MemberObjects(members, tagName, 0);
    }

    private static IEnumerable<(object Item, string Name)> MemberObjects(List<HmiTag> members, string parentName, int depth)
    {
        // Same cut as ReadMembers: members below MaxMemberDepth levels are not read.
        if (depth >= MaxMemberDepth)
        {
            yield break;
        }

        foreach (var member in members)
        {
            var name = parentName + "." + TagName(member);
            yield return (member, name);
            var children = HmiReadLog.Guard(() => member.Members.ToList(), $"The members of tag '{name}'");
            foreach (var child in MemberObjects(children, name, depth + 1))
            {
                yield return child;
            }
        }
    }

    /// <summary>The logging tags of a tag, named <c>Tag/LoggingTag</c>, for <c>validate</c>.</summary>
    internal static IEnumerable<(object Item, string Name)> LoggingTagObjects(HmiTag tag, string tagName)
        => NamedLoggingTags(tag, tagName).Select(l => ((object)l.Source, tagName + "/" + l.Name));

    private static HmiTagTableGroup ResolveGroup(HmiSoftware software, string groupPath)
    {
        IEnumerable<HmiTagTableGroup> level = HmiReadLog.Guard(() => software.TagTableGroups.ToList(), "The tag table groups");
        HmiTagTableGroup? current = null;
        foreach (var segment in groupPath.Split('/'))
        {
            current = level.FirstOrDefault(g => string.Equals(HmiReadLog.Guard(() => g.Name, "A group name"), segment, StringComparison.Ordinal))
                ?? throw NotFound($"Tag table group '{groupPath}' was not found. Use list_tag_tables to see the groups.");
            var captured = current;
            level = HmiReadLog.Guard(() => captured.Groups.ToList(), $"The groups of group '{segment}'");
        }

        return current!;
    }

    private static List<HmiTagTableInfo> TableInfos(IEnumerable<HmiTagTable> tables)
    {
        var infos = tables
            .Select(t => new HmiTagTableInfo
            {
                Name = HmiReadLog.Guard(() => t.Name, "A tag table name"),
                TagCount = HmiReadLog.Guard(() => t.Tags.Count(), "The tags of a tag table"),
            })
            .ToList();
        return HmiPager.InNameOrder(infos, i => i.Name).ToList();
    }

    private static List<HmiTagTableGroupInfo> GroupInfos(IEnumerable<HmiTagTableGroup> groups)
    {
        var infos = groups
            .Select(g => new HmiTagTableGroupInfo
            {
                Name = HmiReadLog.Guard(() => g.Name, "A tag table group name"),
                Tables = TableInfos(HmiReadLog.Guard(() => g.TagTables.ToList(), "The tag tables of a group")),
                Groups = GroupInfos(HmiReadLog.Guard(() => g.Groups.ToList(), "The groups of a group")),
            })
            .ToList();
        return HmiPager.InNameOrder(infos, i => i.Name).ToList();
    }

    private static string TagName(HmiTag tag) => HmiReadLog.Guard(() => tag.Name, "A tag name");

    private static WorkerOperationException NotFound(string message)
        => new(WorkerFailureCategories.TargetNotFound, message);
}
