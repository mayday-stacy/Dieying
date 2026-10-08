# AI control

Dieying can be driven by an AI agent. The agent works in the documents you have open, through the same commands you use: every change it makes is one undoable step, it shows up in the window as it happens, and Ctrl+Z takes it back like anything you did yourself. You keep working alongside it in the same window.

Editing tools work on visible documents and support undo. File tools can also open, save and export files as instructed by the connected client; those disk writes are not reversed by document undo. Review what the client is authorized to do.

## Switching it on

Tick Help > Allow AI Control. It is off by default and remembered between launches. While it is on, Dieying listens for agents on a private connection that only your own user account on this machine can reach; nothing is opened to the network. The status bar shows "AI connected" while an agent is attached, or how many are.

Only one Dieying window at a time can accept agents. If you open a second window it says so, and agents keep working in the first.

## Connecting a client

Dieying speaks the [Model Context Protocol](https://modelcontextprotocol.io) (MCP), which is how AI assistants are given tools. Any MCP client can connect: Claude Code, Claude Desktop, or another assistant that supports MCP servers.

An MCP client starts a small program and talks to it. For Dieying that program is `dieying.exe --mcp`, a bridge that carries the client's messages to the running Dieying. Register it with your client as a command:

- **Command**: the full path to `dieying.exe` inside the extracted Windows folder, or to the `dieying` executable in a source publish on another platform.
- **Arguments**: `--mcp`.

For example, a client configuration for a Windows copy extracted to `C:\Apps\Dieying` uses:

```json
{
  "command": "C:\\Apps\\Dieying\\dieying.exe",
  "args": ["--mcp"]
}
```

Replace that example path with your own. Keep the rest of the portable folder alongside the executable. If the client and editor use a custom profile, give both the same `DIEYING_DATA_DIR` and/or `DIEYING_MCP_PIPE` values.

Other clients take the same command and argument in their own configuration, usually a JSON file with a `command` and an `args` entry.

### The bridge

The bridge outlives Dieying. While Dieying is not running, or AI control is off, the client sees no tools and any call it makes is answered with a message saying so. The moment Dieying starts with AI control on, the tools appear, so you can start, quit, update and restart Dieying without touching the client. A request that arrives while the connection is being made waits for it, and a request that was in progress when Dieying quit gets an answer instead of hanging.

Add `--launch` after `--mcp` in the registration and the bridge starts Dieying itself when nothing answers, once per session. If you quit Dieying later, the bridge leaves it closed.

Some clients only read the tool list when they start. If a client shows no tools after Dieying started, restart the client once.

## What an agent can do

An agent gets tools covering most of what you can do from the menus:

- **Documents**: create a canvas, open a project or image, list and describe the open documents, save the project, export a PNG, JPEG or WebP, and render the document to see it.
- **Layers**: add a layer, place an image or SVG file as a layer, select, rename, hide, reorder, duplicate and delete layers, move, resize and rotate them, and set opacity and blend mode.
- **Content**: add text with a font, size, color, bold and italic; add rectangles, rounded rectangles, ellipses and lines; fill a layer; paint brush strokes with the brush, eraser, blur, smudge, dodge and burn, one at a time or many in one call.
- **Adjustments**: every adjustment, on the layer's pixels or as an adjustment layer.
- **Filters**: every filter but Camera Raw, including Dither and Painterly.
- **Selections**: marquee, lasso, wand, object and subject; select all, inverse, deselect; expand, contract, feather and move the selection.
- **Looking**: render the document, with a labelled grid to read coordinates from or a region at full size; read the colors at points; and trace the picture's edges.
- **Undo**: take back the last step, whoever made it.

The document list, a document's layers and its rendered image are also available as resources, for clients that attach context rather than call tools. The full list of tools and their parameters is in the [AI tool reference](ai-tools-reference.md).

Imports that require a conversion or develop dialog need your attention in the application. The Camera Raw Filter has no MCP tool. Editing is also refused while you are dragging on the canvas or another non-text edit is open; the client should retry after the interaction finishes.

## What to ask for

Agents do best with work you could explain to a colleague over your shoulder: put this logo bottom right at a fifth of the width with a drop shadow; remove the background and put the product on a gradient; brighten the shadows, add a vignette and export a WebP; lay out a card with this title in this font; apply the same treatment to these ten files. Name the layer or the file, say where things should go, and let the agent render to check its work.

Two kinds of request work less well. Asking an agent to draw something recognizable from scratch with brush strokes rarely produces a likeness, because a language model estimates where things are from a picture and is off by tens of pixels. For a hand-drawn look, ask for the Painterly filter on a photo, which paints from the pixels; or ask the agent to place the photo as a reference and use the edge tracing and color sampling tools, which give it the picture's real lines and colors to work from. And a task that needs your eye, such as retouching a face, is better done in steps you review with a render between them.

## Privacy

The default Windows pipe is `dieying-mcp`; on Linux and macOS it is `mcp.sock` inside Dieying's cache folder. `DIEYING_MCP_PIPE` overrides it. This is separate from Composa's connection; the `composa://` resource URIs in the [reference](ai-tools-reference.md) remain unchanged for protocol compatibility.

The connection between the client and Dieying stays on your machine: it is a named pipe on Windows and a socket file in Dieying's cache folder on Linux, both readable only by your own user account. What the agent sees of your document, and where that goes, depends on the AI client you use and its provider; Dieying itself sends nothing anywhere.
