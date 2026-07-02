using MultiplayerMod.Network;

namespace MultiplayerMod.Platform.Direct.Network;

/// <summary>Address of a direct-transport host: an IP/hostname and TCP port.</summary>
public record DirectServerEndpoint(string Host, int Port) : IMultiplayerEndpoint;
