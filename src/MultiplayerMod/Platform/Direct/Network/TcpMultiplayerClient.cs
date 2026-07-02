using System;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Threading;
using JetBrains.Annotations;
using MultiplayerMod.Core.Dependency;
using MultiplayerMod.Core.Logging;
using MultiplayerMod.Core.Unity;
using MultiplayerMod.Multiplayer.Commands;
using MultiplayerMod.Network;
using MultiplayerMod.Platform.Direct.Network.Components;
using MultiplayerMod.Platform.Steam.Network.Messaging;
using UnityEngine;

namespace MultiplayerMod.Platform.Direct.Network;

/// <summary>
/// TCP implementation of <see cref="IMultiplayerClient"/> for the direct (LAN/IP) transport. Connecting and
/// reading happen on a background thread (a blocking connect must never freeze the game thread); state changes
/// and received commands are marshalled back onto the game thread via <see cref="Tick"/>, since command
/// execution and the execution-level context are thread-bound.
/// </summary>
[Dependency, UsedImplicitly]
public class TcpMultiplayerClient : IMultiplayerClient {

    private static readonly TimeSpan connectTimeout = TimeSpan.FromSeconds(10);

    public IMultiplayerClientId Id => localId;
    public MultiplayerClientState State { get; private set; } = MultiplayerClientState.Disconnected;
    public event Action<MultiplayerClientState>? StateChanged;
    public event Action<IMultiplayerCommand>? CommandReceived;

    private readonly Core.Logging.Logger log = LoggerFactory.GetLogger<TcpMultiplayerClient>();
    private readonly DirectMultiplayerClientId localId;

    // Single FIFO queue preserves ordering between the connect state-change and subsequent received commands.
    // System.Action is fully qualified: ONI defines a global (non-delegate) `Action` enum that shadows it.
    private readonly ConcurrentQueue<System.Action> gameThreadActions = new();

    private TcpClient? client;
    private NetworkStream? stream;
    private volatile bool running;
    private GameObject? gameObject;

    public TcpMultiplayerClient(LocalPlayerId localPlayerId) {
        localId = localPlayerId.Value;
    }

    public void Connect(IMultiplayerEndpoint endpoint) {
        var direct = (DirectServerEndpoint) endpoint;

        SetState(MultiplayerClientState.Connecting);
        gameObject = UnityObject.CreateStaticWithComponent<DirectClientComponent>();

        running = true;
        var thread = new Thread(() => ConnectAndRead(direct)) { IsBackground = true, Name = "DirectClient" };
        thread.Start();
    }

    public void Disconnect() {
        if (State == MultiplayerClientState.Disconnected)
            return;

        running = false;
        try {
            client?.Close();
        } catch (Exception) {
            // ignore
        }
        if (gameObject != null)
            UnityObject.Destroy(gameObject);
        SetState(MultiplayerClientState.Disconnected);
    }

    public void Send(IMultiplayerCommand command, MultiplayerCommandOptions options = MultiplayerCommandOptions.None) {
        if (State != MultiplayerClientState.Connected || stream == null)
            throw new InvalidOperationException("Client not connected");

        try {
            DirectFraming.Write(stream, new NetworkMessage(command, options));
        } catch (Exception exception) {
            log.Error($"Failed to send {command}: {exception.Message}");
            SetState(MultiplayerClientState.Error);
        }
    }

    public void Tick() {
        while (gameThreadActions.TryDequeue(out var action)) {
            try {
                action();
            } catch (Exception exception) {
                log.Error($"Failed to process client action: {exception}");
            }
        }
    }

    private void ConnectAndRead(DirectServerEndpoint endpoint) {
        var tcpClient = new TcpClient();
        try {
            var async = tcpClient.BeginConnect(endpoint.Host, endpoint.Port, null, null);
            if (!async.AsyncWaitHandle.WaitOne(connectTimeout))
                throw new TimeoutException($"Connection to {endpoint.Host}:{endpoint.Port} timed out");
            tcpClient.EndConnect(async);
            tcpClient.NoDelay = true;

            client = tcpClient;
            stream = tcpClient.GetStream();
            DirectFraming.Write(stream, new DirectHello(localId.Id));
            log.Info($"Connected to {endpoint.Host}:{endpoint.Port}");
            gameThreadActions.Enqueue(() => SetState(MultiplayerClientState.Connected));

            while (running) {
                var frame = DirectFraming.Read(stream);
                if (frame == null)
                    break;
                if (frame is NetworkMessage message)
                    gameThreadActions.Enqueue(() => CommandReceived?.Invoke(message.Command));
                else
                    log.Warning($"Unexpected frame {frame.GetType()}");
            }
            gameThreadActions.Enqueue(() => SetState(MultiplayerClientState.Disconnected));
        } catch (Exception exception) {
            if (running)
                log.Error($"Direct client connection failed: {exception.Message}");
            gameThreadActions.Enqueue(() => SetState(MultiplayerClientState.Error));
        } finally {
            try {
                tcpClient.Close();
            } catch (Exception) {
                // ignore
            }
        }
    }

    private void SetState(MultiplayerClientState state) {
        State = state;
        StateChanged?.Invoke(state);
    }

}
