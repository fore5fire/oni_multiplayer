using System;
using HarmonyLib;
using MultiplayerMod.ModRuntime.Context;

namespace MultiplayerMod.Game.UI.Screens.Events;

public static class SkillScreenEvents {

    public static event Action<MinionIdentity, string?>? SetHat;
    public static event Action<MinionIdentity, string>? MasterSkill;

    [HarmonyPatch(typeof(SkillsScreen))]
    // ReSharper disable once UnusedType.Local
    private static class SkillsScreenEvents {

        [HarmonyPostfix]
        [HarmonyPatch(nameof(SkillsScreen.OnHatDropEntryClick))]
        [RequireExecutionLevel(ExecutionLevel.Game)]
        // ReSharper disable once UnusedMember.Local
        private static void OnHatDropEntryClick(SkillsScreen __instance, IListableOption skill, object data) {
            __instance.GetMinionIdentity(__instance.currentlySelectedMinion, out var minionIdentity, out _);
            // The command must carry the hat resource id (e.g. "hat_role_mining1"), NOT GetProperName() (the
            // localized display name). MinionResume.SetHats/AddHat look the id up in AccessorySlots.Hat; a display
            // name resolves to null → "Missing hat" warning, a green-dot placeholder, and an NRE in AddHat for
            // dupes that render via SymbolOverrideController. The game's own handler uses (skill as HatListable).hat.
            SetHat?.Invoke(
                minionIdentity,
                (skill as HatListable)?.hat
            );
        }

    }

    [HarmonyPatch(typeof(SkillMinionWidget))]
    // ReSharper disable once UnusedType.Local
    private static class SkillMinionWidgetEvents {

        [HarmonyPostfix]
        [HarmonyPatch(nameof(SkillMinionWidget.OnHatDropEntryClick))]
        [RequireExecutionLevel(ExecutionLevel.Game)]
        // ReSharper disable once UnusedMember.Local
        private static void OnHatDropEntryClick(SkillMinionWidget __instance, IListableOption hatOption, object data) {
            __instance.skillsScreen.GetMinionIdentity(__instance.assignableIdentity, out var minionIdentity, out _);
            // Send the hat resource id, not the display name — see the note in SkillsScreenEvents above.
            SetHat?.Invoke(minionIdentity, (hatOption as HatListable)?.hat);
        }

    }

    [HarmonyPatch(typeof(SkillWidget))]
    // ReSharper disable once UnusedType.Local
    private static class SkillWidgetEvents {
        [HarmonyPostfix]
        [HarmonyPatch(nameof(SkillWidget.OnPointerClick))]
        [RequireExecutionLevel(ExecutionLevel.Game)]
        // ReSharper disable once UnusedMember.Local
        private static void OnPointerClick(SkillWidget __instance) {
            __instance.skillsScreen.GetMinionIdentity(
                __instance.skillsScreen.CurrentlySelectedMinion,
                out var minionIdentity,
                out _
            );
            MasterSkill?.Invoke(minionIdentity, __instance.skillID);
        }

    }

}
