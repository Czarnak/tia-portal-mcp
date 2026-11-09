using System;
using System.Globalization;
using Siemens.Engineering;

namespace TiaMcpServer.OpennessReferenceProbe
{
    // Compile-only V21 signatures; never executed by the verifier.
    internal static class LibraryMetadataReferenceSurface
    {
        private static void Metadata(LanguageSettings settings, MultilingualText comment, Language language)
        {
            Func<CultureInfo, Language> findLanguage = settings.Languages.Find;
            Action<Language> activate = settings.ActiveLanguages.Add;
            settings.EditingLanguage = language;
            settings.ReferenceLanguage = language;
            Func<Language, MultilingualTextItem> findComment = comment.Items.Find;
            findComment(language).Text = "";
        }
    }
}
