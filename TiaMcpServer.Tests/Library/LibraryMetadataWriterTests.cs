using Siemens.Engineering;
using Siemens.Engineering.Library;
using TiaMcpServer.Contracts.Library;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Library;
using TiaMcpServer.OpennessWorker.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Library;

public sealed class LibraryMetadataWriterTests
{
    private readonly TiaPortal portal = new();
    private readonly Siemens.Engineering.Project project = new() { Name = "Bound", ProjectLibrary = new ProjectLibrary() };
    private readonly UserGlobalLibrary library = new() { Name = "Fixture", Path = new FileInfo(@"C:\Libraries\Fixture.al21") };
    private readonly List<string> calls = new();
    private int readsAtRollback;
    private int rollbacks, commits;

    public LibraryMetadataWriterTests()
    {
        library.ReadOnlyFailure = new InvalidOperationException("Metadata writes must leave refusal to Portal.");
        portal.GlobalLibraries.Items.Add(library);
        var settings = library.LanguageSettings;
        foreach (var culture in new[] { "en-US", "de-DE", "pl-PL", "fr-FR" }) settings.Languages.Items.Add(Language.Of(culture));
        settings.ActiveLanguages.Items.Add(settings.Languages.Items[0]);
        settings.EditingLanguage = settings.ReferenceLanguage = settings.Languages.Items[0];
        library.Comment.With("en-US", "before").With("de-DE", "unrelated");
        settings.Languages.FindAction = culture => calls.Add("find:" + culture.Name);
        library.Comment.Items.FindAction = language => calls.Add("comment:" + language.Culture.Name);
        foreach (var comment in library.Comment.Items.Items) comment.TextSetAction = _ => calls.Add("set:" + comment.Language.Culture.Name);
        settings.ActiveLanguages.AddAction = language => calls.Add("activate:" + language.Culture.Name);
        settings.EditingLanguageSetAction = language => calls.Add("editing:" + language.Culture.Name);
        settings.ReferenceLanguageSetAction = language => calls.Add("reference:" + language.Culture.Name);
        portal.TransactionAction = transaction =>
        {
            var comments = library.Comment.Items.Items.Select(x => (x, x.Text)).ToList();
            var active = settings.ActiveLanguages.Items.ToList();
            var editing = settings.EditingLanguage; var reference = settings.ReferenceLanguage;
            transaction.RollbackAction = () =>
            {
                readsAtRollback = library.MetadataGetCount;
                rollbacks++;
                foreach (var (comment, text) in comments) { comment.TextSetAction = null; comment.TextFailure = null; comment.Text = text; }
                settings.ActiveLanguages.Items.Clear(); settings.ActiveLanguages.Items.AddRange(active);
                settings.EditingLanguageSetAction = settings.ReferenceLanguageSetAction = null;
                settings.EditingLanguage = editing; settings.ReferenceLanguage = reference;
            };
            transaction.CommitAction = () => commits++;
        };
    }

    private static LibraryWriteRequestInfo Comments(params (string culture, string text)[] values) => new()
    {
        Operation = "set_comments", Scope = "global", LibraryName = "Fixture",
        Comments = values.Select(x => new LibraryCommentInfo { Culture = x.culture, Text = x.text }).ToList()
    };
    private static LibraryWriteRequestInfo Languages() => new()
    {
        Operation = "set_languages", Scope = "global", LibraryName = "Fixture",
        ActivateLanguages = new() { "PL-pl", "en-US" }, EditingLanguage = "fr-FR", ReferenceLanguage = "pl-PL"
    };
    private LibraryWriteResultInfo Write(LibraryWriteRequestInfo item)
        => LibraryWriter.Write(portal, project, LibraryWriteMethods.ForItem(item), item);

