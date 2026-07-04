using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace MultiplayerMod.Multiplayer.StateMachines.Configuration;

/// <summary>
/// Transpiler applied to each configured state machine's concrete <c>InitializeStates</c>. It wraps every call to
/// a fluent <c>GameStateMachine.State</c> method (the ones the chore synchronizers suppress) in a guard:
/// <code>if (StateCallSuppressor.IsSuppressed(receiver, name)) { skip the call, leave receiver } else { call }</code>
/// The guard fires only for state instances a configurer registered for the current init (see
/// <see cref="StateCallSuppressor"/>), so unregistered calls run unchanged. Because only the concrete,
/// per-type <c>InitializeStates</c> is rewritten — never a shared generic method — this avoids the Mono
/// generic-code-sharing crash the old detour approach hit.
/// </summary>
public static class StateCallSuppressionTranspiler {

    /// <summary>Fluent <c>State</c> methods the chore synchronizers suppress. Kept in sync with the
    /// <c>Suppress(...)</c> call sites; <see cref="Configurers.StateMachinePreConfigurer{T,I,M,D}.Suppress"/>
    /// asserts a suppressed method's name is in this set so a future addition fails loudly.</summary>
    public static readonly HashSet<string> GuardedMethods = new() {
        "ToggleChore", "ToggleRecurringChore", "ToggleScheduleCallback", "Update", "Transition", "MoveTo", "Enter"
    };

    private static readonly MethodInfo isSuppressed = AccessTools.Method(
        typeof(StateCallSuppressor),
        nameof(StateCallSuppressor.IsSuppressed)
    );

    public static IEnumerable<CodeInstruction> Transpile(
        IEnumerable<CodeInstruction> instructions,
        ILGenerator generator
    ) {
        var result = new List<CodeInstruction>();
        foreach (var instruction in instructions) {
            if (TryGetGuardedTarget(instruction, out var method)) {
                EmitGuardedCall(result, generator, instruction, method!);
                continue;
            }
            result.Add(instruction);
        }
        return result;
    }

    private static bool TryGetGuardedTarget(CodeInstruction instruction, out MethodInfo? method) {
        method = null;
        if (instruction.opcode != OpCodes.Call && instruction.opcode != OpCodes.Callvirt)
            return false;
        if (instruction.operand is not MethodInfo candidate)
            return false;
        if (!GuardedMethods.Contains(candidate.Name))
            return false;

        var declaringType = candidate.DeclaringType;
        if (declaringType == null || !typeof(StateMachine.BaseState).IsAssignableFrom(declaringType))
            return false;

        // We only know how to preserve the stack for chaining (returns the receiver) or void calls. Anything else
        // is left untouched (and Suppress() will refuse to target it), so we never emit unbalanced IL.
        var returnsReceiver = candidate.ReturnType != typeof(void)
                              && candidate.ReturnType.IsAssignableFrom(declaringType);
        if (candidate.ReturnType != typeof(void) && !returnsReceiver)
            return false;

        method = candidate;
        return true;
    }

    private static void EmitGuardedCall(
        List<CodeInstruction> result,
        ILGenerator generator,
        CodeInstruction callInstruction,
        MethodInfo method
    ) {
        var parameters = method.GetParameters();
        var argLocals = parameters.Select(it => generator.DeclareLocal(it.ParameterType)).ToArray();
        var skipLabel = generator.DefineLabel();
        var endLabel = generator.DefineLabel();

        // Stack in: receiver, arg0, ..., argN-1. Spill args (top-down) so the receiver is left on top.
        var first = true;
        for (var i = argLocals.Length - 1; i >= 0; i--) {
            var store = new CodeInstruction(OpCodes.Stloc, argLocals[i]);
            if (first) {
                // Any branch that targeted the original call must now enter the guard.
                store.labels.AddRange(callInstruction.labels);
                callInstruction.labels.Clear();
                first = false;
            }
            result.Add(store);
        }

        // receiver -> IsSuppressed(receiver, "<name>")
        var dup = new CodeInstruction(OpCodes.Dup);
        if (first) {
            dup.labels.AddRange(callInstruction.labels);
            callInstruction.labels.Clear();
        }
        result.Add(dup);
        result.Add(new CodeInstruction(OpCodes.Ldstr, method.Name));
        result.Add(new CodeInstruction(OpCodes.Call, isSuppressed));
        result.Add(new CodeInstruction(OpCodes.Brtrue, skipLabel));

        // Not suppressed: reload args in order and make the original call.
        foreach (var local in argLocals)
            result.Add(new CodeInstruction(OpCodes.Ldloc, local));
        result.Add(callInstruction);
        result.Add(new CodeInstruction(OpCodes.Br, endLabel));

        // Suppressed: the receiver is still on the stack. For chaining methods it IS the return value; for void
        // methods discard it so the stack matches the non-suppressed path.
        var skipTarget = new CodeInstruction(OpCodes.Nop);
        skipTarget.labels.Add(skipLabel);
        result.Add(skipTarget);
        if (method.ReturnType == typeof(void))
            result.Add(new CodeInstruction(OpCodes.Pop));

        var endTarget = new CodeInstruction(OpCodes.Nop);
        endTarget.labels.Add(endLabel);
        result.Add(endTarget);
    }

}
