// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Condec.Core.Localization;

namespace Condec.Tests;

public partial class LocalizationTests
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en");
    private static readonly CultureInfo Indonesian = CultureInfo.GetCultureInfo("id");

    private static string RepoRoot() =>
        System.Reflection.CustomAttributeExtensions.GetCustomAttributes<System.Reflection.AssemblyMetadataAttribute>(typeof(LocalizationTests).Assembly)
            .Single(a => a.Key == "RepoRoot").Value!;

    private static Dictionary<string, string> ReadResx(string fileName) =>
        XDocument.Load(Path.Combine(RepoRoot(), "src", "Condec.Core", "Resources", fileName))
            .Root!.Elements("data")
            .ToDictionary(e => (string)e.Attribute("name")!, e => (string)e.Element("value")!);

    [Fact]
    public void EveryLanguage_HasExactlyTheEnglishKeys()
    {
        var english = ReadResx("Strings.resx");

        foreach (var language in Languages.Supported.Where(l => l != "en"))
        {
            var translated = ReadResx($"Strings.{language}.resx");
            Assert.Empty(english.Keys.Except(translated.Keys));
            Assert.Empty(translated.Keys.Except(english.Keys));
        }
    }

    [Fact]
    public void EveryLanguage_KeepsThePlaceholders()
    {
        var english = ReadResx("Strings.resx");

        foreach (var language in Languages.Supported.Where(l => l != "en"))
        {
            foreach (var (key, value) in ReadResx($"Strings.{language}.resx"))
            {
                Assert.True(
                    Placeholders(english[key]).SetEquals(Placeholders(value)),
                    $"{language}: '{key}' must use the same {{n}} placeholders as the English text.");
            }
        }
    }

    [Fact]
    public void EveryLanguage_HasNoEmptyText()
    {
        foreach (var language in Languages.Supported)
        {
            var file = language == "en" ? "Strings.resx" : $"Strings.{language}.resx";
            Assert.All(ReadResx(file), pair => Assert.False(string.IsNullOrWhiteSpace(pair.Value), $"{language}: '{pair.Key}' is empty."));
        }
    }

    /// <summary>Catches a key typed wrong in code or XAML before it throws at run time.</summary>
    [Fact]
    public void EveryKeyUsedInTheApp_ExistsInEnglish()
    {
        var english = ReadResx("Strings.resx");
        var root = RepoRoot();
        var used = new HashSet<string>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "src"), "*.*", SearchOption.AllDirectories)
                     .Where(f => (f.EndsWith(".cs") || f.EndsWith(".xaml")) && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")))
        {
            var text = File.ReadAllText(file);
            foreach (Match match in CodeKey().Matches(text))
            {
                used.Add(match.Groups[1].Value);
            }

            foreach (Match match in XamlKey().Matches(text))
            {
                used.Add(match.Groups[1].Value);
            }
        }

        Assert.NotEmpty(used);
        Assert.Empty(used.Except(english.Keys));
    }

    [Fact]
    public void Loc_AnswersInTheRequestedLanguage()
    {
        Assert.Equal("Cancel", Loc.Get("Processing.Cancel", English));
        Assert.Equal("Batal", Loc.Get("Processing.Cancel", Indonesian));
    }

    [Fact]
    public void Loc_FallsBackToEnglishForAnUnknownLanguage()
    {
        Assert.Equal("Cancel", Loc.Get("Processing.Cancel", CultureInfo.GetCultureInfo("fr")));
    }

    [Fact]
    public void Loc_FillsPlaceholders()
    {
        Assert.Equal("Page 2 of 5", string.Format(CultureInfo.InvariantCulture, Loc.Get("Pdf.PageOf", English), 2, 5));
        Assert.Equal("Halaman 2 dari 5", string.Format(CultureInfo.InvariantCulture, Loc.Get("Pdf.PageOf", Indonesian), 2, 5));
    }

    [Fact]
    public void Loc_RejectsAnUnknownKey() =>
        Assert.Throws<KeyNotFoundException>(() => Loc.Get("No.Such.Key", English));

    [Theory]
    [InlineData("id-ID", "id")]
    [InlineData("id", "id")]
    [InlineData("in-ID", "id")]
    [InlineData("en-US", "en")]
    [InlineData("en-GB", "en")]
    [InlineData("fr-FR", "en")]
    public void Pick_MatchesTheLanguageIgnoringTheRegion(string tag, string expected) =>
        Assert.Equal(expected, Languages.Pick([tag]).Name);

    [Fact]
    public void Pick_TakesTheFirstSupportedLanguageInTheList() =>
        Assert.Equal("id", Languages.Pick(["fr-FR", "id-ID", "en-US"]).Name);

    [Fact]
    public void Pick_FallsBackToEnglishWhenNothingMatches()
    {
        Assert.Equal("en", Languages.Pick(["fr-FR", "de-DE"]).Name);
        Assert.Equal("en", Languages.Pick([]).Name);
    }

    private static HashSet<string> Placeholders(string text) =>
        [.. Placeholder().Matches(text).Select(m => m.Value)];

    [GeneratedRegex(@"\{\d+\}")]
    private static partial Regex Placeholder();

    /// <summary>Loc.Get("Key"), Loc.Format("Key", …) and the key table lookups written in code.</summary>
    [GeneratedRegex(@"Loc\.(?:Get|Format)\(\s*(?:culture,\s*)?""([A-Za-z]+\.[A-Za-z.]+)""")]
    private static partial Regex CodeKey();

    /// <summary>local:L.Text="Key", L.Content, L.Header and the other attached properties in XAML.</summary>
    [GeneratedRegex(@"local:L\.\w+=""([A-Za-z]+\.[A-Za-z.]+)""")]
    private static partial Regex XamlKey();
}
