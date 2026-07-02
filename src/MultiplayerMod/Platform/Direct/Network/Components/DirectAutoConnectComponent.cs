using System.IO;
using System.Linq;
using MultiplayerMod.Core.Logging;
using MultiplayerMod.Core.Unity;
using MultiplayerMod.ModRuntime.StaticCompatibility;
using MultiplayerMod.Multiplayer;
using MultiplayerMod.Multiplayer.CoreOperations.Events;
using MultiplayerMod.Core.Events;
using UnityEngine;

namespace MultiplayerMod.Platform.Direct.Network.Components;

/// <summary>
/// Drives the host/join flow with no UI, for unattended two-machine testing. Spawned at the main menu when
/// <c>AUTOHOST=true</c> or <c>AUTOJOIN=true</c> is configured. After a short settle delay it replicates exactly
/// what the multiplayer menu buttons do: AUTOHOST sets host mode and loads the newest save (embark ->
/// GameStartedEvent(Host) -> server.Start()); AUTOJOIN sets client mode and dials the configured host.
/// </summary>
public class DirectAutoConnectComponent : MultiplayerMonoBehaviour {

    private const float SettleDelaySeconds = 2f;

    private readonly Core.Logging.Logger log = LoggerFactory.GetLogger<DirectAutoConnectComponent>();

    private float elapsed;
    private bool done;

    private void Update() {
        if (done)
            return;
        elapsed += Time.unscaledDeltaTime;
        if (elapsed < SettleDelaySeconds)
            return;

        done = true;
        if (DirectNetworkConfig.AutoHost)
            StartHost();
        else if (DirectNetworkConfig.AutoJoin)
            StartJoin();

        Destroy(this);
    }

    private void StartHost() {
        var save = FindNewestSave();
        if (save == null) {
            log.Error("[AUTO] AUTOHOST: no save found to host — cannot auto-host");
            return;
        }
        log.Info($"[AUTO] AUTOHOST: hosting from save {save}");
        SelectMode(MultiplayerMode.Host);
        LoadScreen.DoLoad(save);
    }

    private void StartJoin() {
        log.Info($"[AUTO] AUTOJOIN: connecting to {DirectNetworkConfig.Host}:{DirectNetworkConfig.Port}");
        SelectMode(MultiplayerMode.Client);
        Dependencies.Get<IMultiplayerOperations>().Join();
    }

    private static void SelectMode(MultiplayerMode mode) {
        Dependencies.Get<MultiplayerGame>().Refresh(mode);
        Dependencies.Get<EventDispatcher>().Dispatch(new MultiplayerModeSelectedEvent(mode));
    }

    private string? FindNewestSave() {
        var dir = SaveLoader.GetCloudSavesDefault()
            ? SaveLoader.GetCloudSavePrefix()
            : SaveLoader.GetSavePrefixAndCreateFolder();
        if (!Directory.Exists(dir))
            return null;
        return Directory.GetFiles(dir, "*.sav", SearchOption.AllDirectories)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

}
