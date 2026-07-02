using System;
using JetBrains.Annotations;
using MultiplayerMod.Core.Dependency;
using MultiplayerMod.Multiplayer.Players;

namespace MultiplayerMod.Platform.Direct;

/// <summary>
/// Supplies the local player's profile for the direct transport, where there is no Steam persona to read from.
/// The name comes from <c>MP_NAME</c> (or the machine name), so peers show up distinctly even under one account.
/// </summary>
[Dependency, UsedImplicitly]
public class DirectPlayerProfileProvider : IPlayerProfileProvider {

    private readonly Lazy<PlayerProfile> profile = new(() => new PlayerProfile(DirectNetworkConfig.PlayerName));

    public PlayerProfile GetPlayerProfile() => profile.Value;

}
