using System;
using MultiplayerMod.Multiplayer.Commands;

namespace MultiplayerMod.Platform.Direct.Network;

/// <summary>
/// Probe command for the direct-transport loopback self-test. Its inherited <see cref="MultiplayerCommand.Id"/>
/// is matched on the receiving side to confirm the exact instance round-tripped. Executing it is a no-op — the
/// self-test only checks delivery, not effects.
/// </summary>
[Serializable]
public class DirectSelfTestCommand : MultiplayerCommand {

    public override void Execute(MultiplayerCommandContext context) {
        // Intentionally empty: this command exists only to verify transport delivery.
    }

}
