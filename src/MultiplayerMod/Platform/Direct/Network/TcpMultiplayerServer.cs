using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using JetBrains.Annotations;
using MultiplayerMod.Core.Collections;
using MultiplayerMod.Core.Dependency;
using MultiplayerMod.Core.Logging;
using MultiplayerMod.Core.Unity;
using MultiplayerMod.Multiplayer.Commands;
using MultiplayerMod.Multiplayer.Commands.Registry;
using MultiplayerMod.Network;
using MultiplayerMod.Platform.Direct.Network.Components;
using MultiplayerMod.Platform.Steam.Network.Messaging;
using UnityEngine;

namespace MultiplayerMod.Platform.Direct.Network;

/// <summary>
/// TCP implementation of <see cref="IMultiplayerServer"/> for the direct (LAN/IP) transport. Mirrors
/// <c>SteamServer</c>'s semantics exactly: reliable ordered delivery, server-side command routing
/// (execute-on-server vs. forward-to-peers), and host <c>SkipHost</c> filtering keyed on the shared
/// <see cref="LocalPlayerId"/>. Sockets are read on per-connection background threads; all game-facing events
/// (connect/disconnect/command) are queued and drained on the game thread in <see cref="Tick"/>, because command
/// execution and the execution-level context are thread-bound.
/// </summary>
[Dependency, UsedImplicitly]
public class TcpMultiplayerServer : IMultiplayerServer {

    public MultiplayerServerState State { get; private set; } = MultiplayerServerState.Stopped;

    public IMultiplayerEndpoint Endpoint {
        get {
            if (State != MultiplayerServerState.Started)
                throw new InvalidOperationException("Server isn't started");
            // The host's own client dials this to establish its loopback connection.
            return new DirectServerEndpoint("127.0.0.1", DirectNetworkConfig.Port);
        }
    }

    public List<IMultiplayerClientId> Clients => new(clients.Keys);

    public event Action<MultiplayerServerState>? StateChanged;
    public event Action<IMultiplayerClientId>? ClientConnected;
    public event Action<IMultiplayerClientId>? ClientDisconnected;
    public event Action<IMultiplayerClientId, IMultiplayerCommand>? CommandReceived;

    private readonly Core.Logging.Logger log = LoggerFactory.GetLogger<TcpMultiplayerServer>();

    private readonly MultiplayerCommandRegistry commands;
    private readonly IMultiplayerClientId currentPlayer;

    // Mutated only on the game thread (via Tick). Send() also runs on the game thread, so no locking is needed.
    private readonly Dictionary<IMultiplayerClientId, Connection> clients = new();
    private readonly ConcurrentQueue<ServerEvent> events = new();

    private TcpListener? listener;
    private Thread? acceptThread;
    private volatile bool running;
    private GameObject? gameObject;

    public TcpMultiplayerServer(MultiplayerCommandRegistry commands, LocalPlayerId localPlayerId) {
        this.commands = commands;
        currentPlayer = localPlayerId.Value;
    }

