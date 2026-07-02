using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using KMod;
using MultiplayerMod.Core.Dependency;
using MultiplayerMod.Core.Events;
using MultiplayerMod.Core.Extensions;
using MultiplayerMod.Core.Logging;
using MultiplayerMod.Core.Patch;

namespace MultiplayerMod.ModRuntime.Loader;

public class DelayedModLoader {

    private readonly Core.Logging.Logger log = LoggerFactory.GetLogger<DelayedModLoader>();

    private readonly Harmony harmony;
    private readonly Assembly modAssembly;
    private readonly IReadOnlyList<Mod> mods;

    public DelayedModLoader(Harmony harmony, Assembly modAssembly, IReadOnlyList<Mod> mods) {
        this.harmony = harmony;
        this.modAssembly = modAssembly;
        this.mods = mods;
    }

    public void OnLoad() {
        var builder = new DependencyContainerBuilder()
            .AddSingleton(harmony)
            .AddType<EventDispatcher>()
            // Filtered so only the selected transport's platform types register (Steam by default; the direct
            // LAN transport when MP_TRANSPORT=direct). Registering both would make IMultiplayerServer/Client
            // ambiguous. See PlatformSelection.
            .ScanAssembly(modAssembly, Platform.PlatformSelection.IncludeInDependencyScan);
        PrioritizedPatch();
        modAssembly.GetTypes()
            .Where(type => typeof(IModComponentConfigurer).IsAssignableFrom(type) && type.IsClass)
            .OrderBy(type => type.GetCustomAttribute<ModComponentOrder>()?.Order ?? ModComponentOrder.Default)
            .ForEach(
                type => {
                    var instance = (IModComponentConfigurer) Activator.CreateInstance(type);
                    log.Debug($"Configuring mod component with {type.FullName}");
                    instance.Configure(builder);
                }
            );
        var container = builder.Build();
        InjectStatic(modAssembly, container);
        OnRuntimeReady(container);
    }

    private void OnRuntimeReady(IDependencyContainer container) {
        container.Get<EventDispatcher>().Dispatch(new RuntimeReadyEvent(container.Get<Runtime>()));
        log.Info("Mod runtime is ready");
    }

    private void InjectStatic(Assembly assembly, IDependencyInjector container) => assembly.GetTypes()
        .Where(it => it.GetCustomAttribute<DependenciesStaticTargetAttribute>() != null)
        .ForEach(container.Inject);

    private void PrioritizedPatch() => AccessTools.GetTypesFromAssembly(modAssembly)
        .Where(it => it.GetCustomAttribute<HarmonyManualAttribute>() == null)
        // Only types that explicitly declare [HarmonyPatch] are real patch classes. Harmony 2.4.2 broadened its
        // convention-based auxiliary-method detection, so helper/stub types that merely inherit a method named
        // Prepare/Cleanup/TargetMethod (e.g. our StateMachine.Parameter.Context subclasses inherit a virtual
        // Cleanup()) would otherwise be misdetected as patch containers and fail to "patch". Requiring the
        // attribute restores the pre-2.4.2 behavior and is future-proof against other stub types.
        .Where(it => it.GetCustomAttributes(typeof(HarmonyPatch), false).Length > 0)
        .Select(type => (type, processor: TryCreateClassProcessor(type)))
        .Where(it => it.processor?.containerAttributes != null)
        .OrderByDescending(it => it.processor!.containerAttributes.priority)
        .ForEach(it => TryPatch(it.type, it.processor!));

    // A single patch failing (e.g. a transpiler that no longer matches an updated game method, or a target that
    // moved) must not abort the remaining patches or, worse, propagate out of OnLoad into the game's launch
    // sequence — that bricks the whole game instead of degrading one feature. Catch per-patch, log loudly, and
    // continue so the rest of the mod still loads and the failure is diagnosable from a booted game.
    private void TryPatch(Type type, PatchClassProcessor processor) {
        try {
            processor.Patch();
        } catch (Exception exception) {
            log.Error($"Failed to apply patch {type.FullName}; feature disabled\n{exception}");
        }
    }

    private PatchClassProcessor? TryCreateClassProcessor(Type type) {
        var optional = type.GetCustomAttribute<HarmonyOptionalAttribute>() != null;
        try {
            return harmony.CreateClassProcessor(type);
        } catch (Exception exception) {
            if (optional) {
                log.Trace(() => $"Unable to create class processor for patch {type.FullName}\n{exception}");
                log.Info($"Optional patch {type.FullName} is omitted");
            } else {
                throw;
            }
        }
        return null;
    }

}
