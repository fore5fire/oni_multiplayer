using System;
using JetBrains.Annotations;
using MultiplayerMod.Core.Dependency;

namespace MultiplayerMod.Platform.Direct.Network;

/// <summary>
/// The local machine's direct-transport identity, generated once and shared (as a DI singleton) between the
/// TCP client and server. This is what makes host <c>SkipHost</c> routing work without Steam: the host's server
/// keys its own loopback client under this exact id, so it recognises "itself" among connected clients — the
/// role Steam's SteamID plays intrinsically.
/// </summary>
[Dependency, UsedImplicitly]
public class LocalPlayerId {

    public DirectMultiplayerClientId Value { get; } = new(Guid.NewGuid());

}
