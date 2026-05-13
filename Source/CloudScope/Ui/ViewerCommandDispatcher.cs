using CloudScope.Selection;

namespace CloudScope.Ui
{
    public sealed class ViewerCommandDispatcher
    {
        private readonly ViewerController _viewer;

        public ViewerCommandDispatcher(ViewerController viewer)
        {
            _viewer = viewer;
        }

        public string Execute(string commandText)
        {
            string command = commandText.Trim();
            if (command.Length == 0)
                return string.Empty;

            string[] parts = command.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            string verb = parts[0].ToUpperInvariant();
            string argument = parts.Length > 1 ? parts[1] : string.Empty;

            switch (verb)
            {
                case "SEL":
                case "SELECT":
                    return ExecuteSelect(argument);

                case "B":
                case "BOX":
                    _viewer.SetTool(SelectionToolType.Box);
                    return "Tool: Box";

                case "SPH":
                case "SPHERE":
                    _viewer.SetTool(SelectionToolType.Sphere);
                    return "Tool: Sphere";

                case "CYL":
                case "CYLINDER":
                    _viewer.SetTool(SelectionToolType.Cylinder);
                    return "Tool: Cylinder";

                case "L":
                case "LABELMODE":
                    _viewer.SetMode(InteractionMode.Label);
                    return "Mode: Label";

                case "N":
                case "NAV":
                case "NAVIGATE":
                    _viewer.SetMode(InteractionMode.Navigate);
                    return "Mode: Navigate";

                case "LABEL":
                    if (string.IsNullOrWhiteSpace(argument))
                        return "Usage: LABEL <name>";
                    _viewer.SetLabel(argument);
                    return $"Label: {argument}";

                case "CONFIRM":
                case "ENTER":
                    _viewer.ConfirmActiveSelection();
                    return "Confirm";

                case "CANCEL":
                case "ESC":
                case "ESCAPE":
                    _viewer.CancelOrExitLabelMode();
                    return "Cancel";

                case "HELP":
                case "?":
                    return "Commands: SELECT [Box/Sphere/Cylinder], LABEL <name>, LABELMODE, NAVIGATE, CONFIRM, CANCEL";

                default:
                    return $"Unknown command: {command}";
            }
        }

        private string ExecuteSelect(string option)
        {
            if (string.IsNullOrWhiteSpace(option))
                return "Select [Box/Sphere/Cylinder] <Box>:";

            string normalized = option.Trim().ToUpperInvariant();
            SelectionToolType? toolType = normalized switch
            {
                "B" or "BOX" => SelectionToolType.Box,
                "S" or "SPH" or "SPHERE" => SelectionToolType.Sphere,
                "C" or "CYL" or "CYLINDER" => SelectionToolType.Cylinder,
                _ => null
            };

            if (toolType == null)
                return "Invalid option. Select [Box/Sphere/Cylinder] <Box>:";

            _viewer.SetTool(toolType.Value);
            return $"Tool: {toolType.Value}";
        }
    }
}
