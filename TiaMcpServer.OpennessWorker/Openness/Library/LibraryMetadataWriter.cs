using System.Globalization;
using Siemens.Engineering;
using Siemens.Engineering.Library;
using TiaMcpServer.Contracts.Library;
using TiaMcpServer.Contracts.Shared;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.Library;

internal static class LibraryMetadataWriter
{
    internal static void Plan(GlobalLibrary library, LibraryWriteRequestInfo item, LibraryWritePlanInfo plan)
    {
        var before = ReadState(library);
        var after = new LibraryMetadataState
        {
            Comments = new(before.Comments, StringComparer.Ordinal),
            ActiveLanguages = before.ActiveLanguages.ToList(), ActiveLanguagesRead = true,
            EditingLanguage = before.EditingLanguage, ReferenceLanguage = before.ReferenceLanguage
        };
        var settings = library.LanguageSettings;
        Language Resolve(string culture) => settings.Languages.Find(CultureInfo.GetCultureInfo(culture))
            ?? throw Missing("language", culture);
        if (item.Operation == "set_comments")
        {
            foreach (var comment in item.Comments!)
            {
                var culture = CultureNameRules.CanonicalName(comment.Culture)!;
                var language = Resolve(culture);
                if (library.Comment.Items.Find(language) is null)
                {
                    if (before.ActiveLanguages.Contains(culture)) throw Missing("comment", culture);
                    var root = plan.Library!;
                    (plan.MissingTargets ??= new()).Add(new LibraryIdentityInfo
                    {
                        Scope = root.Scope, LibraryName = root.LibraryName, LibraryPath = root.LibraryPath,
                        Kind = "comment", Name = culture
                    });
                }
                after.Comments[culture] = comment.Text;
            }
        }
        else
        {
            foreach (var culture in LibraryMetadataState.RequestedLanguages(item))
            {
                _ = Resolve(culture);
                if (!after.ActiveLanguages.Contains(culture)) after.ActiveLanguages.Add(culture);
            }
            if (item.EditingLanguage is not null) after.EditingLanguage = CultureNameRules.CanonicalName(item.EditingLanguage);
            if (item.ReferenceLanguage is not null) after.ReferenceLanguage = CultureNameRules.CanonicalName(item.ReferenceLanguage);
        }
        plan.MetadataChange = Change(item, before, after);
    }

