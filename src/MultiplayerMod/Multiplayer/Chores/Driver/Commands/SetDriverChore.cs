using System;
using MultiplayerMod.Core.Logging;
using MultiplayerMod.Multiplayer.Commands;
using MultiplayerMod.Multiplayer.Objects.Extensions;
using MultiplayerMod.Multiplayer.Objects.Reference;

namespace MultiplayerMod.Multiplayer.Chores.Driver.Commands;

[Serializable]
public class SetDriverChore(ChoreDriver driver, ChoreConsumer consumer, Chore chore, object data) : MultiplayerCommand {

    private static Core.Logging.Logger log = LoggerFactory.GetLogger<SetDriverChore>();

    private ComponentReference<ChoreDriver> driverReference = driver.GetReference();
    private ComponentReference<ChoreConsumer> consumerReference = consumer.GetReference();
    private ChoreReference choreReference = chore.GetReference();
    private object? data = ArgumentUtils.WrapObject(data);

    public override void Execute(MultiplayerCommandContext context) {
        // TODO: A temporary solution until all chores are synced.
        // Any of the referenced objects (chore/driver/consumer) may be absent or missing components on this
        // client under sim divergence — resolve them all defensively and skip if anything is unavailable.
        ChoreDriver driver;
        Chore.Precondition.Context choreContext;
        try {
            var chore = choreReference.Resolve();
            driver = driverReference.Resolve();
            choreContext = new Chore.Precondition.Context(
                chore,
                new ChoreConsumerState(consumerReference.Resolve()),
                is_attempting_override: false,
                ArgumentUtils.UnWrapObject(data)
            );
        } catch (Exception exception) {
            log.Warning($"Unable to set driver chore (object unavailable on this client): {exception.Message}");
            return;
        }
        context.Dependencies.Get<MultiplayerDriverChores>().Set(driver, ref choreContext);
    }

}
