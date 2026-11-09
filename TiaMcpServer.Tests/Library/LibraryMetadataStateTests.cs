using TiaMcpServer.Contracts.Library;
using TiaMcpServer.OpennessWorker.Openness.Library;
using Xunit;

namespace TiaMcpServer.Tests.Library;

public sealed class LibraryMetadataStateTests
{
    private static LibraryMetadataState State() => new()
    {
        Comments = new() { ["en-US"] = "old", ["de-DE"] = "unrelated\r\nä" },
        ActiveLanguages = new() { "en-US", "de-DE" },
        EditingLanguage = "en-US", ReferenceLanguage = "de-DE"
    };

    [Theory]
    [InlineData("\"<&\\\r\n\nZażółć 😀 e\u0301 ")]
    [InlineData("")]
    public void MetadataState_OrdinalChangesAndUnrelatedText(string text)
    {
        var item = new LibraryWriteRequestInfo { Operation = "set_comments", Comments = new() { new() { Culture = "EN-us", Text = text } } };
        var before = State();
        var after = State();
        after.Comments["en-US"] = text;
        Assert.Empty(LibraryMetadataState.Mismatches(item, before, after));
        if (text == "")
        {
            after.Comments.Remove("en-US");
            Assert.Empty(LibraryMetadataState.Mismatches(item, before, after));
        }
        after.Comments["de-DE"] = "altered";
        Assert.NotEmpty(LibraryMetadataState.Mismatches(item, before, after));
        after = State();
        after.Comments["en-US"] = text + "x";
        Assert.NotEmpty(LibraryMetadataState.Mismatches(item, before, after));
    }

    [Theory]
    [InlineData("active")]
    [InlineData("editing")]
    [InlineData("reference")]
    [InlineData("extraComment")]
    public void Comments_RejectUnrequestedSideEffects(string sideEffect)
    {
        var item = new LibraryWriteRequestInfo { Operation = "set_comments", Comments = new() { new() { Culture = "en-US", Text = "new" } } };
        var before = State(); var after = State(); after.Comments["en-US"] = "new";
        switch (sideEffect)
        {
            case "active": after.ActiveLanguages.Add("pl-PL"); break;
            case "editing": after.EditingLanguage = "de-DE"; break;
            case "reference": after.ReferenceLanguage = "en-US"; break;
            case "extraComment": after.Comments["pl-PL"] = ""; break;
        }
        Assert.NotEmpty(LibraryMetadataState.Mismatches(item, before, after));
    }

    [Theory]
    [InlineData("removeOld")]
    [InlineData("extraLanguage")]
    [InlineData("missingActivation")]
    [InlineData("editing")]
    [InlineData("reference")]
    [InlineData("oldComment")]
    [InlineData("newNonempty")]
    [InlineData("unrelatedEmpty")]
    public void MetadataState_OnlyRequestedLanguageChanges(string sideEffect)
    {
        var item = new LibraryWriteRequestInfo { Operation = "set_languages", ActivateLanguages = new() { "PL-pl" }, EditingLanguage = "fr-FR" };
        var before = State(); var after = State();
        after.ActiveLanguages.AddRange(new[] { "pl-PL", "fr-FR" });
        after.EditingLanguage = "fr-FR";
        after.Comments["pl-PL"] = "";
        Assert.Empty(LibraryMetadataState.Mismatches(item, before, after));
        switch (sideEffect)
        {
            case "removeOld": after.ActiveLanguages.Remove("de-DE"); break;
            case "extraLanguage": after.ActiveLanguages.Add("ja-JP"); break;
            case "missingActivation": after.ActiveLanguages.Remove("pl-PL"); break;
            case "editing": after.EditingLanguage = "en-US"; break;
            case "reference": after.ReferenceLanguage = "en-US"; break;
            case "oldComment": after.Comments["en-US"] = "changed"; break;
            case "newNonempty": after.Comments["pl-PL"] = "unexpected"; break;
            case "unrelatedEmpty": after.Comments["ja-JP"] = ""; break;
        }
        Assert.NotEmpty(LibraryMetadataState.Mismatches(item, before, after));
    }
}
