using JetBrains.Annotations;
using MultiplayerMod.Core.Dependency;
using MultiplayerMod.Core.Events;
using MultiplayerMod.Core.Logging;
using MultiplayerMod.Core.Unity;
using MultiplayerMod.ModRuntime;
using MultiplayerMod.Platform.Direct.Network.Components;

namespace MultiplayerMod.Platform.Direct;

/// <summary>
/// Spawns the direct-transport loopback self-test once the mod runtime is ready, when <c>SELFTEST=true</c> is
/// configured. Only scanned/registered when the direct transport is selected (see PlatformSelection).
/// </summary>
[Dependency, UsedImplicitly]
public class DirectSelfTest {

    private readonly Core.Logging.Logger log = LoggerFactory.GetLogger<DirectSelfTest>();

    public DirectSelfTest(EventDispatcher events) {
        if (!DirectNetworkConfig.SelfTest)
            return;

        log.Info("[SELFTEST] enabled; will run after runtime is ready");
        events.Subscribe<RuntimeReadyEvent>(_ => UnityObject.CreateStaticWithComponent<DirectSelfTestComponent>());
    }

}
