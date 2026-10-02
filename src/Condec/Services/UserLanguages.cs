// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using System.Globalization;
using Windows.System.UserProfile;

namespace Condec.Services;

/// <summary>The language list from Windows settings (Time and language), most wanted first.</summary>
internal static class UserLanguages
{
    public static IReadOnlyList<string> Get()
    {
        try
        {
            // The user's own list, whether or not the app is packaged: a package's manifest would narrow
            // ApplicationLanguages.Languages to the languages it declares.
            return [.. GlobalizationPreferences.Languages];
        }
        catch (Exception)
        {
            // No language list to read: the thread's UI culture is the next best answer.
            return [CultureInfo.CurrentUICulture.Name];
        }
    }
}
