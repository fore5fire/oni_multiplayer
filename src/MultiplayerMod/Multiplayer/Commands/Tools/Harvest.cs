using System;
using System.Linq;
using MultiplayerMod.Game.UI.Tools.Events;

namespace MultiplayerMod.Multiplayer.Commands.Tools;

[Serializable]
public class Harvest : AbstractDragToolCommand<HarvestTool> {

    public Harvest(DragCompleteEventArgs arguments) : base(arguments) { }

    protected override void InitializeTool(HarvestTool tool) {
        base.InitializeTool(tool);
        // ONI switched tool option state from Dictionary<string, ToggleState> to ToggleData[].
        tool.options = new[] {
            new ToolParameterMenu.ToggleData("HARVEST_WHEN_READY", ToolParameterMenu.ToggleState.Off, false),
            new ToolParameterMenu.ToggleData("DO_NOT_HARVEST", ToolParameterMenu.ToggleState.Off, false)
        };
        for (var i = 0; i < tool.options.Length; i++) {
            if (Arguments.Parameters?.Contains(tool.options[i].name) == true)
                tool.options[i].state = ToolParameterMenu.ToggleState.On;
        }
    }

}
