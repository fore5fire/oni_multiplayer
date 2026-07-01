using System;
using MultiplayerMod.Platform.Steam.Network.Messaging;
using Steamworks;

namespace MultiplayerMod.Platform.Steam.Network;

public static class Configuration {

    // The whole world save is fragmented and queued into the Steam send buffer at once, so this cap must
    // exceed the serialized save size or Send fails with k_EResultLimitExceeded and the client hangs on the
    // "waiting for players" screen (issue #353). 10 MiB is exceeded by mid/late-game colonies; 128 MiB covers
    // typical large saves. Interim measure — the proper fix is flow-controlled streaming (wait for drain
    // between fragments) instead of over-provisioning. NOTE: raised value not yet validated in-game.
    private const int defaultBufferSize = 134217728; // 128 MiB

    public static SteamNetworkingConfigValue_t SendBufferSize(int size = defaultBufferSize) => new() {
        m_eValue = ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_SendBufferSize,
        m_eDataType = ESteamNetworkingConfigDataType.k_ESteamNetworkingConfig_Int32,
        m_val = new SteamNetworkingConfigValue_t.OptionValue { m_int32 = size }
    };

    public const int MaxMessageSize = 524288; // 512 KiB
    public static readonly int MaxFragmentDataSize = GetFragmentDataSize();

    private static int GetFragmentDataSize() {
        using var serialized = NetworkSerializer.Serialize(new NetworkMessageFragment(0, Array.Empty<byte>()));
        return MaxMessageSize - (int) serialized.Size;
    }

}
