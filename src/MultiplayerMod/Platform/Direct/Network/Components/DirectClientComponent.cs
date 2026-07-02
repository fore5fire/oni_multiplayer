using MultiplayerMod.Core.Dependency;
using MultiplayerMod.Core.Unity;

// ReSharper disable FieldCanBeMadeReadOnly.Local

namespace MultiplayerMod.Platform.Direct.Network.Components;

public class DirectClientComponent : MultiplayerMonoBehaviour {

    [InjectDependency]
    private TcpMultiplayerClient client = null!;

    private void Update() => client.Tick();

}