    public void Start() {
        SetState(MultiplayerServerState.Starting);
        try {
            listener = new TcpListener(IPAddress.Any, DirectNetworkConfig.Port);
            listener.Start();
            running = true;
            acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "DirectServerAccept" };
            acceptThread.Start();
            log.Info($"Direct server listening on 0.0.0.0:{DirectNetworkConfig.Port}");
        } catch (Exception exception) {
            log.Error($"Failed to start direct server: {exception}");
            Reset();
            SetState(MultiplayerServerState.Error);
            throw;
        }
        gameObject = UnityObject.CreateStaticWithComponent<DirectServerComponent>();
        SetState(MultiplayerServerState.Started);
    }

    public void Stop() {
        if (State <= MultiplayerServerState.Stopped)
            throw new InvalidOperationException("Server isn't started");

        if (gameObject != null)
            UnityObject.Destroy(gameObject);
        Reset();
        SetState(MultiplayerServerState.Stopped);
    }

    public void Tick() {
        while (events.TryDequeue(out var serverEvent)) {
            // Isolate each event: a throwing command must not abort the rest of the drained batch.
            try {
                ProcessEvent(serverEvent);
            } catch (Exception exception) {
                log.Error($"Failed to process server event {serverEvent.Kind}: {exception}");
            }
        }
    }

    public void Send(IMultiplayerClientId clientId, IMultiplayerCommand command) {
        if (!clients.TryGetValue(clientId, out var connection)) {
            log.Warning($"Cannot send {command} to unknown or disconnected client {clientId}");
            return;
        }
        SendCommand(command, MultiplayerCommandOptions.None, new SingletonCollection<Connection>(connection));
    }

    public void SendAll(IMultiplayerCommand command) => Send(command, MultiplayerCommandOptions.None);

    public void Send(IMultiplayerCommand command) => Send(command, MultiplayerCommandOptions.SkipHost);

    private void Send(IMultiplayerCommand command, MultiplayerCommandOptions options) {
        IEnumerable<KeyValuePair<IMultiplayerClientId, Connection>> recipients = clients;
        if (options.HasFlag(MultiplayerCommandOptions.SkipHost))
            recipients = recipients.Where(entry => !entry.Key.Equals(currentPlayer));

        SendCommand(command, options, recipients.Select(it => it.Value).ToList());
    }

    private void ProcessEvent(ServerEvent serverEvent) {
        switch (serverEvent.Kind) {
            case ServerEventKind.Connected:
                var connection = serverEvent.Connection!;
                clients[connection.Id!] = connection;
                log.Debug($"Client connected {connection.Id}");
                ClientConnected?.Invoke(connection.Id!);
                break;
            case ServerEventKind.Disconnected:
                if (clients.Remove(serverEvent.Id!)) {
                    log.Debug($"Client disconnected {serverEvent.Id}");
                    ClientDisconnected?.Invoke(serverEvent.Id!);
                }
                break;
            case ServerEventKind.Message:
                RouteMessage(serverEvent.Id!, serverEvent.Message!);
                break;
        }
    }

    private void RouteMessage(IMultiplayerClientId id, NetworkMessage message) {
        var configuration = commands.GetCommandConfiguration(message.Command.GetType());
        if (configuration.ExecuteOnServer) {
            CommandReceived?.Invoke(id, message.Command);
        } else {
            var recipients = clients.Where(it => !it.Key.Equals(id)).Select(it => it.Value).ToList();
            SendCommand(message.Command, message.Options, recipients);
        }
    }

    private void SendCommand(
        IMultiplayerCommand command, MultiplayerCommandOptions options, IEnumerable<Connection> connections
    ) {
        // Serialize once, write to every recipient — the world save broadcast would be ruinous otherwise.
        var framed = DirectFraming.Serialize(new NetworkMessage(command, options));
        foreach (var connection in connections) {
            try {
                DirectFraming.Write(connection.Stream, framed);
            } catch (Exception exception) {
                log.Error($"Failed to send {command} to {connection.Id}: {exception.Message}");
            }
        }
    }

    private void AcceptLoop() {
        while (running) {
            TcpClient tcpClient;
            try {
                tcpClient = listener!.AcceptTcpClient();
            } catch (Exception) {
                break; // listener stopped
            }
            tcpClient.NoDelay = true;
            var connection = new Connection(tcpClient);
            var thread = new Thread(() => ReadLoop(connection)) { IsBackground = true, Name = "DirectServerRead" };
            thread.Start();
        }
    }

    private void ReadLoop(Connection connection) {
        try {
            if (DirectFraming.Read(connection.Stream) is not DirectHello hello) {
                log.Warning("Connection closed before handshake");
                connection.Close();
                return;
            }
            connection.Id = new DirectMultiplayerClientId(hello.ClientId);
            events.Enqueue(ServerEvent.ForConnected(connection));

            while (running && connection.Running) {
                var frame = DirectFraming.Read(connection.Stream);
                if (frame == null)
                    break;
                if (frame is NetworkMessage message)
                    events.Enqueue(ServerEvent.ForMessage(connection.Id!, message));
                else
                    log.Warning($"Unexpected frame {frame.GetType()} from {connection.Id}");
            }
        } catch (Exception exception) {
            if (running && connection.Running)
                log.Error($"Read error from {connection.Id}: {exception.Message}");
        } finally {
            connection.Close();
            if (connection.Id != null)
                events.Enqueue(ServerEvent.ForDisconnected(connection.Id));
        }
    }

    private void SetState(MultiplayerServerState state) {
        State = state;
        StateChanged?.Invoke(state);
    }

    private void Reset() {
        running = false;
        try {
            listener?.Stop();
        } catch (Exception) {
            // ignore
        }
        listener = null;
        foreach (var connection in clients.Values)
            connection.Close();
        clients.Clear();
        while (events.TryDequeue(out _)) { }
    }

    private class Connection {
        public TcpClient Client { get; }
        public NetworkStream Stream { get; }
        public DirectMultiplayerClientId? Id { get; set; }
        public volatile bool Running = true;

        public Connection(TcpClient client) {
            Client = client;
            Stream = client.GetStream();
        }

        public void Close() {
            Running = false;
            try {
                Client.Close();
            } catch (Exception) {
                // ignore
            }
        }
    }

    private enum ServerEventKind { Connected, Disconnected, Message }

    private class ServerEvent {
        public ServerEventKind Kind { get; }
        public Connection? Connection { get; }
        public IMultiplayerClientId? Id { get; }
        public NetworkMessage? Message { get; }

        private ServerEvent(
            ServerEventKind kind, Connection? connection, IMultiplayerClientId? id, NetworkMessage? message
        ) {
            Kind = kind;
            Connection = connection;
            Id = id;
            Message = message;
        }

        public static ServerEvent ForConnected(Connection connection) =>
            new(ServerEventKind.Connected, connection, null, null);

        public static ServerEvent ForDisconnected(IMultiplayerClientId id) =>
            new(ServerEventKind.Disconnected, null, id, null);

        public static ServerEvent ForMessage(IMultiplayerClientId id, NetworkMessage message) =>
            new(ServerEventKind.Message, null, id, message);
    }

}