    internal static LibraryWriteResultInfo Write(TiaPortal portal, ProjectBase project, LibraryWriteRequestInfo item)
    {
        var library = (GlobalLibrary)LibraryWriteTargets.Library(portal, project, item.Scope, item.LibraryPath, item.LibraryName);
        var identity = LibraryIdentities.Identity(project, library);
        LibraryMetadataState? before = null, after = null;
        Exception? pending = null, commitFailure = null, transactionFailure = null;
        try
        {
            using (var access = portal.ExclusiveAccess(item.Operation))
            {
                try
                {
                    using (var transaction = access.Transaction(library, item.Operation))
                    {
                        try
                        {
                            var settings = library.LanguageSettings;
                            var comments = new List<(MultilingualTextItem Target, string Text)>();
                            var languages = new Dictionary<string, Language>(StringComparer.Ordinal);
                            Language Resolve(string culture)
                            {
                                var canonical = CultureNameRules.CanonicalName(culture)!;
                                if (!languages.TryGetValue(canonical, out var language))
                                {
                                    language = settings.Languages.Find(CultureInfo.GetCultureInfo(canonical))
                                        ?? throw Missing("language", canonical);
                                    languages.Add(canonical, language);
                                }
                                return language;
                            }
                            if (item.Operation == "set_comments")
                                foreach (var comment in item.Comments!)
                                {
                                    var language = Resolve(comment.Culture!);
                                    var target = library.Comment.Items.Find(language) ?? throw Missing("comment", language.Culture.Name);
                                    comments.Add((target, comment.Text!));
                                }
                            else
                                foreach (var culture in LibraryMetadataState.RequestedLanguages(item)) Resolve(culture);

                            before = ReadState(library);
                            foreach (var comment in comments) comment.Target.Text = comment.Text;
                            if (item.Operation == "set_languages")
                            {
                                foreach (var culture in LibraryMetadataState.RequestedLanguages(item))
                                    if (!before.ActiveLanguages.Contains(culture)) settings.ActiveLanguages.Add(languages[culture]);
                                if (item.EditingLanguage is not null && CultureNameRules.CanonicalName(item.EditingLanguage) != before.EditingLanguage)
                                    settings.EditingLanguage = languages[CultureNameRules.CanonicalName(item.EditingLanguage)!];
                                if (item.ReferenceLanguage is not null && CultureNameRules.CanonicalName(item.ReferenceLanguage) != before.ReferenceLanguage)
                                    settings.ReferenceLanguage = languages[CultureNameRules.CanonicalName(item.ReferenceLanguage)!];
                            }
                            after = new LibraryMetadataState();
                            ReadState(library, after);
                            var mismatches = LibraryMetadataState.Mismatches(item, before, after);
                            if (mismatches.Count != 0)
                                throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed,
                                    "Library metadata end-state mismatch: " + string.Join(", ", mismatches));
                            if (!transaction.CanCommit)
                                throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed, "The library transaction cannot commit.");
                        }
                        catch (Exception exception) { pending = exception; }
                        // Commit and scope disposal have unknown outcomes on failure, unlike a completed rollback.
                        if (pending is null)
                        {
                            try { transaction.CommitOnDispose(); }
                            catch (Exception exception) { commitFailure = exception; throw; }
                        }
                    }
                }
                catch (Exception exception) { transactionFailure = exception; throw; }
            }
        }
        catch (Exception exception)
        {
            var earlier = new[] { pending, commitFailure, transactionFailure };
            var originalFatal = earlier.FirstOrDefault(failure => failure is NonRecoverableException
                || failure is WorkerOperationException wrapped && wrapped.FailureCategory == WorkerFailureCategories.PortalLost);
            throw Failure(originalFatal ?? exception,
                "Library metadata state is unknown. Inspect the library before retrying.", null,
                earlier.OfType<EngineeringException>().FirstOrDefault());
        }
        if (pending is not null)
            throw Failure(pending, "The library metadata transaction was rolled back.",
                before is null ? null : new LibraryWriteOutcomeInfo { Library = identity, MetadataChange = Change(item, before, after) });
        return new LibraryWriteResultInfo { Library = identity, MetadataChange = Change(item, before!, after) };
    }

    internal static LibraryMetadataState ReadState(GlobalLibrary library)
    {
        var state = new LibraryMetadataState();
        ReadState(library, state);
        return state;
    }

    private static void ReadState(GlobalLibrary library, LibraryMetadataState state)
    {
        foreach (var comment in library.Comment.Items)
            state.Comments.Add(comment.Language.Culture.Name, comment.Text);
        var settings = library.LanguageSettings;
        state.ActiveLanguages = settings.ActiveLanguages.Select(x => x.Culture.Name).ToList();
        state.ActiveLanguagesRead = true;
        state.EditingLanguage = settings.EditingLanguage?.Culture.Name;
        state.ReferenceLanguage = settings.ReferenceLanguage?.Culture.Name;
    }

    private static LibraryMetadataChangeInfo Change(LibraryWriteRequestInfo item, LibraryMetadataState before, LibraryMetadataState? after)
        => new()
        {
            Comments = item.Operation != "set_comments" ? null : item.Comments!.Select(comment =>
            {
                var culture = CultureNameRules.CanonicalName(comment.Culture)!;
                before.Comments.TryGetValue(culture, out var oldText);
                string? newText = null;
                after?.Comments.TryGetValue(culture, out newText);
                return new LibraryCommentChangeInfo { Culture = culture, Before = oldText, After = newText };
            }).ToList(),
            ActivatedLanguages = item.Operation != "set_languages" || after?.ActiveLanguagesRead != true ? null
                : after.ActiveLanguages.Where(x => !before.ActiveLanguages.Contains(x)).ToList(),
            EditingLanguage = item.EditingLanguage is null ? null : new LibraryLanguageChangeInfo { Before = before.EditingLanguage, After = after?.EditingLanguage },
            ReferenceLanguage = item.ReferenceLanguage is null ? null : new LibraryLanguageChangeInfo { Before = before.ReferenceLanguage, After = after?.ReferenceLanguage }
        };

    private static WorkerOperationException Missing(string kind, string culture)
        => new(WorkerFailureCategories.TargetNotFound, $"The requested library {kind} '{culture}' was not found.");

    private static WorkerOperationException Failure(Exception exception, string context, LibraryWriteOutcomeInfo? outcome,
        EngineeringException? earlierEngineering = null)
    {
        var category = exception is NonRecoverableException ? WorkerFailureCategories.PortalLost
            : exception is EngineeringException native ? WorkerExecution.Classify(native, true)
            : exception is WorkerOperationException failure ? failure.FailureCategory : WorkerFailureCategories.WorkerOperationFailed;
        return new WorkerOperationException(category, context + " " + exception.Message,
            engineeringError: category == WorkerFailureCategories.PortalLost ? null
                : exception is EngineeringException engineering ? WorkerExecution.Describe(engineering)
                : (exception as WorkerOperationException)?.EngineeringError
                    ?? (earlierEngineering is null ? null : WorkerExecution.Describe(earlierEngineering)),
            libraryWriteOutcome: outcome);
    }
}
