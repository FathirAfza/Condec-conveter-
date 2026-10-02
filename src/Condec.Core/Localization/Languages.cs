// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using System.Globalization;

namespace Condec.Core.Localization;

/// <summary>Which of the languages Condec has strings for suits the user's Windows language list.</summary>
public static class Languages
{
    /// <summary>
    /// The languages with their own Strings.&lt;language&gt;.resx. English is also the neutral resource, so it is
    /// the fallback. Adding a language means adding its resx file and its code here.
    /// </summary>
    public static IReadOnlyList<string> Supported { get; } = ["en", "id"];

    /// <summary>
    /// The first language in <paramref name="preferred"/> (BCP 47 tags such as "id-ID", most wanted first) that
    /// Condec supports, or English when none is. The region doesn't matter: "en-GB" and "en-US" are both English.
    /// </summary>
    public static CultureInfo Pick(IEnumerable<string> preferred)
    {
        foreach (var tag in preferred)
        {
            var language = Normalize(tag);
            if (Supported.Contains(language))
            {
                return CultureInfo.GetCultureInfo(language);
            }
        }

        return CultureInfo.GetCultureInfo("en");
    }

    private static string Normalize(string tag)
    {
        var language = tag.Split('-', '_')[0].Trim().ToLowerInvariant();

        // Windows and older .NET versions used "in" for Indonesian before ISO 639 renamed it "id".
        return language == "in" ? "id" : language;
    }
}
