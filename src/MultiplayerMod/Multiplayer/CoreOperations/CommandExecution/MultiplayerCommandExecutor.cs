using System;
using System.Collections.Generic;
using JetBrains.Annotations;
using MultiplayerMod.Core.Dependency;
using MultiplayerMod.Core.Logging;
using MultiplayerMod.ModRuntime;
using MultiplayerMod.ModRuntime.Context;
using MultiplayerMod.Multiplayer.Commands;
using MultiplayerMod.Multiplayer.Commands.Registry;
using MultiplayerMod.Network;

namespace MultiplayerMod.Multiplayer.CoreOperations.CommandExecution;

[Dependency, UsedImplicitly]
public class MultiplayerCommandExecutor {

    private readonly Core.Logging.Logger log = LoggerFactory.GetLogger<MultiplayerCommandExecutor>();

    private readonly ExecutionLevelManager executionLevelManager;
    private readonly MultiplayerCommandRegistry registry;
    private readonly CommandExceptionHandler exceptionHandler = new();

    private readonly MultiplayerCommandRuntimeAccessor runtimeAccessor;

    // Game commands received while the Game level isn't active yet (e.g. a client still loading a world) are
    // held here and replayed, in order, once the base level reaches Game — otherwise they are silently dropped
    // and cascade into missing-object failures for later commands.
    private readonly Queue<(IMultiplayerClientId? clientId, IMultiplayerCommand command)> pendingGameCommands = new();

    public MultiplayerCommandExecutor(
        Runtime runtime,
        ExecutionLevelManager executionLevelManager,
        MultiplayerCommandRegistry registry
    ) {
        this.executionLevelManager = executionLevelManager;
        this.registry = registry;
        runtimeAccessor = new MultiplayerCommandRuntimeAccessor(runtime);
        executionLevelManager.BaseLevelChanged += OnBaseLevelChanged;
    }

    public void Execute(IMultiplayerClientId? clientId, IMultiplayerCommand command) {
        var configuration = registry.GetCommandConfiguration(command.GetType());
        switch (configuration.CommandType) {
            case MultiplayerCommandType.System:
                RunCatching(clientId, command);
                break;
            case MultiplayerCommandType.Game:
                ExecuteGameCommand(clientId, command);
                break;
            default:
                throw new CommandConfigurationException(
                    $"{command.GetType()} has unsupported type \"{configuration.Type}\""
                );
        }
    }

    private void ExecuteGameCommand(IMultiplayerClientId? clientId, IMultiplayerCommand command) {
        if (!executionLevelManager.LevelIsActive(ExecutionLevel.Game)) {
            log.Debug(() => $"Deferring {command} until Game execution level is active");
            pendingGameCommands.Enqueue((clientId, command));
            return;
        }
        executionLevelManager.RunUsingLevel(ExecutionLevel.Command, () => RunCatching(clientId, command));
    }

    private void OnBaseLevelChanged(ExecutionLevel level) {
        if (level < ExecutionLevel.Multiplayer) {
            // Multiplayer stopped / returned to single player: queued commands belong to a dead session.
            if (pendingGameCommands.Count > 0) {
                log.Debug(() => $"Dropping {pendingGameCommands.Count} deferred command(s) on level {level}");
                pendingGameCommands.Clear();
            }
            return;
        }
        if (level < ExecutionLevel.Game || pendingGameCommands.Count == 0)
            return;

        log.Debug(() => $"Replaying {pendingGameCommands.Count} deferred command(s)");
        while (pendingGameCommands.Count > 0) {
            var (clientId, command) = pendingGameCommands.Dequeue();
            executionLevelManager.RunUsingLevel(ExecutionLevel.Command, () => RunCatching(clientId, command));
        }
    }

    private void RunCatching(IMultiplayerClientId? clientId, IMultiplayerCommand command) {
        try {
            log.Trace(() => $"Executing {command}");
            command.Execute(new MultiplayerCommandContext(clientId, runtimeAccessor));
        } catch (Exception exception) {
            exceptionHandler.Handle(command, exception);
        }
    }

}
