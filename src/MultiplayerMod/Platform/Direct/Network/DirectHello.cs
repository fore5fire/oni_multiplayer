using System;

namespace MultiplayerMod.Platform.Direct.Network;

/// <summary>
/// First frame a client sends after connecting. It announces the client's self-generated identity so the server
/// can key the connection — the direct transport's substitute for Steam's intrinsic peer identity. The player
/// profile (name) is exchanged separately at the application layer via InitializeClientCommand, exactly as with
/// the Steam transport.
/// </summary>
[Serializable]
public class DirectHello {

    public Guid ClientId { get; }

    public DirectHello(Guid clientId) {
        ClientId = clientId;
    }

}
