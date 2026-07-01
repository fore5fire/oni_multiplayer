using System.Collections.Generic;
using System.IO;
using System.Linq;
using JetBrains.Annotations;
using MultiplayerMod.Core.Dependency;
using MultiplayerMod.Core.Events;
using MultiplayerMod.Core.Extensions;
using MultiplayerMod.Core.Paths;
using MultiplayerMod.Core.Scheduling;
using MultiplayerMod.ModRuntime.Context;
using MultiplayerMod.ModRuntime.StaticCompatibility;
using MultiplayerMod.Multiplayer.Commands.Speed;
using MultiplayerMod.Multiplayer.CoreOperations.Events;
using MultiplayerMod.Multiplayer.CoreOperations.PlayersManagement.Commands;
using MultiplayerMod.Multiplayer.Players;
using MultiplayerMod.Multiplayer.Players.Events;
using MultiplayerMod.Multiplayer.UI.Overlays;
using MultiplayerMod.Multiplayer.World.Commands;
using MultiplayerMod.Multiplayer.World.Data;
using MultiplayerMod.Network;

namespace MultiplayerMod.Multiplayer.World;

[Dependency, UsedImplicitly]
public class WorldManager {

    private readonly IMultiplayerServer server;
    private readonly MultiplayerGame multiplayer;
    private readonly EventDispatcher events;
    private readonly UnityTaskScheduler scheduler;
    private readonly ExecutionLevelManager executionLevelManager;
    private readonly List<IWorldStateManager> worldStateManagers;

    // One-shot event subscriptions are tracked so a new sync/load supersedes any still-pending one. Without this,
    // near-simultaneous syncs (e.g. a join racing an autosave) stack subscriptions and fire them all on a single
    // load — running LoadState (and registering chores) more than once, and sending spurious ResumeGame.
    private EventSubscription? syncResumeSubscription;
    private EventSubscription? syncStatusSubscription;
    private EventSubscription? loadOverlaySubscription;
    private EventSubscription? loadStateSubscription;

    public WorldManager(
        IMultiplayerServer server,
        MultiplayerGame multiplayer,
        EventDispatcher events,
        UnityTaskScheduler scheduler,
        ExecutionLevelManager executionLevelManager,
        List<IWorldStateManager> worldStateManagers
    ) {
        this.server = server;
        this.multiplayer = multiplayer;
        this.events = events;
        this.scheduler = scheduler;
        this.executionLevelManager = executionLevelManager;
        this.worldStateManagers = worldStateManagers;
    }

    public void Sync() {
        SetupStatusOverlay();

        var resume = !SpeedControlScreen.Instance.IsPaused;
        server.SendAll(new PauseGame());

        events.Dispatch(new WorldSyncEvent());

        multiplayer.Players.ForEach(it => server.SendAll(new ChangePlayerStateCommand(it.Id, PlayerState.Loading)));
        server.SendAll(new ChangePlayerStateCommand(multiplayer.Players.Current.Id, PlayerState.Ready));
        server.Send(new NotifyWorldSavePreparing());

        var world = new WorldSave(WorldName, GetWorldSave(), new WorldState());
        worldStateManagers.ForEach(it => it.SaveState(world.State));
        server.Send(new LoadWorld(world));
        syncResumeSubscription?.Cancel();
        syncResumeSubscription = events.Subscribe<PlayersReadyEvent>(
            (_, subscription) => {
                if (resume)
                    server.SendAll(new ResumeGame());
                subscription.Cancel();
                syncResumeSubscription = null;
            }
        );
    }

    private void SetupStatusOverlay() {
        MultiplayerStatusOverlay.Show("Waiting for players...");
        syncStatusSubscription?.Cancel();
        syncStatusSubscription = events.Subscribe<PlayerStateChangedEvent>(
            (_, subscription) => {
                var players = multiplayer.Players;
                if (players.Ready) {
                    MultiplayerStatusOverlay.Close();
                    subscription.Cancel();
                    syncStatusSubscription = null;
                }
                var readyPlayersCount = players.Count(it => it.State == PlayerState.Ready);
                var playerList = string.Join("\n", players.Select(it => $"{it.Profile.PlayerName}: {it.State}"));
                var statusText = $"Waiting for players ({readyPlayersCount}/{players.Count} ready)...\n{playerList}";
                MultiplayerStatusOverlay.Text = statusText;
            }
        );
    }

    public void RequestWorldLoad(WorldSave world) {
        MultiplayerStatusOverlay.Show($"Loading {world.Name}...");
        loadOverlaySubscription?.Cancel();
        loadOverlaySubscription = events.Subscribe<PlayersReadyEvent>(
            (_, subscription) => {
                MultiplayerStatusOverlay.Close();
                subscription.Cancel();
                loadOverlaySubscription = null;
            }
        );
        // Supersede any pending load's state handler so a duplicate LoadWorld can't run LoadState twice
        // (which would register the saved chores/objects twice).
        loadStateSubscription?.Cancel();
        loadStateSubscription = events.Subscribe<WorldStateInitializingEvent>((_, subscription) => {
            worldStateManagers.ForEach(it => it.LoadState(world.State));
            subscription.Cancel();
            loadStateSubscription = null;
        });
        scheduler.Run(() => LoadWorldSave(world.Name, world.Data));
    }

    private void LoadWorldSave(string name, byte[] data) {
        var savePath = SaveLoader.GetCloudSavesDefault()
            ? SaveLoader.GetCloudSavePrefix()
            : SaveLoader.GetSavePrefixAndCreateFolder();

        var path = SecurePath.Combine(savePath, name, $"{name}.sav");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using (var writer = new BinaryWriter(File.OpenWrite(path)))
            writer.Write(data);

        executionLevelManager.BaseLevel = ExecutionLevel.Multiplayer;
        LoadScreen.DoLoad(path);
    }

    private static string WorldName => Path.GetFileNameWithoutExtension(SaveLoader.GetActiveSaveFilePath());

    private static byte[] GetWorldSave() {
        var path = SaveLoader.GetActiveSaveFilePath();
        Execution.RunUsingLevel(ExecutionLevel.Multiplayer, () => SaveLoader.Instance.Save(path));
        return File.ReadAllBytes(path);
    }

}
