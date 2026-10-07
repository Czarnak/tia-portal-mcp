using Siemens.Engineering;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.Common;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness.Hmi;

/// <summary>
/// Operation 20: <c>Validate()</c> over the objects of a category. The page is cut from the scanned objects in
/// name order (kind then name for <c>all</c>), so the cost of a call is bounded by <c>limit</c>; findings are
/// reported only for the objects of the page. Kinds without <c>Validate</c> (spike S6) are listed once in
/// <c>notValidatable</c>. An unreadable composition fails the item; a recoverable <c>Validate</c> failure marks
/// that object unvalidated with a message and the result incomplete.
/// </summary>
public static class HmiValidationReader
{
    public static HmiValidationInfo Validate(HmiSoftware software, string category, string? name, int offset, int limit)
    {
        var scanned = Objects(software, category);
        if (name is not null)
        {
            scanned = scanned.Where(o => string.Equals(o.Name, name, StringComparison.OrdinalIgnoreCase)).ToList();
            if (scanned.Count == 0)
            {
                throw new WorkerOperationException(
                    WorkerFailureCategories.TargetNotFound,
                    $"No '{category}' object named '{name}' was found. Use the list operations to see the objects.");
            }
        }

        var (items, page) = HmiPager.Page(
            scanned, o => category == "all" ? KindOf(o.Item) + "\0" + o.Name : o.Name, offset, limit);
        var info = new HmiValidationInfo { Page = page, Scanned = items.Count };
        var log = new HmiReadLog();
        var notValidatable = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var (item, itemName) in items)
        {
            var kind = KindOf(item);
            if (item is not IValidator validator)
            {
                notValidatable.Add(kind);
                continue;
            }

            var results = Validate(validator, kind, itemName, log);
            if (results is null)
            {
                continue;
            }

            if (results.Count == 0)
            {
                info.Clean++;
            }
            else
            {
                info.Findings.Add(new HmiValidationFindingInfo { ObjectKind = kind, Name = itemName, Results = results });
            }
        }

        info.NotValidatable = notValidatable.ToList();
        info.IsComplete = log.IsComplete;
        info.Messages = log.Messages;
        return info;
    }

    /// <summary>
    /// The results with an error or a warning; null when the object could not be fully validated (a recoverable
    /// failure, or a null result), so it is never reported clean.
    /// </summary>
    private static List<HmiValidationResultInfo>? Validate(IValidator validator, string kind, string name, HmiReadLog log)
    {
        var what = $"Validation of {kind} '{name}'";
        var results = new List<HmiValidationResultInfo>();
        try
        {
            var raw = validator.Validate()?.ToList();
            if (raw is null)
            {
                log.Fail($"{what} returned no result list.");
                return null;
            }

            foreach (var result in raw)
            {
                var errors = result?.Errors?.ToList();
                var warnings = result?.Warnings?.ToList();
                if (errors is null || warnings is null)
                {
                    log.Fail($"{what} returned a result without errors or warnings.");
                    return null;
                }

                if (errors.Count > 0 || warnings.Count > 0)
                {
                    results.Add(new HmiValidationResultInfo
                    {
                        PropertyName = log.Try(() => result!.PropertyName, $"The property name of a result of {what}"),
                        Errors = errors,
                        Warnings = warnings,
                    });
                }
            }
        }
        catch (EngineeringException ex)
        {
            log.Recover(ex, $"{what} failed: {ex.Message}");
            return null;
        }

        return results;
    }
    private static string KindOf(object item) => item.GetType().Name;

    private static List<(object Item, string Name)> Objects(HmiSoftware software, string category) => category switch
    {
        "tags" => HmiTagReader.NamedTags(software).Concat(HmiTagReader.NamedSystemTags(software)).ToList(),
        "alarms" => HmiAlarmReader.NamedAlarms(software),
        "screens" => HmiScreenReader.NamedScreens(software),
        "connections" => HmiConnectionReader.NamedConnections(software),
        "logs" => HmiLogReader.NamedLogs(software),
        "alarmClasses" => HmiAlarmReader.NamedAlarmClasses(software),
        "all" => new[]
            {
                HmiTagReader.NamedTags(software), HmiTagReader.NamedSystemTags(software),
                HmiAlarmReader.NamedAlarms(software), HmiAlarmReader.NamedAlarmClasses(software),
                HmiConnectionReader.NamedConnections(software), HmiLogReader.NamedLogs(software),
                HmiScreenReader.NamedScreens(software),
            }.SelectMany(l => l).ToList(),
        _ => throw new WorkerOperationException(
            WorkerFailureCategories.ValidationError, $"'{category}' is not a validate category."),
    };
}
