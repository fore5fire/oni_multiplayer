using JetBrains.Annotations;
using MultiplayerMod.Core.Dependency;
using MultiplayerMod.Core.Events;
using MultiplayerMod.Core.Logging;
using MultiplayerMod.Core.Unity;
using MultiplayerMod.ModRuntime;
using MultiplayerMod.Multiplayer.Testing.Components;
using MultiplayerMod.Platform.Direct;

namespace MultiplayerMod.Multiplayer.Testing;

/// <summary>
/// Starts the automated test-control server once the mod runtime is ready, when <c>TESTCONTROL=true</c> is
/// configured. Lets sync be exercised by injecting real gameplay actions and comparing host/client state over a
/// local TCP socket — no manual mouse input. See <see cref="Components.TestControlComponent"/>.
/// </summary>
[Dependency, UsedImplicitly]
public class TestControl {

    private readonly Core.Logging.Logger log = LoggerFactory.GetLogger<TestControl>();

    public TestControl(EventDispatcher events) {
        if (!DirectNetworkConfig.TestControl)
            return;

        log.Info("[TESTCTL] enabled; control server will start after runtime is ready");
        events.Subscribe<RuntimeReadyEvent>(_ => UnityObject.CreateStaticWithComponent<TestControlComponent>());
    }

}
