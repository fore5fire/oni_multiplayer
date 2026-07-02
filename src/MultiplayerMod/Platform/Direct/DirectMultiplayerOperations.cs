using JetBrains.Annotations;
using MultiplayerMod.Core.Dependency;
using MultiplayerMod.Core.Events;
using MultiplayerMod.Core.Logging;
using MultiplayerMod.Multiplayer;
using MultiplayerMod.Multiplayer.Players.Events;
using MultiplayerMod.Platform.Direct.Network;

namespace MultiplayerMod.Platform.Direct;

/// <summary>
/// "Join" for the direct transport. With no Steam friends overlay to pick a host from, the target address comes
/// from configuration (<c>MP_HOST</c>/<c>MP_PORT</c>); pressing JOIN MULTIPLAYER dials it immediately. A future
/// improvement is an in-game address prompt in place of this config-driven connect.
/// </summary>
[Dependency, UsedImplicitly]
public class DirectMultiplayerOperations : IMultiplayerOperations {

    private readonly Core.Logging.Logger log = LoggerFactory.GetLogger<DirectMultiplayerOperations>();
    private readonly EventDispatcher events;

    public DirectMultiplayerOperations(EventDispatcher events) {
        this.events = events;
    }

    public void Join() {
        var host = DirectNetworkConfig.Host;
        var port = DirectNetworkConfig.Port;
        log.Info($"Direct join requested -> {host}:{port}");
        var endpoint = new DirectServerEndpoint(host, port);
        events.Dispatch(new MultiplayerJoinRequestedEvent(endpoint, $"{host}:{port}"));
    }

}
