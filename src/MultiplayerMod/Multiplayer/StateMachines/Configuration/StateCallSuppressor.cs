using System.Collections.Generic;

namespace MultiplayerMod.Multiplayer.StateMachines.Configuration;

/// <summary>
/// Runtime gate consulted by the <c>InitializeStates</c> transpiler (see
/// <see cref="StateCallSuppressionTranspiler"/>). A configurer registers a specific
/// <c>(state instance, method name)</c> pair while a state machine's <c>InitializeStates</c> runs; the transpiled
/// guard at each fluent-State call site skips the call (leaving the receiver on the stack, since these methods
/// return <c>this</c> for chaining) when its <c>(receiver, name)</c> is registered.
///
/// <para>This replaces the old <c>ControlFlowCustomizer</c>/<c>HarmonyGenericsRouter</c> approach, which
/// Harmony-patched a shared generic <c>GameStateMachine.State</c> method — unsound on Mono because that native
/// code is shared across every instantiation (see git history / the InvalidCastException storm on client
/// world-load). Here nothing generic is patched; only the concrete, per-type <c>InitializeStates</c> is
/// transpiled, and suppression is scoped by exact state instance.</para>
///
/// <para>State-machine initialization runs single-threaded on the main (game) thread, matching the previous
/// design, so a plain non-synchronized set is sufficient.</para>
/// </summary>
public static class StateCallSuppressor {

    private static readonly HashSet<(StateMachine.BaseState state, string method)> suppressed = new();

    public static void Suppress(StateMachine.BaseState state, string method) => suppressed.Add((state, method));

    public static void Reset(StateMachine.BaseState state, string method) => suppressed.Remove((state, method));

    /// <summary>Called from transpiled <c>InitializeStates</c> at each guarded call site.</summary>
    public static bool IsSuppressed(StateMachine.BaseState state, string method) => suppressed.Contains((state, method));

}
