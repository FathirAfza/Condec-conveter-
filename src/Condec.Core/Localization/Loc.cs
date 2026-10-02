// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using System.Globalization;
using System.Resources;

namespace Condec.Core.Localization;

/// <summary>
/// Every sentence the app shows. English is the neutral resource (Resources/Strings.resx); a language is a
/// Strings.&lt;language&gt;.resx next to it, and a string missing there falls back to English.
/// </summary>
public static class Loc
{
    private static readonly ResourceManager Manager = new("Condec.Core.Resources.Strings", typeof(Loc).Assembly);

    /// <summary>
    /// The language the app speaks. The app sets it once at start from the Windows language list
    /// (<see cref="Languages.Pick"/>); until then, and in tests, the thread's UI culture decides.
    /// </summary>
    public static CultureInfo? Culture { get; set; }

    public static string Get(string key, CultureInfo? culture = null) =>
        Manager.GetString(key, culture ?? Culture ?? CultureInfo.CurrentUICulture)
        ?? throw new KeyNotFoundException($"No string named '{key}'.");

    /// <summary>The string with {0}, {1}… filled in. Numbers are written the invariant way: the text carries the style.</summary>
    public static string Format(string key, params object[] args) =>
        string.Format(CultureInfo.InvariantCulture, Get(key), args);

    /// <summary>Same as <see cref="Format(string, object[])"/> for an explicit language.</summary>
    internal static string Format(CultureInfo? culture, string key, params object[] args) =>
        string.Format(CultureInfo.InvariantCulture, Get(key, culture), args);
}
