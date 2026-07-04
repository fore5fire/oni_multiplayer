using System;
using MultiplayerMod.Multiplayer.Commands;
using MultiplayerMod.Multiplayer.Objects.Extensions;
using MultiplayerMod.Multiplayer.Objects.Reference;

namespace MultiplayerMod.Multiplayer.Chores.Driver.Commands;

[Serializable]
public class ReleaseChoreDriver(ChoreDriver driver) : MultiplayerCommand {

    private readonly ComponentReference<ChoreDriver> driverReference = driver.GetReference();

    public override void Execute(MultiplayerCommandContext context) {
        // The driver may be absent on this client under sim divergence; nothing to release then.
        ChoreDriver driver;
        try {
            driver = driverReference.Resolve();
        } catch (Exception) {
            return;
        }
        context.Dependencies.Get<MultiplayerDriverChores>().Release(driver);
    }

}
