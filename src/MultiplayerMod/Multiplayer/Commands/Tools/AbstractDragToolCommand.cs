using System;
using System.Collections.Generic;
using MultiplayerMod.Core.Extensions;
using MultiplayerMod.Game.Context;
using MultiplayerMod.Game.UI.Tools.Context;
using MultiplayerMod.Game.UI.Tools.Events;

namespace MultiplayerMod.Multiplayer.Commands.Tools;

[Serializable]
public abstract class AbstractDragToolCommand<T> : MultiplayerCommand where T : DragTool, new() {

    protected DragCompleteEventArgs Arguments;

    protected AbstractDragToolCommand(DragCompleteEventArgs arguments) {
        Arguments = arguments;
    }

    // ReSharper disable once Unity.IncorrectMonoBehaviourInstantiation
    public override void Execute(MultiplayerCommandContext context) {
        var tool = new T();
        InitializeTool(tool);
        GameContext.Override(CreateContext(), () => InvokeTool(tool));
    }

    protected virtual IGameContext CreateContext() => new PrioritySettingsContext(Arguments.Priority);

    protected virtual void InitializeTool(T tool) {
        tool.downPos = Arguments.CursorDown;

        if (tool is not FilteredDragTool filteredTool)
            return;

        // ONI switched filter state from Dictionary<string, ToggleState> to ToggleData[].
        var filters = new List<ToolParameterMenu.ToggleData> {
            new(ToolParameterMenu.FILTERLAYERS.ALL, ToolParameterMenu.ToggleState.Off, true)
        };
        Arguments.Parameters?.ForEach(
            it => filters.Add(new ToolParameterMenu.ToggleData(it, ToolParameterMenu.ToggleState.On, true))
        );
        filteredTool.currentFilters = filters.ToArray();
    }

    protected virtual void InvokeTool(T tool) => Arguments.Cells.ForEach(it => tool.OnDragTool(it, 0));

}
