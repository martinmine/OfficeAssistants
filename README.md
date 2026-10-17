# Office Assistants
<img width="1418" height="870" alt="image" src="https://github.com/user-attachments/assets/bd04b0ef-a50d-470a-be56-ed09e2cdee80" />


The Office XP assistants (Clippit and friends) are back, and this time they actually know
things. Ask the assistant a question in its balloon and it answers with
[Claude](https://www.anthropic.com/claude), reacting with its own animations. Give each
assistant [MCP](https://modelcontextprotocol.io) servers and it can use their tools too:
read your files, search your notes, check your calendar, or whatever the servers offer.

It's a C# app for Windows (WinUI 3) and macOS (AppKit). Characters, animations, sounds and localized strings are
read straight from the original Microsoft Agent files, and the assistant and its balloons
are drawn pixel for pixel: the tests compare the output with the reference screenshots in
`Screenshots/` and require zero differing pixels.

## Requirements

- Windows 10 version 1809 or later (Windows 11 recommended), x64 or ARM64.
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).
- **Developer Mode** enabled: *Settings → System → For developers → Developer Mode*.
  The app is an MSIX-packaged WinUI app, and Windows only registers development packages
  with Developer Mode on.
- [WinApp CLI](https://github.com/microsoft/winappCli) 0.7 or later, which builds,
  registers and launches the packaged app:

  ```powershell
  winget install --id Microsoft.WinAppCli --exact
  ```

  Open a new terminal afterwards so `winapp` is on the `PATH`.
- A **Claude API key** from the [Claude Console](https://platform.claude.com). You paste it
  into the app (it's stored encrypted for your Windows account), or set the
  `ANTHROPIC_API_KEY` environment variable.
- For local MCP servers, whatever they run on, typically [Node.js](https://nodejs.org)
  (`npx`) or [uv](https://docs.astral.sh/uv/) (`uvx`).

Visual Studio is optional. With the *WinUI application development* workload you can open
`OfficeAssistant.slnx` and press F5 on the `OfficeAssistant (Package)` profile instead of
using WinApp CLI.

## Getting started

```powershell
git clone https://github.com/martinmine/OfficeAssistants.git
cd OfficeAssistants
winapp run src\OfficeAssistant
```

The first build restores NuGet packages and can take a minute. The app opens its main
window, and Clippit appears in the bottom-right corner of the screen with a greeting.

To build everything (app, parser, tool and tests) without launching:

```powershell
dotnet build Windows.slnf
```

## On macOS

Needs macOS 12 or later, the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0),
Xcode, and the .NET macOS workload:

```bash
sudo dotnet workload install macos
```

Build and run:

```bash
dotnet build Mac.slnf
```

```bash
open "src/OfficeAssistant.Mac/bin/Debug/net10.0-macos/osx-arm64/Office Assistant.app"
```

(`osx-x64` on an Intel Mac.) The assistant floats over every app and Space, and a paper
clip in the menu bar shows or hides it, asks a question, picks another assistant and opens
the settings, which have the same gallery, options, Claude, instructions-and-tools and
animation tabs as the Windows window. The API key is kept in your Keychain. Right-click or
Control-click the assistant for its menu. MCP servers started with `npx` or `uvx` work
because the app takes `PATH` from your login shell.

`tools/package-mac.sh` builds a universal (Apple silicon and Intel) app and packs it into
`artifacts/OfficeAssistant.dmg`. Set `SIGN_IDENTITY` to a Developer ID certificate to sign
it with the hardened runtime, and `NOTARY_PROFILE` to notarize it; without them the app is
only ad hoc signed and other Macs will refuse to open it until it's allowed in System
Settings.

## Using the app

- In the main window's **Claude** card, paste your API key and click **Save key**. The
  model defaults to `claude-opus-5-5`; you can pick or type another.
- **Click** the assistant to open "What would you like to do?". Type a question and press
  Enter or click **Ask**. The answer appears in the balloon; long answers get a
  **See more...** link. Type a follow-up in the box under the answer, or click
  **New topic** to start over. **Cancel** (or Escape) stops a question in progress.
- **Drag** the assistant to move it, **double-click** it to play a random animation, and
  **right-click** it for Hide, Options, Choose Assistant, New Topic and Animate!
- In the main window:
  - **Gallery**: browse the assistants with Back/Next and click **Use this assistant**.
    The speaker button mutes the preview.
  - **Options**: show or hide the assistant, turn sounds on or off, pick the pixel size
    (1x is identical to the original) and the language for the assistant's strings.
  - **Claude**: the API key, the model, and buttons to ask a question or start a new
    conversation.
  - **Instructions and tools**: extra instructions and the MCP servers for the active
    assistant (see below).
  - **Animations**: play any animation in the character file.

## MCP servers

Each assistant has its own MCP servers, so Clippit can have your files while Rocky has
your calendar. Enter them in **Instructions and tools** using the same JSON as Claude
Desktop's `mcpServers` (you can paste a whole `claude_desktop_config.json`), then click
**Save and connect**:

```json
{
  "filesystem": {
    "command": "npx",
    "args": ["-y", "@modelcontextprotocol/server-filesystem", "C:\\Users\\you\\Documents"]
  },
  "remote": {
    "url": "https://example.com/mcp",
    "headers": { "Authorization": "Bearer ..." }
  }
}
```

- `command`, `args`, `env` and `cwd` start a local server over stdio.
- `url` and `headers` connect to a remote server (Streamable HTTP, falling back to SSE).
- The assistant asks in its balloon before every tool call (**Allow**, **Always** for the
  rest of the conversation, or **Deny**). Add `"alwaysAllow": ["tool_name"]`, or `["*"]`
  for every tool of a server, to skip asking.
- `"disabled": true` keeps a server in the list without connecting to it.

The cards below the button show whether each server connected and which tools it offers.
The assistant also has a built-in tool to play its own animations. Settings live in
`%LOCALAPPDATA%\OfficeAssistant\settings.json` (redirected into the package's folder when
the app runs packaged); the path is shown at the bottom of the card.

## How it works

`OfficeAssistant.Core/Chat` holds the conversation code:

- `AssistantChat` runs the conversation with the Claude Messages API (the official
  `Anthropic` NuGet package). It sends the assistant's tools, runs the tool calls Claude
  asks for, and loops until Claude answers. Requests opt into server-side fallback, so a
  request the model declines for policy reasons is retried on a fallback model.
- `McpHub` connects to the active assistant's MCP servers with the official
  `ModelContextProtocol` package and names their tools `server__tool`.
- `AnswerFormatter` turns Claude's answer into plain text for the balloon and splits long
  answers into pages.
- `AppSettings`/`SettingsStore` load and save the settings; the API key is encrypted with
  Windows DPAPI.

## Assistant files

The original files live in `Assistants/` and are bundled with the app. Each of the eight
assistants (`CLIPPIT`, `DOT`, `F1`, `GENIUS`, `LOGO`, `MNATURE`, `OFFCAT` and `ROCKY`) comes as
two files:

- `.acs` is the full character with every animation, used on screen.
- `.acg` is the gallery preview Office shows in its "Choose Assistant" dialog. It has the
  same format but only contains the `Wave` animation, and the gallery in Options shows it.

An assistant with only an `.acg` file can still be picked, but it can do no more than wave.
To add other assistants, put their files in `Assistants/` and rebuild, or load them at
runtime with **Add assistant files (.acs, .acg)...**.

## Running the tests

```powershell
dotnet test Windows.slnf
```

The tests parse every file in `Assistants/`, check the localized strings, text editing,
the character's and balloon's mouse and keyboard handling, and animation playback, and
render Clippit and four balloons for pixel comparisons with the screenshots.
`tests/AcsFormat.Tests` runs on any OS (`dotnet test Mac.slnf` on a Mac);
`tests/OfficeAssistant.Windows.Tests` needs GDI and DPAPI and skips itself elsewhere.

## Developer tool

`tools/AcsTool` inspects and exports assistant files:

```powershell
# States, animations and localized strings
dotnet run --project tools\AcsTool -- info Assistants\CLIPPIT.ACS

# Every frame as PNG and every sound as WAV
dotnet run --project tools\AcsTool -- export Assistants\CLIPPIT.ACS out\clippit

# A balloon as PNG (answer, autocomplete, results, save or letter)
dotnet run --project tools\AcsTool -- balloon save out\save.png
```

`tools/extract_sprites.py` (Python with Pillow) regenerates the balloon bullet, arrow and
warning icon from the screenshots into `src/OfficeAssistant.Core/Rendering/BalloonSprites.g.cs`.

The balloon text on macOS comes from a pre-rendered Tahoma in
`src/OfficeAssistant.Core/Rendering/BalloonFont.g.cs`. Regenerate it from GDI on Windows
with `dotnet run --project tools\AcsTool -- bake-font src\OfficeAssistant.Core\Rendering\BalloonFont.g.cs`,
or from FreeType on a Mac with `tools/FontBaker/bake_font.c` (instructions in the file).

## Repository layout

| Path | Contents |
|------|----------|
| `src/AcsFormat` | Platform-neutral parser for `.acs`/`.acg` files: localized strings, states, animations (frames, branches, exit branches, mouth overlays), images with the Agent decompressor, and WAVE sounds. |
| `src/OfficeAssistant.Core` | Platform-neutral runtime: the Office XP balloon renderer, the character's and balloon's mouse and keyboard handling (`Interaction/`), animation and sound playback, idling, the conversation with Claude and MCP servers (`Chat/`), and the interfaces each platform implements (`Platform/`). |
| `src/OfficeAssistant.Platform.Windows` | The Win32 implementation of those interfaces: layered windows, GDI text, waveOut sound, the clipboard and DPAPI. |
| `src/OfficeAssistant.Presentation` | View models shared by the app shells. |
| `src/OfficeAssistant` | The WinUI 3 app. |
| `src/OfficeAssistant.Mac` | The macOS app: the AppKit implementation of the platform interfaces (`Platform/`), the menu bar item and the settings window (`Settings/`). |
| `tools/AcsTool` | Developer CLI described above (Windows only). |
| `tests/AcsFormat.Tests` | Portable unit tests. |
| `tests/OfficeAssistant.Windows.Tests` | GDI pixel-comparison and DPAPI tests. |
| `Assistants/` | Original assistant files. |
| `Screenshots/` | Office XP reference screenshots used by the tests. |

## Troubleshooting

| Problem | Fix |
|---------|-----|
| `Developer Mode is not enabled on this machine` | Turn on Developer Mode (see Requirements) and run again. |
| `winapp` is not recognized | Install WinApp CLI and open a new terminal. |
| `NETSDK1045` or a missing SDK error | Install the .NET 10 SDK. |
| The app starts but no assistant appears | Check that `Assistants/` contains the `.acs`/`.acg` files, then use **Show the Office Assistant** in Options. |
| "There's a problem: I need a Claude API key" | Paste a key in the **Claude** card and click **Save key**, or set `ANTHROPIC_API_KEY` and restart the app. |
| An MCP server shows "Failed" | Check that its command runs in a terminal (for example `npx` needs Node.js on the `PATH`), then click **Reconnect**. Remote servers that need OAuth sign-in aren't supported yet; use `headers` with a token. |
| The assistant looks too small or too large | With **Pixel size** on **Automatic** it follows the display scaling (2x at 200%). Pick a size in Options to override it; it scales by whole pixels so the art stays sharp. |

## Notes on the file format

- The localized info "extra data" holds Office's own strings: greetings and reminder
  captions, separated by `^^` (sections) and `~~` (entries).
- In a frame, the first image is drawn on top.
- Exit frame values `-1` and `-2` mean "continue with the next frame".
- The Office text ("What would you like to do?", button labels and help topics) is not in
  the character files and is English only.