    [Fact]
    public void Comments_AllLookupsBeforeSetters_CommitResolvedLibraryAndReturnOnlyRequestedChanges()
    {
        const string text = "\"<&\\\r\n\nZażółć 😀 e\u0301 ";
        var result = Write(Comments(("EN-us", text), ("de-DE", "")));
        Assert.Equal(new[] { "find:en-US", "comment:en-US", "find:de-DE", "comment:de-DE", "set:en-US", "set:de-DE" }, calls);
        Assert.Same(library, Assert.Single(portal.Transactions).Persistence);
        Assert.True(portal.Transactions[0].Committed);
        Assert.Equal(1, commits); Assert.Equal(0, rollbacks);
        Assert.Equal("Fixture", result.Library.LibraryName);
        var change = Assert.IsType<LibraryMetadataChangeInfo>(result.MetadataChange);
        Assert.Equal("before", change.Comments![0].Before); Assert.Equal(text, change.Comments[0].After);
        Assert.Equal("", change.Comments[1].After);
        Assert.Null(change.ActivatedLanguages); Assert.Null(change.EditingLanguage); Assert.Null(change.ReferenceLanguage);
        Assert.Null(result.Metadata);
    }

    [Fact]
    public void Languages_AutoactivateInOrderAndSkipAlreadyActiveAndUnchangedSettings()
    {
        var result = Write(Languages());
        Assert.Equal(new[] { "find:pl-PL", "find:en-US", "find:fr-FR", "activate:pl-PL", "activate:fr-FR", "editing:fr-FR", "reference:pl-PL" }, calls);
        Assert.Equal(new[] { "pl-PL", "fr-FR" }, result.MetadataChange!.ActivatedLanguages);
        Assert.Null(result.MetadataChange.Comments);
        Assert.Equal("en-US", result.MetadataChange.EditingLanguage!.Before);
        calls.Clear();
        Write(new() { Operation = "set_languages", Scope = "global", LibraryName = "Fixture", ActivateLanguages = new() { "pl-PL" }, EditingLanguage = "fr-FR", ReferenceLanguage = "pl-PL" });
        Assert.DoesNotContain(calls, x => x.StartsWith("activate:") || x.StartsWith("editing:") || x.StartsWith("reference:"));
    }

    [Theory]
    [InlineData("language")]
    [InlineData("comment")]
    [InlineData("before")]
    public void MissingLookupOrStrictBeforeRead_PerformsZeroSetters(string stage)
    {
        if (stage == "language") library.LanguageSettings.Languages.Items.RemoveAt(1);
        if (stage == "comment") library.Comment.Items.Items.RemoveAt(1);
        if (stage == "before") library.LanguageSettings.ActiveLanguages.EnumerationFailure = new EngineeringException("cannot read");
        var failure = Assert.Throws<WorkerOperationException>(() => Write(Comments(("en-US", "new"), ("de-DE", "new"))));
        Assert.Equal(stage == "before" ? WorkerFailureCategories.WorkerOperationFailed : WorkerFailureCategories.TargetNotFound, failure.FailureCategory);
        Assert.DoesNotContain(calls, x => x.StartsWith("set:"));
        Assert.Equal(0, commits);
    }

