using System;
using MultiplayerMod.Platform.Direct;

namespace MultiplayerMod.Platform;

/// <summary>
/// Selects the active network transport at load time. Only one transport's dependencies may be registered — two
/// <c>IMultiplayerServer</c>/<c>IMultiplayerClient</c> registrations would make the container throw
/// <c>AmbiguousDependencyException</c>. Rather than conditionally attributing classes, the loader filters the DI
/// scan so the non-selected platform's dependencies are simply not registered. Steam is the default; setting
/// <c>MP_TRANSPORT=direct</c> switches to the LAN transport. Shared serialization (under Platform.Steam.Network.
/// Messaging) is instantiated directly, not DI-scanned, so excluding a platform's namespace never removes it.
/// </summary>
public static class PlatformSelection {

    private const string SteamNamespace = "MultiplayerMod.Platform.Steam";
    private const string DirectNamespace = "MultiplayerMod.Platform.Direct";

    public static bool DirectSelected => DirectNetworkConfig.DirectTransportSelected;

    /// <summary>Predicate for <c>ScanAssembly</c>: excludes the non-selected platform's types from DI.</summary>
    public static bool IncludeInDependencyScan(Type type) {
        var ns = type.Namespace;
        if (ns == null)
            return true;

        var excludedNamespace = DirectSelected ? SteamNamespace : DirectNamespace;
        return !ns.StartsWith(excludedNamespace, StringComparison.Ordinal);
    }

}
