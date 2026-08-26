# CloudScope Command System

CloudScope is driven by one command system, built on the useful parts of the AutoCAD command
model without depending on Autodesk assemblies. Everything the viewer can do is a command;
menus, toolbars, shortcuts, scripts and viewport picks are ways of issuing one, not separate
paths into the viewer.

The runtime, the prompt API, the command table, the system variables and the undo manager
live in `CloudScope.Core/Commands`. UI projects are adapters over them.

## Writing a command

A command is a public method marked with `CommandMethodAttribute`. It returns
`IEnumerable<PromptStep>` and takes a `CommandContext`:

```csharp
[CommandMethod("ROTATE", "RO", Flags = CommandFlags.NoUndoMarker,
    Group = CommandGroup.Edit, Scope = CommandScope.Document,
    Summary = "Rotates the selection volume about a world axis.",
    Syntax = "ROTATE [X/Y/Z] <angle in degrees>")]
public IEnumerable<PromptStep> Rotate(CommandContext context)
{
    var viewer = context.GetTarget<ViewerController>();
    Editor ed = context.Editor;

    PromptStep axis = ed.GetKeywords("Specify rotation axis [X/Y/Z] <Z>:", AxisKeywords)
                        .WithDefaultKeyword("Z");
    yield return axis;

    PromptDoubleStep angle = ed.GetAngle("Specify rotation angle in degrees:");
    yield return angle;
    if (!angle.IsOk) yield break;

    ed.WriteMessage(viewer.RotateActiveSelection(angle.Single, AxisIndex(axis.Keyword)));
}
```

The body is a coroutine: it yields the question it wants answered and reads the answer on the
next line. A command therefore keeps its state in locals, not in fields on the command class,
and the code reads in the order the user is asked.

**Cancelling costs nothing to write.** `Escape` disposes the iterator, which resumes the body
at its `yield` and runs any `finally` block. A command that leaves something live — `SELECT`
leaves a selection volume on screen — puts it away in `finally`. There is no per-command reset
switch to keep in step with the prompts.

`Summary` is required: registration fails without it, because `HELP` is generated from the
table and a hand-written command list is the thing that rots.

## Asking questions

`Editor` is the command line as a command sees it. Each method returns a step to yield:

| Method | Answers with |
| --- | --- |
| `GetKeywords` | one of the prompt's keywords |
| `GetString`, `GetLine` | free text (`GetLine` takes the rest of the line, spaces included) |
| `GetInteger`, `GetDouble`, `GetDistance`, `GetAngle` | a number, optionally ranged |
| `GetPoint`, `GetCorner` | a world point, typed as `x,y,z` **or picked in the viewport** |
| `GetScreenPoint` | a viewport pixel position, typed as `x,y` or clicked |
| `GetFileNameForOpen`, `GetFileNameForSave`, `GetDirectory` | a path, typed or chosen in the shell's file dialog |

Every step reports a `PromptStatus`: `OK`, `Keyword`, `Cancel` or `None` (Enter with no
default). Steps are configured fluently — `.WithDefault(…)`, `.WithRange(…)`,
`.WithKeywords(…)`, `.WithDefaultKeyword(…)`, `.TakingRest()`.

A pending point prompt owns the next viewport click: `ViewerController.MouseDown` hands it to
the command instead of treating it as a selection gesture. This is why `ZOOM Window`, `MOVE`,
`PIVOT` and `PAN` are interactive without any of them having a second, gesture-shaped
implementation.

## Runtime behaviour

- Command and keyword matching is case-insensitive; a keyword's capitalised letters define its
  abbreviation (`CONFirm` accepts `CONF`).
- Arguments on the command line answer successive prompts, so `ZOOM W 10,10 200,200` runs
  without stopping. A prompt with nothing left to consume asks the user.
