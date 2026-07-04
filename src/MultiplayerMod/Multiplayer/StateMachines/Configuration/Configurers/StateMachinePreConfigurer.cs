using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
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

    private readonly StateMachineRootConfigurer<TStateMachine, TStateMachineInstance, TMaster, TDef> rootConfigurer =
        root;

    public void PostConfigure(
        Action<StateMachinePostConfigurer<TStateMachine, TStateMachineInstance, TMaster, TDef>> action
    ) => rootConfigurer.PostConfigure(action);

    /// <summary>
    /// Suppresses a specific fluent-State call (e.g. <c>state.ToggleChore(...)</c>) for the duration of this state
    /// machine's <c>InitializeStates</c>. The call site is guarded at patch time by
    /// <see cref="StateCallSuppressionTranspiler"/>; here we register the exact <c>(state instance, method name)</c>
    /// so only that call is skipped (leaving the receiver for chaining). Replaces the old shared-generic Harmony
    /// detour, which crashed on Mono (see <see cref="StateCallSuppressor"/>).
    /// </summary>
    public void Suppress(Expression<System.Action> expression) {
        var (state, method) = ExtractMethodCallInfo(expression);
        if (!StateCallSuppressionTranspiler.GuardedMethods.Contains(method.Name))
            throw new InvalidStateExpressionException(
                $"Suppress target '{method.Name}' is not guarded by the InitializeStates transpiler. " +
                $"Add it to {nameof(StateCallSuppressionTranspiler)}.{nameof(StateCallSuppressionTranspiler.GuardedMethods)}."
            );
        var name = method.Name;
        rootConfigurer.AddAction(ControlFlowApply, _ => StateCallSuppressor.Suppress(state, name));
        rootConfigurer.AddAction(ControlFlowReset, _ => StateCallSuppressor.Reset(state, name));
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
