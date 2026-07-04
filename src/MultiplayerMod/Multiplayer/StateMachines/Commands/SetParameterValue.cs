using System;
using MultiplayerMod.Multiplayer.Commands;
using MultiplayerMod.Multiplayer.Objects.Extensions;
using MultiplayerMod.Multiplayer.Objects.Reference;
using MultiplayerMod.Multiplayer.StateMachines.RuntimeTools;

namespace MultiplayerMod.Multiplayer.StateMachines.Commands;

[Serializable]
public class SetParameterValue : MultiplayerCommand {

    private ComponentReference<StateMachineController> controllerReference;
    private Type stateMachineInstanceType;
    private int parameterIndex;
    private object? value;

    public SetParameterValue(StateMachine.Instance smi, StateMachine.Parameter parameter, object? value) {
        var runtime = StateMachineRuntimeTools.Get(smi);
        controllerReference = runtime.GetController().GetReference();
        stateMachineInstanceType = smi.GetType();
        parameterIndex = parameter.idx;
        this.value = ArgumentUtils.WrapObject(value);
    }

    public override void Execute(MultiplayerCommandContext context) {
        // The controller may exist on the client without this state machine instance (sim divergence); skip.
        var instance = controllerReference.Resolve().GetSMI(stateMachineInstanceType);
        if (instance == null)
            return;
        var parameterContext = instance.parameterContexts[parameterIndex];
        var parameterValue = ArgumentUtils.UnWrapObject(value);
        StateMachineContextRuntimeTools.Get(parameterContext).Set(instance, parameterValue);
    }

}
