using System;
using MultiplayerMod.Multiplayer.Commands;
using MultiplayerMod.Multiplayer.Objects.Reference;
using MultiplayerMod.Multiplayer.StateMachines.Configuration.Parameters;
using MultiplayerMod.Multiplayer.StateMachines.Configuration.States;
using MultiplayerMod.Multiplayer.StateMachines.RuntimeTools;

namespace MultiplayerMod.Multiplayer.StateMachines.Commands;

[Serializable]
public class MoveObjectToCell : MultiplayerCommand {

    public static StateMachineMultiplayerParameterInfo<int> TargetCell = new(
        "__move_to_target_cell",
        defaultValue: Grid.InvalidCell
    );

    private readonly Reference<StateMachine.Instance> reference;
    private readonly string? movingStateName;
    private readonly int cell;

    public MoveObjectToCell(Reference<StateMachine.Instance> reference, int cell, StateMachine.BaseState? movingState) :
        this(reference, cell, movingState?.name) { }

    public MoveObjectToCell(
        Reference<StateMachine.Instance> reference,
        int cell,
        StateMachineMultiplayerStateInfo? movingStateInfo
    ) : this(reference, cell, movingStateInfo?.ReferenceName) { }

    public MoveObjectToCell(Reference<StateMachine.Instance> reference, int cell, string? movingStateName) {
        this.movingStateName = movingStateName;
        this.reference = reference;
        this.cell = cell;
    }

    public override void Execute(MultiplayerCommandContext context) {
        // See GoToState: the referenced state machine instance may be absent on the client under sim divergence.
        var instance = reference.Resolve();
        if (instance == null)
            return;
        var runtime = StateMachineRuntimeTools.Get(instance);
        runtime.FindParameter(TargetCell)?.Set(cell);
        runtime.GoToState(movingStateName);
    }

}
