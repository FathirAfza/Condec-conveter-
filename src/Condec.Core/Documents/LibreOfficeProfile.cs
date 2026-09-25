// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using System.Xml.Linq;

namespace Condec.Core.Documents;

/// <summary>
/// The LibreOffice user profile Condec runs with, kept apart from the user's own profile so a LibreOffice
/// window the user has open can't take over the conversion. Keeping it between runs saves the several
/// seconds LibreOffice spends creating a profile.
/// </summary>
/// <remarks>
/// LibreOffice's update check runs only on the <c>onFirstVisibleTask</c> event (share/registry/onlineupdate.xcd),
/// which headless mode never raises. It is still switched off here, so the profile never checks for updates
/// even if that changes. LibreOffice turns it on while creating a new profile, even when the setting is
/// written beforehand, so it is switched off again after every run.
/// </remarks>
public static class LibreOfficeProfile
{
    private static readonly XNamespace Oor = "http://openoffice.org/2001/registry";

    private const string UpdateCheckPath = "/org.openoffice.Office.Jobs/Jobs/org.openoffice.Office.Jobs:Job['UpdateCheck']/Arguments";

    /// <summary>Sets <c>AutoCheckEnabled</c> to false in the profile's registrymodifications.xcu, if the profile exists yet.</summary>
    /// <returns>True when the file had to be changed.</returns>
    public static bool DisableUpdateCheck(string profileDirectory)
    {
        var file = Path.Combine(profileDirectory, "user", "registrymodifications.xcu");
        if (!File.Exists(file))
        {
            return false;
        }

        var document = XDocument.Load(file);
        var root = document.Root ?? throw new InvalidDataException("registrymodifications.xcu has no root element.");

        var property = root.Elements("item")
            .Where(item => (string?)item.Attribute(Oor + "path") == UpdateCheckPath)
            .Elements("prop")
            .FirstOrDefault(prop => (string?)prop.Attribute(Oor + "name") == "AutoCheckEnabled");

        if (property is null)
        {
            property = new XElement("prop", new XAttribute(Oor + "name", "AutoCheckEnabled"), new XAttribute(Oor + "op", "fuse"));
            root.Add(new XElement("item", new XAttribute(Oor + "path", UpdateCheckPath), property));
        }
        else if ((string?)property.Element("value") == "false")
        {
            return false;
        }

        property.SetElementValue("value", "false");

        var temporary = file + ".condec-tmp";
        document.Save(temporary);
        File.Move(temporary, file, overwrite: true);
        return true;
    }
}
