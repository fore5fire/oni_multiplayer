using System;
using MultiplayerMod.Network;

namespace MultiplayerMod.Platform.Direct.Network;

/// <summary>
/// Peer identity for the direct transport. Unlike Steam (where identity is intrinsic to the socket), the direct
/// transport has no built-in identity, so each peer generates its own <see cref="Guid"/> and announces it on
/// connect. This is what removes the same-Steam-account collision: identity is decoupled from Steam entirely.
/// </summary>
[Serializable]
public record DirectMultiplayerClientId(Guid Id) : IMultiplayerClientId {

    public bool Equals(IMultiplayerClientId other) {
        return other is DirectMultiplayerClientId player && player.Equals(this);
    }

    public override string ToString() => $"Direct({Id.ToString().Substring(0, 8)})";

}
