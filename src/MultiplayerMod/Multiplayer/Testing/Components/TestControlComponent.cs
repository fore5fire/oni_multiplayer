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
            "dig" => DragAction<DigTool>(args),
            "cancel" => DragAction<CancelTool>(args),
            "count" => Count(args),
            "help" => "commands: ping | dig x,y [x,y...] | cancel x,y... | count <ComponentType> | help",
            _ => "error: unknown command '" + command + "'"
        };
    }

    private static string Ping() {
        var mode = Dependencies.Get<MultiplayerGame>().Mode;
        var cycle = GameClock.Instance != null ? GameClock.Instance.GetCycle() : -1;
        return $"pong mode={mode} cycle={cycle}";
    }

    // ReSharper disable once Unity.IncorrectMonoBehaviourInstantiation
    private string DragAction<T>(string[] args) where T : DragTool, new() {
        var cells = ParseCells(args);
        if (cells.Count == 0)
            return "error: no valid cells (expected x,y pairs)";
        var tool = new T();
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
