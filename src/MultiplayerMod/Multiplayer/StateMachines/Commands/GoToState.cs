using System;
using MultiplayerMod.Multiplayer.Commands;
using MultiplayerMod.Multiplayer.Objects.Reference;
using MultiplayerMod.Multiplayer.StateMachines.RuntimeTools;

namespace MultiplayerMod.Multiplayer.StateMachines.Commands;

[Serializable]
public class GoToState : MultiplayerCommand {

    private readonly Reference<StateMachine.Instance> reference;
    private readonly string? stateName;

    public GoToState(Reference<StateMachine.Instance> reference, StateMachine.BaseState? state) : this(
        reference,
        state?.name
    ) { }

    public GoToState(Reference<StateMachine.Instance> reference, string? stateName) {
        this.stateName = stateName;
        this.reference = reference;
    }

    public override void Execute(MultiplayerCommandContext context) {
        // The client may not currently have this state machine instance (its object isn't in the same chore/state
        // as the host — expected under ONI's non-deterministic sim). Nothing to transition; a hard-sync realigns.
        var instance = reference.Resolve();
        if (instance == null)
            return;
        StateMachineRuntimeTools.Get(instance).GoToState(stateName);
    }

}
