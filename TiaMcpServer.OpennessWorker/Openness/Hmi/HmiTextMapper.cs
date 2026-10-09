using Siemens.Engineering;
using TiaMcpServer.Contracts.Hmi;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.Hmi;

using Project = Siemens.Engineering.Project;

public static class HmiTextMapper
{
    /// <summary>
    /// Maps a multilingual property to raw per-culture texts (markup is kept), ordered by culture and
    /// narrowed to <paramref name="language"/> when given. An unreadable item is skipped with a message.
    /// </summary>
    public static HmiText[] Map(MultilingualText? text, string? language, ICollection<string> messages)
    {
        if (text is null)
        {
            return Array.Empty<HmiText>();
        }

        var texts = new List<HmiText>();
        try
        {
            foreach (MultilingualTextItem item in text.Items)
            {
                try
                {
                    var culture = item.Language.Culture.Name;
                    if (language is null || string.Equals(culture, language, StringComparison.OrdinalIgnoreCase))
                    {
                        texts.Add(new HmiText(culture, item.Text));
                    }
                }
                catch (EngineeringException ex) when (HmiReadLog.IsRecoverable(ex))
                {
                    messages.Add($"A text item could not be read: {ex.Message}");
                }
            }
        }
        catch (EngineeringException ex)
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.WorkerOperationFailed, $"The text items could not be read: {ex.Message}");
        }

        return HmiPager.InNameOrder(texts, t => t.Culture).ToArray();
    }

    /// <summary>The culture names of the project's active languages, ordered by name.</summary>
    public static IReadOnlyList<string> ProjectCultures(Project project)
    {
        try
        {
            var cultures = new List<string>();
            foreach (Language language in project.LanguageSettings.ActiveLanguages)
            {
                cultures.Add(language.Culture.Name);
            }

            return HmiPager.InNameOrder(cultures, c => c).ToList();
        }
        catch (EngineeringException ex)
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.WorkerOperationFailed, $"The project languages could not be read: {ex.Message}");
        }
    }

    /// <summary>Fails the item with <c>target_not_found</c>, naming the project languages, unless <paramref name="language"/> is one.</summary>
    public static void RequireProjectLanguage(Project project, string language)
    {
        var cultures = ProjectCultures(project);
        if (!cultures.Contains(language, StringComparer.OrdinalIgnoreCase))
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.TargetNotFound,
                $"'{language}' is not a project language. Project languages: {string.Join(", ", cultures)}.");
        }
    }
}
