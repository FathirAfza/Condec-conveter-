// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using System.Reflection;

namespace Condec.Tests;

/// <summary>
/// Condec must never touch the network. This guards the portable core against
/// accidentally pulling in networking assemblies.
/// </summary>
public class OfflineGuaranteeTests
{
    private static readonly string[] ForbiddenAssemblyPrefixes =
    [
        "System.Net.Http",
        "System.Net.Sockets",
        "System.Net.WebClient",
        "System.Net.Requests",
        "System.Net.WebSockets",
        "System.Net.NetworkInformation",
        "System.Net.Mail",
        "System.Net.Quic",
    ];

    [Fact]
    public void CoreDoesNotReferenceNetworkingAssemblies()
    {
        var core = Assembly.Load("Condec.Core");

        var offending = core.GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .Where(name => ForbiddenAssemblyPrefixes.Any(p => name.StartsWith(p, StringComparison.Ordinal)))
            .ToList();

        Assert.Empty(offending);
    }
}
