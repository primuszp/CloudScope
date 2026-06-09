# CloudScope Command System

The CloudScope command runtime follows the useful parts of the AutoCAD .NET
command model without depending on Autodesk assemblies. The runtime and
command-line session live in `CloudScope.Core`; UI projects are adapters.

## Command API

Commands are public instance methods marked with `CommandMethodAttribute`.
They receive a `CommandContext` and return a `CommandResult`.

```csharp
[CommandMethod("LABEL", Flags = CommandFlags.NoUndoMarker)]
public CommandResult Label(CommandContext context)
{
    if (context.Input.Length == 0)
        return CommandResult.Continue("Enter label name:");

    context.Viewer.SetLabel(context.Input);
    return CommandResult.End($"Label: {context.Input}");
}
```

`CommandResult.Continue` keeps the command active. The next command-line input
is delivered to the same method through `context.Input`. `Escape`, `ESC`, and
`CANCEL` cancel the active command globally.

## Runtime behavior

- Command and alias matching is case-insensitive.
- Empty Enter repeats the last completed command.
- `NoHistory` commands do not become the repeatable last command.
- Commands publish `CommandStarted`, `CommandEnded`, `CommandCancelled`, and
  `CommandFailed` lifecycle events.
- Only one modal command can be active.
- The runtime owns the current prompt shown by the Avalonia command line.

## Shared command-line session

`CommandLineSession` is the single implementation for staged command text,
Enter/Space submission, repeat, input recall, and output history. Avalonia and
the full-screen ImGui viewer render the same session behavior instead of
maintaining parallel history and staging implementations.

Viewer-specific commands remain in the viewer project because they invoke
`ViewerController`. The generic runtime, attributes, lifecycle, prompt, repeat,
and session state are in Core and have no rendering or UI dependency.

## Undo model

AutoCAD groups database mutations performed by a command into an undoable
operation and can use undo marks to roll a multi-step command back. CloudScope
currently exposes selection/label undo through `ViewerController`.

Commands which mutate selection or labels call that undo primitive. The command
flags and lifecycle are intentionally separated from the viewer so a future
document-level undo manager can create a mark on `CommandStarted`, commit it on
`CommandEnded`, and roll it back on `CommandCancelled`.

## Adding commands

Add methods to a command class and pass an instance to `CommandRuntime`.
Signatures are validated during registration. Duplicate command names or aliases
fail immediately.

## ZOOM command

`ZOOM` (`Z`) follows AutoCAD-style keyword and scale-factor input:

- `ZOOM E`, `ZOOM Extents`, and `ZOOM All` fit the complete point cloud.
- `ZOOM C` and `ZOOM Object` focus the object under the viewport center.
- Positive numeric values, `nX`, and `nXP` zoom around the viewport center.
- `ZOOM Window` is reserved until the editor supports interactive point prompts.

## Scriptable viewer commands

The current viewer command surface includes:

- `SELECT`, `CONFIRM`, `CANCEL`, `UNDO`
- `ZOOM`, `VIEW`, `PROJECTION`, `POINTSIZE`
- `LABEL`, `LABELMODE`, `SAVELABELS`, `LOADLABELS`, `CLEARLABELS`
- `NAVIGATE`, `HELP`

Menus, toolbars, OpenGL shortcuts, and SharpMetal shortcuts invoke these same
commands. Continuous pointer gestures and held-key navigation remain input
events; they are not duplicated state-changing command implementations.