- Empty Enter repeats the last completed command; `NoHistory` commands are not repeated.
- `Escape`, `ESC` and `CANCEL` cancel the active command.
- A keyword of the active prompt always beats a command name, which is what keeps a value like
  `Z 1 2` at a filter prompt from being taken as `ZOOM`.
- At a keyword-only prompt a known command name starts that command — how menus and toolbars
  issue commands mid-conversation.
- An apostrophe runs a transparent command from inside another one (`'STATUS`), the only
  unambiguous way in at a prompt that accepts arbitrary text.
- Commands publish `CommandStarted`, `CommandEnded`, `CommandCancelled` and `CommandFailed`.
- Only one modal command is active at a time.

## The command table

`CommandRuntime` builds a `CommandDescriptor` per command: global name, aliases, flags, group,
scope, summary, syntax and the implementing method. It is the single source behind `HELP`,
`HELP <command>`, autocomplete, the menu, and the coverage report. Duplicate names or aliases
fail at registration.

`CommandScope` states what a command needs — `Application`, `Viewer` or `Document` — and
`CommandGroup` orders the help and names the menu section it belongs to.

## System variables

`SystemVariableTable` names the viewer's state. A variable stores nothing: it is a reader and
a writer pointing at the field that already exists, so it cannot drift from what the viewer is
actually doing. `SETVAR` and `GETVAR` read and write it, `SETVAR ? *` lists it, and the
convenience commands (`POINTSIZE`, `COLORBY`, `PROJECTION`) write through the same setters.

Registered today: `PDSIZE`, `PERSPECTIVE`, `COLORSOURCE`, `VPORTLAYOUT`, `VIEWNAME`,
`SELMODE`, `SELTOOL`, `CLABEL`, `CINSTANCE`, `PTMAX`, `PTRESIDENT`, `RENDERBACKEND`,
`SOURCENAME`, `LOADEDPOINTS`, `VISIBLEPOINTS`, `FPS`, `LABELCOUNT`.

## Undo

`UndoManager` groups a command's changes into one reversible step. The dispatcher opens a mark
on `CommandStarted`, commits it on `CommandEnded`, and rolls it back on `CommandCancelled` and
`CommandFailed`. A command flagged `NoUndoMarker` opens no mark, so stepping back never lands
on a `ZOOM`.

`UNDO` takes a count, `Mark` and `Back`; `REDO` reapplies. Label changes report themselves
through `LabelManager.ActionRecorded`, so a labelling command is a single undo step.

## Scripts

`SCRIPT <path>` runs a file of commands, one per line, `;` starting a comment. A command
cannot start another while it is itself active, so `SCRIPT` queues its lines with
`Editor.RunAfter` and the runtime runs them once the command returns. A line that fails or
cancels stops the rest.

## Menus and shells

`CommandMenu` is the single menu definition. Every entry is a command string, so a menu can
only do what the user could type. The Avalonia shell projects it into a native or classic
menu; the full-screen ImGui shell renders it as a menu bar.

The Avalonia shell answers a `PromptFileStep` with the platform file dialog, and echoes
prompts answered by a viewport pick onto its command line through
`ICommandOutputSource.OutputProduced`.

## Keeping it honest

`Source/CloudScope.CommandChecks` runs without a display:

```bash
dotnet run --project Source/CloudScope.CommandChecks
```

It checks that every command declares a summary and syntax, that every menu entry resolves in
the table, and that the runtime's conversation rules hold — prompts, inline arguments,
keyword abbreviations and defaults, invalid keywords re-asking, cancellation unwinding the
command body, transparency, repeat, deferred lines, system variables and undo.

It also prints the coverage report (`COVERAGE` in the viewer): it reads the IL of every
command — following the compiler's iterator state machine and the command classes' own
helpers — and lists the public `ViewerController` members no command reaches. Continuous
gestures and frame plumbing are excluded by name, because a gesture is input, not a command.
The report currently shows full coverage, and a new viewer capability that no command reaches
will show up there rather than waiting to be noticed.
