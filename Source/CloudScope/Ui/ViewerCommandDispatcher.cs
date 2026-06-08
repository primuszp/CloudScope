using CloudScope.Selection;

namespace CloudScope.Ui
{
    public sealed class ViewerCommandDispatcher
    {
        private const string SelectPrompt = "Select shape [Box/Sphere/Cylinder/Undo] <Box>:";
        private const string EditPrompt = "Adjust using grips or [Confirm/Undo/Cancel]:";
        private readonly ViewerController _viewer;
        private readonly Dictionary<string, Func<string, string>> _commands;
        private bool _selectActive;
        private bool _shapeChosen;

        public ViewerCommandDispatcher(ViewerController viewer)
        {
            _viewer = viewer;
            _commands = new(StringComparer.OrdinalIgnoreCase);

            Register(ExecuteSelect, "SEL", "SELECT");
            Register(_ => Confirm(), "CONFIRM", "ENTER");
            Register(_ => Cancel(), "CANCEL", "ESC", "ESCAPE");
            Register(_ => Undo(), "U", "UNDO");
            Register(_ => SetMode(InteractionMode.Label), "L", "LABELMODE");
            Register(_ => SetMode(InteractionMode.Navigate), "N", "NAV", "NAVIGATE");
            Register(SetLabel, "LABEL");
            Register(_ => HelpText, "HELP", "?");
        }

        private const string HelpText =
            "Commands: SELECT/SEL, LABEL <name>, NAVIGATE. SELECT options: Box, Sphere, Cylinder, Undo.";

        public string Execute(string commandText)
        {
            string command = commandText.Trim();
            if (command.Length == 0)
                return ExecuteEmpty();

            string[] parts = command.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            string verb = parts[0];
            string argument = parts.Length > 1 ? parts[1] : string.Empty;

            if (_selectActive && parts.Length == 1)
                return ExecuteSelectOption(verb);

            return _commands.TryGetValue(verb, out Func<string, string>? execute)
                ? execute(argument)
                : $"Unknown command: {command}";
        }

        private string ExecuteEmpty()
        {
            if (!_selectActive)
                return string.Empty;

            if (_viewer.HasActiveSelection)
                return Confirm();

            return _shapeChosen ? EditPrompt : SelectTool(SelectionToolType.Box);
        }

        private string ExecuteSelect(string option)
        {
            _selectActive = true;
            return string.IsNullOrWhiteSpace(option) ? SelectPrompt : ExecuteSelectOption(option);
        }

        private string ExecuteSelectOption(string option)
        {
            string normalized = option.Trim().ToUpperInvariant();
            return normalized switch
            {
                "B" or "BOX" => SelectTool(SelectionToolType.Box),
                "S" or "SPH" or "SPHERE" => SelectTool(SelectionToolType.Sphere),
                "C" or "CYL" or "CYLINDER" => SelectTool(SelectionToolType.Cylinder),
                "U" or "UNDO" => Undo(),
                "CONFIRM" or "ENTER" => Confirm(),
                "CANCEL" or "ESC" or "ESCAPE" => Cancel(),
                _ => $"Invalid option. {SelectPrompt}"
            };
        }

        private string SelectTool(SelectionToolType toolType)
        {
            _selectActive = true;
            _shapeChosen = true;
            _viewer.SetTool(toolType);
            return $"{toolType} selection started. Draw the volume, then use grips. {EditPrompt}";
        }

        private string Confirm()
        {
            if (!_selectActive || !_viewer.HasActiveSelection)
                return _selectActive ? EditPrompt : "No active SELECT command.";

            _viewer.ConfirmActiveSelection();
            _selectActive = false;
            _shapeChosen = false;
            return "Selection applied.";
        }

        private string Undo()
        {
            bool hadActiveSelection = _viewer.HasActiveSelection;
            bool undone = _viewer.UndoSelectionCommand();
            if (hadActiveSelection)
                _shapeChosen = false;
            return undone
                ? (_selectActive ? $"Last selection change undone. {SelectPrompt}" : "Last selection change undone.")
                : "Nothing to undo.";
        }

        private string Cancel()
        {
            if (!_selectActive)
                return "No active SELECT command.";

            if (_viewer.HasActiveSelection)
            {
                _viewer.CancelOrExitLabelMode();
                _shapeChosen = false;
                return $"Current selection cancelled. {SelectPrompt}";
            }

            _viewer.SetMode(InteractionMode.Navigate);
            _selectActive = false;
            _shapeChosen = false;
            return "SELECT cancelled.";
        }

        private string SetMode(InteractionMode mode)
        {
            _selectActive = false;
            _shapeChosen = false;
            _viewer.SetMode(mode);
            return $"Mode: {mode}";
        }

        private string SetLabel(string label)
        {
            if (string.IsNullOrWhiteSpace(label))
                return "Usage: LABEL <name>";

            _viewer.SetLabel(label);
            return $"Label: {label.Trim()}";
        }

        private void Register(Func<string, string> execute, params string[] aliases)
        {
            foreach (string alias in aliases)
                _commands.Add(alias, execute);
        }
    }
}
