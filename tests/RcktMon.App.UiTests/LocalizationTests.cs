using System.Globalization;
using Avalonia.Controls;
using Avalonia;
using RcktMon.App.Localization;
using Xunit;

namespace RcktMon.App.UiTests;

/// <summary>
/// The translation layer: resource parity, and text that follows a language change while
/// the window stays open.
/// </summary>
public sealed class LocalizationTests(HeadlessSessionFixture fixture) : IClassFixture<HeadlessSessionFixture>
{
    [Fact]
    public void English_and_Russian_define_exactly_the_same_keys()
    {
        // A key present in one language and missing from the other renders as the raw key
        // on screen, which is the sort of thing that ships because nobody runs the app in
        // the other language.
        HashSet<string> english = Keys(CultureInfo.GetCultureInfo("en"));
        HashSet<string> russian = Keys(CultureInfo.GetCultureInfo("ru"));

        Assert.Empty(english.Except(russian));
        Assert.Empty(russian.Except(english));
        Assert.NotEmpty(english);
    }

    [Fact]
    public void Every_string_differs_between_the_two_languages_unless_it_is_a_proper_noun()
    {
        // Catches a translation that was pasted rather than written. The exceptions are
        // words that genuinely are the same in both.
        string[] identicalOnPurpose = ["Col_Bid", "Col_Ask"];

        foreach (string key in Keys(CultureInfo.GetCultureInfo("en")))
        {
            string english = Localizer.Resources.GetString(key, CultureInfo.GetCultureInfo("en"))!;
            string russian = Localizer.Resources.GetString(key, CultureInfo.GetCultureInfo("ru"))!;

            if (identicalOnPurpose.Contains(key))
            {
                continue;
            }

            Assert.True(english != russian, $"{key} is identical in both languages: '{english}'");
        }
    }

    [Fact]
    public void Format_placeholders_match_between_the_two_languages()
    {
        // A translation that drops a {0} throws at runtime rather than looking wrong, so
        // it is worth catching at build time.
        foreach (string key in Keys(CultureInfo.GetCultureInfo("en")))
        {
            string english = Localizer.Resources.GetString(key, CultureInfo.GetCultureInfo("en"))!;
            string russian = Localizer.Resources.GetString(key, CultureInfo.GetCultureInfo("ru"))!;

            Assert.True(
                Placeholders(english).SetEquals(Placeholders(russian)),
                $"{key} uses different placeholders: '{english}' vs '{russian}'");
        }
    }

    [Fact]
    public void Bound_text_follows_a_language_change()
    {
        // The whole reason the markup extension returns a binding instead of a string: a
        // language setting that needs a restart reads as broken.
        fixture.Run(() =>
        {
            Localizer.Instance.Use("en");

            var text = new TextBlock();
            text.Bind(TextBlock.TextProperty, Localizer.Instance.Observe("Nav_Monitor").ToBinding());

            Assert.Equal("Monitor", text.Text);

            Localizer.Instance.Use("ru");
            Assert.Equal("Монитор", text.Text);

            Localizer.Instance.Use("en");
            Assert.Equal("Monitor", text.Text);
        });
    }

    private static HashSet<string> Keys(CultureInfo culture)
    {
        System.Resources.ResourceSet set = Localizer.Resources.GetResourceSet(
            culture, createIfNotExists: true, tryParents: false)
            ?? throw new InvalidOperationException($"no resources for {culture}");

        return [.. set.Cast<System.Collections.DictionaryEntry>().Select(entry => (string)entry.Key)];
    }

    private static HashSet<string> Placeholders(string value)
    {
        var found = new HashSet<string>();

        foreach (System.Text.RegularExpressions.Match match in
                 System.Text.RegularExpressions.Regex.Matches(value, @"\{\d+(?::[^}]*)?\}"))
        {
            found.Add(match.Value);
        }

        return found;
    }
}
