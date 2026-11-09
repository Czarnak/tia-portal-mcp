using System.Globalization;
using System.Security;
using Siemens.Engineering;
using Siemens.Engineering.Library;
using TiaMcpServer.OpennessWorker.Openness.Library;
using Xunit;

namespace TiaMcpServer.Tests.Library;

public sealed class LibraryMetadataReaderTests
{
    [Fact]
    public void Read_PreservesEmptyValuesAndNullLanguages()
    {
        var library = new GlobalLibrary();
        library.MetadataValues[nameof(library.Version)] = null;
        library.MetadataValues[nameof(library.CreationTime)] = new DateTime(2026, 11, 8, 12, 34, 56, DateTimeKind.Utc);
        library.MetadataValues[nameof(library.LastModified)] = new DateTime(2026, 11, 9, 10, 20, 30, DateTimeKind.Unspecified);
        library.MetadataValues[nameof(library.Size)] = long.MaxValue;
        library.Comment.With("en-US", "");
        var result = LibraryMetadataReader.Read(library);
        Assert.Equal("", result.Author);
        Assert.Null(result.Version);
        Assert.Equal("", result.Family);
        Assert.Equal("", result.Copyright);
        Assert.Equal("", result.LastModifiedBy);
        Assert.Equal("2026-11-08T12:34:56.0000000Z", result.CreationTime);
        Assert.Equal("2026-11-09T10:20:30.0000000", result.LastModified);
        Assert.Equal(long.MaxValue, result.SizeKilobytes);
        Assert.Equal("", Assert.Single(result.Comments!).Text);
        Assert.Null(result.EditingLanguage);
        Assert.Null(result.ReferenceLanguage);
        Assert.Empty(result.SupportedLanguages!);
        Assert.Empty(result.ActiveLanguages!);
        Assert.Empty(result.UsedProducts!);
        Assert.Empty(result.History!);
        Assert.True(result.Complete);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Read_SortsCommentsAndLanguagesOrdinally()
    {
        var library = new GlobalLibrary();
        foreach (var culture in new[] { "pl-PL", "en-US", "de-DE" })
        {
            library.Comment.With(culture, culture);
            library.LanguageSettings.Languages.Items.Add(Language.Of(culture));
            library.LanguageSettings.ActiveLanguages.Items.Add(Language.Of(culture));
        }
        library.LanguageSettings.EditingLanguage = Language.Of("pl-PL");
        library.LanguageSettings.ReferenceLanguage = Language.Of("en-US");
        var result = LibraryMetadataReader.Read(library);
        var expected = new[] { "de-DE", "en-US", "pl-PL" };
        Assert.Equal(expected, result.Comments!.Select(x => x.Culture));
        Assert.Equal(expected, result.Comments!.Select(x => x.Text));
        Assert.Equal(expected, result.SupportedLanguages);
        Assert.Equal(expected, result.ActiveLanguages);
        Assert.Equal("pl-PL", result.EditingLanguage);
        Assert.Equal("en-US", result.ReferenceLanguage);
    }

    [Fact]
    public void Read_PreservesNativeNullCollections()
    {
        var library = new GlobalLibrary();
        foreach (var member in new[] { nameof(library.Comment), nameof(library.LanguageSettings), nameof(library.UsedProducts), nameof(library.HistoryEntries) })
            library.MetadataValues[member] = null;
        var result = LibraryMetadataReader.Read(library);
        Assert.Null(result.Comments);
        Assert.Null(result.SupportedLanguages);
        Assert.Null(result.ActiveLanguages);
        Assert.Null(result.EditingLanguage);
        Assert.Null(result.ReferenceLanguage);
        Assert.Null(result.UsedProducts);
        Assert.Null(result.History);
        Assert.True(result.Complete);
        Assert.Empty(result.Diagnostics);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public void Read_CapsProductsAt100AndHistoryAt50(int extra, bool complete)
    {
        var library = new GlobalLibrary();
        for (var i = 0; i < 100 + extra; i++) library.UsedProducts.Items.Add(new UsedProduct { Name = (100 - i).ToString(CultureInfo.InvariantCulture), Version = "v" });
        for (var i = 0; i < 50 + extra; i++) library.HistoryEntries.Items.Add(new HistoryEntry { Text = (50 - i).ToString(CultureInfo.InvariantCulture), DateTime = new DateTime(2026, 11, 8, 0, 0, 0, DateTimeKind.Utc).AddDays(i) });
        if (extra > 0)
        {
            library.UsedProducts.Items[100].NameFailure = new InvalidOperationException("Must not read beyond the cap.");
            library.HistoryEntries.Items[50].TextFailure = new InvalidOperationException("Must not read beyond the cap.");
        }
        var result = LibraryMetadataReader.Read(library);
        Assert.Equal(Enumerable.Range(1, 100).Reverse().Select(x => x.ToString(CultureInfo.InvariantCulture)), result.UsedProducts!.Select(x => x.Name));
        Assert.All(result.UsedProducts!, x => Assert.Equal("v", x.Version));
        Assert.Equal(Enumerable.Range(1, 50).Reverse().Select(x => x.ToString(CultureInfo.InvariantCulture)), result.History!.Select(x => x.Text));
        Assert.Equal("2026-11-08T00:00:00.0000000Z", result.History![0].DateTime);
        Assert.Equal(complete, result.Complete);
        Assert.Equal(extra * 2, result.Diagnostics.Count);
        if (!complete)
        {
            Assert.Contains(result.Diagnostics, x => x.Contains("usedProducts") && x.Contains("100"));
            Assert.Contains(result.Diagnostics, x => x.Contains("history") && x.Contains("50"));
        }
    }

    [Fact]
    public void Read_DoesNotCapCommentsOrLanguages()
    {
        var library = new GlobalLibrary();
        for (var i = 0; i < 201; i++)
        {
            library.Comment.With("en-US", new string('x', 1000));
            library.LanguageSettings.Languages.Items.Add(Language.Of("en-US"));
            library.LanguageSettings.ActiveLanguages.Items.Add(Language.Of("en-US"));
        }
        var result = LibraryMetadataReader.Read(library);
        Assert.Equal(201, result.Comments!.Count);
        Assert.All(result.Comments!, x => Assert.Equal(1000, x.Text!.Length));
        Assert.Equal(201, result.SupportedLanguages!.Count);
        Assert.Equal(201, result.ActiveLanguages!.Count);
        Assert.True(result.Complete);
        Assert.Empty(result.Diagnostics);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Read_RecoverableGetterFailurePreservesOtherMembers(bool targetInvocation)
    {
        Exception Failure() => targetInvocation ? new EngineeringTargetInvocationException("unsupported") : new EngineeringNotSupportedException("unsupported");
        var library = new GlobalLibrary();
        library.MetadataFailures[nameof(library.Author)] = Failure();
        library.MetadataFailures[nameof(library.Size)] = Failure();
        library.Comment.With("en-US", "ok").With("pl-PL", "bad");
        library.Comment.Items.Items[1].TextFailure = Failure();
        library.LanguageSettings.Languages.EnumerationFailure = Failure();
        library.LanguageSettings.ActiveLanguages.Items.Add(new Language { CultureFailure = Failure() });
        library.LanguageSettings.EditingLanguageFailure = Failure();
        library.UsedProducts.Items.Add(new UsedProduct { Name = "ok" });
        library.UsedProducts.Items.Add(new UsedProduct { NameFailure = Failure() });
        library.HistoryEntries.EnumerationFailure = Failure();
        var result = LibraryMetadataReader.Read(library);
        Assert.Null(result.Author);
        Assert.Null(result.SizeKilobytes);
        Assert.Null(result.Comments);
        Assert.Null(result.SupportedLanguages);
        Assert.Null(result.ActiveLanguages);
        Assert.Null(result.EditingLanguage);
        Assert.Null(result.UsedProducts);
        Assert.Null(result.History);
        Assert.Equal("", result.Family);
        Assert.False(result.Complete);
        Assert.Equal(8, result.Diagnostics.Count);
        Assert.Contains($"author unavailable: {Failure().GetType().Name}", result.Diagnostics);
    }

    [Theory]
    [InlineData("security")]
    [InlineData("nativeSecurity")]
    [InlineData("disposed")]
    [InlineData("fatal")]
    [InlineData("engineering")]
    public void Read_SecurityDisposedAndFatalFailuresEscape(string kind)
    {
        Exception failure = kind switch
        {
            "security" => new SecurityException("denied"),
            "nativeSecurity" => new EngineeringSecurityException("denied"),
            "disposed" => new EngineeringObjectDisposedException("deleted"),
            "fatal" => new NonRecoverableException("lost"),
            _ => new EngineeringException("other")
        };
        var library = new GlobalLibrary();
        library.MetadataFailures[nameof(library.Author)] = failure;
        Assert.Same(failure, Assert.ThrowsAny<Exception>(() => LibraryMetadataReader.Read(library)));
        library.MetadataFailures.Clear();
        library.UsedProducts.Items.Add(new UsedProduct { NameFailure = failure });
        Assert.Same(failure, Assert.ThrowsAny<Exception>(() => LibraryMetadataReader.Read(library)));
    }
}