    [Theory]
    [InlineData("setter")]
    [InlineData("mismatch")]
    [InlineData("canCommit")]
    [InlineData("partialRead")]
    public void PrecommitFailure_RollsBackWithoutRereadAndKeepsCapturedObservations(string stage)
    {
        var comments = library.Comment.Items.Items;
        if (stage == "setter") comments[1].TextSetAction = _ => throw new EngineeringTargetInvocationException("refused");
        if (stage == "mismatch") comments[0].TextSetAction = _ => library.LanguageSettings.ActiveLanguages.Items.Add(Language.Of("de-DE"));
        if (stage == "canCommit") portal.TransactionCanCommit = false;
        if (stage == "partialRead") comments[1].TextSetAction = _ => library.LanguageSettings.ActiveLanguages.EnumerationFailure = new EngineeringException("read interrupted");
        var failure = Assert.Throws<WorkerOperationException>(() => Write(Comments(("en-US", "new"), ("de-DE", stage == "mismatch" ? "changed" : "next"))));
        Assert.Equal(1, rollbacks); Assert.Equal(0, commits);
        Assert.Equal(readsAtRollback, library.MetadataGetCount);
        Assert.False(portal.Transactions[0].CommitRequested);
        var change = Assert.IsType<LibraryMetadataChangeInfo>(failure.LibraryWriteOutcome!.MetadataChange);
        Assert.Equal("before", change.Comments![0].Before);
        Assert.Equal(stage == "setter" ? null : "new", change.Comments[0].After);
        Assert.Equal("before", comments[0].Text);
        Assert.Contains("rolled back", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PartialCommentRead_RetainsEarlierObservedTextOnly()
    {
        var comments = library.Comment.Items.Items;
        comments[1].TextSetAction = _ => comments[1].Language.CultureFailure = new EngineeringException("second comment unreadable");
        var failure = Assert.Throws<WorkerOperationException>(() => Write(Comments(("en-US", "new"), ("de-DE", "next"))));
        var changes = failure.LibraryWriteOutcome!.MetadataChange!.Comments!;
        Assert.Equal("new", changes[0].After);
        Assert.Null(changes[1].After);
        Assert.Equal(1, rollbacks);
        Assert.Equal(readsAtRollback, library.MetadataGetCount);
    }

    [Fact]
    public void PartialLanguageRead_RetainsActivationAndEditingButNotUnreachedReference()
    {
        var settings = library.LanguageSettings;
        settings.ReferenceLanguageSetAction = _ => settings.ReferenceLanguageFailure = new EngineeringException("reference unreadable");
        var failure = Assert.Throws<WorkerOperationException>(() => Write(Languages()));
        var change = failure.LibraryWriteOutcome!.MetadataChange!;
        Assert.Equal(new[] { "pl-PL", "fr-FR" }, change.ActivatedLanguages);
        Assert.Equal("en-US", change.EditingLanguage!.Before);
        Assert.Equal("fr-FR", change.EditingLanguage.After);
        Assert.Equal("en-US", change.ReferenceLanguage!.Before);
        Assert.Null(change.ReferenceLanguage.After);
        Assert.Equal(1, rollbacks);
        Assert.Equal(readsAtRollback, library.MetadataGetCount);
        settings.ReferenceLanguageFailure = null;
        Assert.Equal("en-US", settings.EditingLanguage.Culture.Name);
        Assert.Equal("en-US", settings.ReferenceLanguage.Culture.Name);
    }

    [Fact]
    public void LanguageSetterFailure_RollsBackEarlierActivationWithUnobservedAfterValues()
    {
        library.LanguageSettings.ReferenceLanguageSetAction = _ => throw new EngineeringTargetInvocationException("refused reference");
        var failure = Assert.Throws<WorkerOperationException>(() => Write(Languages()));
        Assert.Equal(WorkerFailureCategories.EngineeringRejected, failure.FailureCategory);
        Assert.Equal(1, rollbacks);
        Assert.Equal(readsAtRollback, library.MetadataGetCount);
        Assert.Single(library.LanguageSettings.ActiveLanguages.Items);
        Assert.Null(failure.LibraryWriteOutcome!.MetadataChange!.ActivatedLanguages);
        Assert.Null(failure.LibraryWriteOutcome.MetadataChange.EditingLanguage!.After);
        Assert.Null(failure.LibraryWriteOutcome.MetadataChange.ReferenceLanguage!.After);
    }

    [Fact]
    public void StrictStateRead_PropagatesNativeGetterFailure()
    {
        library.LanguageSettings.ReferenceLanguageFailure = new EngineeringNotSupportedException("unreadable");
        Assert.Throws<EngineeringNotSupportedException>(() => LibraryMetadataWriter.ReadState(library));
    }

    [Theory]
    [InlineData(false, "worker_operation_failed")]
    [InlineData(true, "engineering_rejected")]
    public void NativeFailure_UsesExceptionTypeAndEngineeringEvidence(bool invocation, string category)
    {
        library.Comment.Items.Items[0].TextSetAction = _ => throw (invocation
            ? new EngineeringTargetInvocationException("same message") : new EngineeringNotSupportedException("same message"));
        var failure = Assert.Throws<WorkerOperationException>(() => Write(Comments(("en-US", "new"))));
        Assert.Equal(category, failure.FailureCategory);
        Assert.Equal(invocation ? "EngineeringTargetInvocationException" : "EngineeringNotSupportedException", failure.EngineeringError!.Type);
    }

    [Fact]
    public void NativeActivationRejection_UsesInvocationClassification()
    {
        library.LanguageSettings.ActiveLanguages.AddAction = _ => throw new EngineeringTargetInvocationException("same message");
        var failure = Assert.Throws<WorkerOperationException>(() => Write(Languages()));
        Assert.Equal(WorkerFailureCategories.EngineeringRejected, failure.FailureCategory);
        Assert.Equal("EngineeringTargetInvocationException", failure.EngineeringError!.Type);
    }

    [Theory]
    [InlineData("commit", false)]
    [InlineData("transaction", false)]
    [InlineData("access", false)]
    [InlineData("transaction", true)]
    [InlineData("access", true)]
    public void CommitOrDisposeFailure_HasNoOutcomeAndNeverRereads(string stage, bool earlierSetterError)
    {
        if (earlierSetterError) library.Comment.Items.Items[0].TextSetAction = _ => throw new EngineeringTargetInvocationException("setter");
        var configure = portal.TransactionAction;
        int readsAtFailure = 0;
        portal.TransactionAction = transaction =>
        {
            configure!(transaction);
            if (stage == "commit") transaction.CommitOnDisposeFailure = new EngineeringNotSupportedException("commit");
            if (stage == "transaction") transaction.DisposeFailure = new EngineeringNotSupportedException("transaction");
            transaction.CommitAction = () => { readsAtFailure = library.MetadataGetCount; commits++; };
        };
        if (stage == "access") portal.VciExclusiveAccessDisposeFailure = new EngineeringNotSupportedException("access");
        var failure = Assert.Throws<WorkerOperationException>(() => Write(Comments(("en-US", "new"))));
        Assert.Null(failure.LibraryWriteOutcome);
        Assert.Equal("EngineeringNotSupportedException", failure.EngineeringError!.Type);
        Assert.Contains("Inspect", failure.Message);
        Assert.DoesNotContain("rolled back", failure.Message, StringComparison.OrdinalIgnoreCase);
        if (stage == "access" && !earlierSetterError) Assert.Equal(readsAtFailure, library.MetadataGetCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OriginalFatalFailure_WinsOverDisposeFailure(bool disposeFails)
    {
        library.Comment.Items.Items[0].TextSetAction = _ => throw new NonRecoverableException("fatal");
        if (disposeFails) portal.VciExclusiveAccessDisposeFailure = new EngineeringNotSupportedException("later disposal");
        var failure = Assert.Throws<WorkerOperationException>(() => Write(Comments(("en-US", "new"))));
        Assert.Equal(WorkerFailureCategories.PortalLost, failure.FailureCategory);
        Assert.Null(failure.EngineeringError);
    }

    [Fact]
    public void CurrentExecutionPortalDisposal_WinsOverWrappedEngineeringFailure()
    {
        var revision = 0L;
        library.Comment.Items.Items[0].TextSetAction = _ => { revision++; throw new EngineeringNotSupportedException("gone"); };
        var response = WorkerExecution.Execute(() => { Write(Comments(("en-US", "new"))); return new WorkerResponse { Success = true }; }, () => revision, mutates: true);
        Assert.Equal(WorkerFailureCategories.PortalLost, response.FailureCategory);
        Assert.Null(response.EngineeringError);
    }

    [Fact]
    public void CurrentExecutionPortalDisposal_WinsWhenGenericDisposalReplacesNativeSetterFailure()
    {
        var revision = 0L;
        library.Comment.Items.Items[0].TextSetAction = _ => { revision++; throw new EngineeringNotSupportedException("gone"); };
        portal.VciExclusiveAccessDisposeFailure = new InvalidOperationException("access disposal failed");
        var response = WorkerExecution.Execute(() => { Write(Comments(("en-US", "new"))); return new WorkerResponse { Success = true }; }, () => revision, mutates: true);
        Assert.Equal(WorkerFailureCategories.PortalLost, response.FailureCategory);
        Assert.Null(response.EngineeringError);
        Assert.Null(response.LibraryWriteOutcome);
    }
}
