using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using MultiplayerMod.Core.Patch.ControlFlow;
using MultiplayerMod.Core.Patch.ControlFlow.Evaluators;
using MultiplayerMod.ModRuntime.StaticCompatibility;
using static MultiplayerMod.Multiplayer.StateMachines.Configuration.StateMachineConfigurationPhase;

namespace MultiplayerMod.Multiplayer.StateMachines.Configuration.Configurers;

public class StateMachinePreConfigurer<TStateMachine, TStateMachineInstance, TMaster, TDef>(
    StateMachineRootConfigurer<TStateMachine, TStateMachineInstance, TMaster, TDef> root,
    StateMachineConfigurationContext context,
    TStateMachine stateMachine
) : StateMachineBaseConfigurer<TStateMachine, TStateMachineInstance, TMaster, TDef>(root, context, stateMachine)
    where TStateMachine : GameStateMachine<TStateMachine, TStateMachineInstance, TMaster, TDef>
    where TStateMachineInstance : GameStateMachine<TStateMachine, TStateMachineInstance, TMaster, TDef>.GameInstance
    where TMaster : IStateMachineTarget
{

    private readonly ControlFlowCustomizer customizer = Dependencies.Get<ControlFlowCustomizer>();

    private readonly StateMachineRootConfigurer<TStateMachine, TStateMachineInstance, TMaster, TDef> rootConfigurer =
        root;

    public void PostConfigure(
        Action<StateMachinePostConfigurer<TStateMachine, TStateMachineInstance, TMaster, TDef>> action
    ) => rootConfigurer.PostConfigure(action);

    public void Suppress(Expression<System.Action> expression) {
        // TEMPORARILY DISABLED (diagnosis/unblock). The old mechanism Harmony-patched a shared generic
        // GameStateMachine.State method (ToggleChore/Transition/MoveTo/Enter/...). Mono shares that native code
        // across all instantiations, so the patch fired for every state machine and Harmony's return-value
        // writeback cast the routed result to the wrong instantiation -> InvalidCastException storm on client
        // world-load. To be replaced with an InitializeStates transpiler that guards the specific call sites.
        _ = expression;
    }

    private StateMachine.BaseState ExtractStateInstance(MemberExpression memberExpression) {
        var chain = new LinkedList<MemberInfo>();

        System.Linq.Expressions.Expression current = memberExpression;
        while (current is MemberExpression expression) {
            chain.AddFirst(expression.Member);
            current = expression.Expression;
        }

        if (current is not ConstantExpression constantExpression)
            throw new InvalidStateExpressionException("Only a constant expression of closure instance is supported");

        return (StateMachine.BaseState) chain.Aggregate(constantExpression.Value, (obj, member) => member switch {
            FieldInfo field => field.GetValue(obj),
            PropertyInfo property => property.GetValue(obj),
            _ => throw new InvalidStateExpressionException("Only a closure field or property access chain is supported")
        });
    }

    private (StateMachine.BaseState, MethodInfo) ExtractMethodCallInfo(LambdaExpression expression) {
        if (expression.Body is not MethodCallExpression body)
            throw new InvalidStateExpressionException("Only a method call expression is supported");

        if (body.Object is not MemberExpression memberExpression)
            throw new InvalidStateExpressionException("Only a field or property access is supported");

        return (ExtractStateInstance(memberExpression), body.Method);
    }

}
