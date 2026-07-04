using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using HarmonyLib;
using MultiplayerMod.Core.Logging;
using MultiplayerMod.Core.Unity;
using MultiplayerMod.Game.UI.Tools.Events;
using MultiplayerMod.ModRuntime.StaticCompatibility;
using MultiplayerMod.Network;
using MultiplayerMod.Platform.Direct;
using UnityEngine;

namespace MultiplayerMod.Multiplayer.Testing.Components;

/// <summary>
/// Local TCP control server for automated sync testing. Reads line commands on a background thread, executes them
/// on the game thread in <see cref="Update"/>, and writes a one-line response. Gameplay actions are injected
/// through the game's real tools (<see cref="DragTool.OnDragTool"/> + <see cref="DragToolEvents.FinishDrag"/>), so
/// the mod's normal producer/binder emits exactly the command a mouse drag would — the host performs the action
/// and clients replicate it. Queries let the host and a client be compared for divergence.
///
/// <para>Commands (newline-terminated): <c>ping</c>, <c>dig x,y [x,y ...]</c>, <c>cancel x,y ...</c>,
/// <c>count &lt;ComponentType&gt;</c>, <c>help</c>. Cells are <c>x,y</c> grid coordinates.</para>
/// </summary>
public class TestControlComponent : MultiplayerMonoBehaviour {

    private readonly Core.Logging.Logger log = LoggerFactory.GetLogger<TestControlComponent>();

    private TcpListener? listener;
    private volatile bool running;
    private readonly ConcurrentQueue<Request> queue = new();

    private class Request {
        public string Line = "";
        public StreamWriter Writer = null!;
    }

    protected override void Awake() {
        base.Awake();
        try {
            listener = new TcpListener(IPAddress.Loopback, DirectNetworkConfig.TestPort);
            listener.Start();
            running = true;
            new Thread(AcceptLoop) { IsBackground = true, Name = "TestControlAccept" }.Start();
            log.Info($"[TESTCTL] listening on 127.0.0.1:{DirectNetworkConfig.TestPort}");
        } catch (Exception e) {
            log.Error($"[TESTCTL] failed to start: {e}");
        }
    }

    private void AcceptLoop() {
        while (running) {
            try {
                var client = listener!.AcceptTcpClient();
                new Thread(() => ReadLoop(client)) { IsBackground = true, Name = "TestControlConn" }.Start();
            } catch (Exception) {
                if (!running)
                    break;
            }
        }
    }

    private void ReadLoop(TcpClient client) {
        try {
            using var stream = client.GetStream();
            var reader = new StreamReader(stream);
            var writer = new StreamWriter(stream) { AutoFlush = true };
            string? line;
            while (running && (line = reader.ReadLine()) != null) {
                var trimmed = line.Trim();
                if (trimmed.Length != 0)
                    queue.Enqueue(new Request { Line = trimmed, Writer = writer });
            }
        } catch (Exception) {
            // connection closed
        }
    }

    private void Update() {
        while (queue.TryDequeue(out var request)) {
            string response;
            try {
                response = Execute(request.Line);
            } catch (Exception e) {
                response = "error: " + e.Message;
            }
            try {
                request.Writer.WriteLine(response);
            } catch (Exception) {
                // client gone
            }
        }
    }

    private string Execute(string line) {
        var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        var command = parts[0].ToLowerInvariant();
        var args = parts.Skip(1).ToArray();
        return command switch {
            "ping" => Ping(),
            "stat" => Stat(),
            "hash" => Hash(),
            "speed" => Speed(args),
            "sync" => Sync(),
            "dig" => DragAction<DigTool>(args),
            "cancel" => DragAction<CancelTool>(args),
            "deconstruct" => DragAction<DeconstructTool>(args),
            "prioritize" => DragAction<PrioritizeTool>(args),
            "sweep" => DragAction<ClearTool>(args),
            "mop" => DragAction<MopTool>(args),
            "disinfect" => DragAction<DisinfectTool>(args),
            "harvest" => DragAction<HarvestTool>(args),
            "emptypipe" => DragAction<EmptyPipeTool>(args),
            "disconnect" => DragAction<DisconnectTool>(args),
            "build" => BuildAction(args),
            "count" => Count(args),
            "help" => "commands: ping | stat | hash | speed 0|1|2|3 | sync | " +
                      "dig|cancel|deconstruct|prioritize|sweep|mop|disinfect|harvest|emptypipe|disconnect x,y... | " +
                      "build <prefabId> x,y [orientation] | count <ComponentType> | help",
            _ => "error: unknown command '" + command + "'"
        };
    }

    /// <summary>Triggers a hard-sync (host only): saves the world and pushes it to clients, which reload and
    /// realign — the mechanism that corrects accumulated physics-sim divergence.</summary>
    private static string Sync() {
        if (Dependencies.Get<MultiplayerGame>().Mode != MultiplayerMode.Host)
            return "error: sync must run on the host";
        Dependencies.Get<MultiplayerMod.Multiplayer.World.WorldManager>().Sync();
        return "ok sync triggered";
    }

