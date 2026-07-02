using System;
using MultiplayerMod.Core.Dependency;
using MultiplayerMod.Core.Logging;
using MultiplayerMod.Core.Unity;
using MultiplayerMod.Multiplayer.Commands;
using MultiplayerMod.Multiplayer.Commands.Registry;
using MultiplayerMod.Network;
using UnityEngine;

namespace MultiplayerMod.Platform.Direct.Network.Components;

/// <summary>
/// Automated end-to-end test of the direct transport, run in the real game process but with no UI, world, or
/// second machine. It stands up a fresh <see cref="TcpMultiplayerServer"/> and <see cref="TcpMultiplayerClient"/>
/// (isolated from the app's command pipeline for determinism), connects the client to the server over loopback,
/// and sends a probe command host->client. Success proves the whole transport path: socket bind/accept, the hello
/// handshake, framing + surrogate serialization, background-read to game-thread hand-off, and host-id matching.
/// The cross-machine path is identical code with a LAN address instead of loopback. Result is logged as
/// <c>[SELFTEST] PASS/FAIL</c> for reading back over SSH.
/// </summary>
public class DirectSelfTestComponent : MultiplayerMonoBehaviour {

    private const float StartDelaySeconds = 3f;
    private const float TimeoutSeconds = 20f;

    [InjectDependency]
    private MultiplayerCommandRegistry registry = null!;

    private readonly Core.Logging.Logger log = LoggerFactory.GetLogger<DirectSelfTestComponent>();

    private TcpMultiplayerServer? server;
    private TcpMultiplayerClient? client;
    private DirectSelfTestCommand? probe;

    private float elapsed;
    private bool started;
    private bool finished;

    private void Update() {
        elapsed += Time.unscaledDeltaTime;
        if (finished)
            return;

        if (!started) {
            if (elapsed >= StartDelaySeconds)
                Begin();
            return;
        }

        // Pump the fresh instances (their auto-created Unity components tick the DI singletons, not these).
        server?.Tick();
        client?.Tick();

        if (elapsed >= TimeoutSeconds)
            Fail($"timed out (client state {client?.State}, {server?.Clients.Count ?? 0} clients connected)");
    }

    private void Begin() {
        started = true;
        try {
            var localId = new LocalPlayerId();
            server = new TcpMultiplayerServer(registry, localId);
            client = new TcpMultiplayerClient(localId);

            client.CommandReceived += OnClientCommandReceived;
            server.ClientConnected += id => {
                log.Info($"[SELFTEST] server accepted client {id}");
                SendProbe();
            };
            server.StateChanged += state => {
                if (state == MultiplayerServerState.Started)
                    ConnectClient();
            };

            log.Info("[SELFTEST] starting direct-transport loopback self-test");
            server.Start();
        } catch (Exception exception) {
            Fail($"exception during setup: {exception}");
        }
    }

    private void ConnectClient() {
        try {
            client!.Connect(server!.Endpoint);
        } catch (Exception exception) {
            Fail($"client connect failed: {exception}");
        }
    }

    private void SendProbe() {
        probe = new DirectSelfTestCommand();
        log.Info($"[SELFTEST] client connected, sending probe {probe.Id:N}");
        server!.SendAll(probe);
    }

    private void OnClientCommandReceived(IMultiplayerCommand command) {
        if (command is DirectSelfTestCommand received && probe != null && received.Id == probe.Id)
            Pass();
        else
            log.Warning($"[SELFTEST] unexpected command received: {command}");
    }

    private void Pass() {
        log.Info("[SELFTEST] PASS: probe round-tripped host->client over the direct transport");
        Finish();
    }

    private void Fail(string reason) {
        log.Error($"[SELFTEST] FAIL: {reason}");
        Finish();
    }

    private void Finish() {
        if (finished)
            return;
        finished = true;
        try {
            if (client is { State: not MultiplayerClientState.Disconnected })
                client.Disconnect();
        } catch (Exception) {
            // ignore
        }
        try {
            if (server != null && server.State > MultiplayerServerState.Stopped)
                server.Stop();
        } catch (Exception) {
            // ignore
        }
        UnityObject.Destroy(gameObject);
    }

}
