using JetBrains.Annotations;
using MultiplayerMod.Core.Dependency;
using MultiplayerMod.Core.Logging;
using MultiplayerMod.Core.Unity;
using MultiplayerMod.ModRuntime.Loader;
using MultiplayerMod.Platform.Steam.Network.Components;

namespace MultiplayerMod.Platform.Steam;

[UsedImplicitly]
[ModComponentOrder(ModComponentOrder.Platform)]
public class SteamPlatformConfigurer : IModComponentConfigurer {

    private readonly Core.Logging.Logger log = LoggerFactory.GetLogger<SteamPlatformConfigurer>();

    public void Configure(DependencyContainerBuilder builder) {
        // Component configurers are discovered independently of the DI scan filter, so this still runs when the
        // direct transport is selected — bail out to avoid wiring Steam lobby handling in that mode.
        if (PlatformSelection.DirectSelected)
            return;

        var steam = DistributionPlatform.Inst.Platform == "Steam";
        if (!steam)
            return;

        log.Info("Steam platform detected");

        builder.ContainerCreated += _ => UnityObject.CreateStaticWithComponent<LobbyJoinRequestComponent>();
    }

}