    /// <summary>Instant-builds a building on the host and replicates it (the exact Build command a placement
    /// emits). Materials default to the building's defaults.</summary>
    private static string BuildAction(string[] args) {
        if (args.Length < 2)
            return "error: build <prefabId> x,y [orientation]";
        var def = Assets.GetBuildingDef(args[0]);
        if (def == null)
            return "error: unknown building '" + args[0] + "'";
        var cells = ParseCells(new[] { args[1] });
        if (cells.Count == 0)
            return "error: bad cell";
        var orientation = Orientation.Neutral;
        if (args.Length >= 3)
            Enum.TryParse(args[2], true, out orientation);
        var materials = def.DefaultElements().ToArray();
        var priority = new PrioritySetting(PriorityScreen.PriorityClass.basic, 5);
        var buildArgs = new Game.UI.Tools.Events.BuildEventArgs(
            cells[0], def.PrefabID, InstantBuild: true, Upgrade: false, orientation, materials, null!, priority
        );
        new MultiplayerMod.Multiplayer.Commands.Tools.Build(buildArgs).Execute(null!);
        Dependencies.Get<IMultiplayerClient>().Send(new MultiplayerMod.Multiplayer.Commands.Tools.Build(buildArgs));
        return $"ok build {def.PrefabID} @{cells[0]}";
    }

    private static string Ping() {
        var mode = Dependencies.Get<MultiplayerGame>().Mode;
        var cycle = GameClock.Instance != null ? GameClock.Instance.GetCycle() : -1;
        return $"pong mode={mode} cycle={cycle}";
    }

    /// <summary>One-line snapshot for host/client comparison: mode, cycle, paused, dupes, buildings.</summary>
    private static string Stat() {
        var mode = Dependencies.Get<MultiplayerGame>().Mode;
        var cycle = GameClock.Instance != null ? GameClock.Instance.GetCycle() : -1;
        var paused = SpeedControlScreen.Instance != null && SpeedControlScreen.Instance.IsPaused;
        var dupes = UnityEngine.Object.FindObjectsOfType(typeof(MinionIdentity)).Length;
        var buildings = UnityEngine.Object.FindObjectsOfType(typeof(BuildingComplete)).Length;
        return $"stat mode={mode} cycle={cycle} paused={paused} dupes={dupes} buildings={buildings}";
    }

    /// <summary>FNV-1a checksum over the per-cell element layout — a structural world fingerprint to compare
    /// between host and client (matches = same solid/gas/liquid layout; digging/building changes it identically
    /// on both when in sync).</summary>
    private static string Hash() {
        if (Grid.Element == null)
            return "error: grid not ready";
        var count = Grid.CellCount;
        var hash = 14695981039346656037UL;
        for (var i = 0; i < count; i++) {
            hash ^= (ulong) (int) Grid.Element[i].id;
            hash *= 1099511628211UL;
        }
        return $"hash cells={count} element={hash:x16}";
    }

    /// <summary>Injects a synced speed change through the real SpeedControlScreen (0=pause, 1..3=speed tiers), so
    /// the mod's producer emits Pause/Resume/ChangeGameSpeed to clients — exactly like clicking the speed UI.</summary>
    private static string Speed(string[] args) {
        if (args.Length == 0 || !int.TryParse(args[0], out var level) || level < 0 || level > 3)
            return "error: speed 0|1|2|3 (0=pause)";
        var screen = SpeedControlScreen.Instance;
        if (screen == null)
            return "error: no speed control (not in game)";
        if (level == 0) {
            if (!screen.IsPaused)
                screen.Pause();
        } else {
            screen.SetSpeed(level - 1);
            if (screen.IsPaused)
                screen.Unpause();
        }
        return $"ok speed={level} paused={screen.IsPaused}";
    }

    // ReSharper disable once Unity.IncorrectMonoBehaviourInstantiation
    private string DragAction<T>(string[] args) where T : DragTool, new() {
        var cells = ParseCells(args);
        if (cells.Count == 0)
            return "error: no valid cells (expected x,y pairs)";
        var tool = new T();
        // FilteredDragTools read currentFilters when finalizing; a fresh instance has none. Give it an
        // "all layers" filter so the finalize step doesn't NPE and the tool acts on everything.
        if (tool is FilteredDragTool filtered && filtered.currentFilters == null)
            filtered.currentFilters = new[] {
                new ToolParameterMenu.ToggleData(ToolParameterMenu.FILTERLAYERS.ALL, ToolParameterMenu.ToggleState.On, true)
            };
        var down = Grid.CellToPosCCC(cells[0], Grid.SceneLayer.Move);
        var up = Grid.CellToPosCCC(cells[cells.Count - 1], Grid.SceneLayer.Move);
        tool.downPos = down;
        // Drive the real tool per cell at ExecutionLevel.Game so the mod's producer accumulates and emits the
        // command, exactly as a drag would.
        foreach (var cell in cells)
            tool.OnDragTool(cell, 0);
        DragToolEvents.FinishDrag(tool, down, up);
        return $"ok {typeof(T).Name} {cells.Count} cells";
    }

    private static List<int> ParseCells(IEnumerable<string> args) {
        var cells = new List<int>();
        foreach (var arg in args) {
            var xy = arg.Split(',');
            if (xy.Length == 2 && int.TryParse(xy[0], out var x) && int.TryParse(xy[1], out var y) && Grid.IsValidCell(Grid.XYToCell(x, y)))
                cells.Add(Grid.XYToCell(x, y));
        }
        return cells;
    }

    private static string Count(string[] args) {
        if (args.Length == 0)
            return "error: count <ComponentType>";
        var type = AccessTools.TypeByName(args[0]);
        if (type == null)
            return "error: unknown type '" + args[0] + "'";
        var count = UnityEngine.Object.FindObjectsOfType(type).Length;
        return $"count {args[0]}={count}";
    }

    private void OnDestroy() {
        running = false;
        try {
            listener?.Stop();
        } catch (Exception) {
            // ignore
        }
    }

}
