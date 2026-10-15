# Office Assistants
<img width="1418" height="870" alt="image" src="https://github.com/user-attachments/assets/bd04b0ef-a50d-470a-be56-ed09e2cdee80" />


A proof of concept that brings back the Office XP assistants (Clippit and friends) as a
WinUI 3 app written in C#. Characters, animations, sounds and localized strings are read
straight from the original Microsoft Agent files, and the assistant and its balloons are
drawn pixel for pixel: the tests compare the output with the reference screenshots in
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
dotnet build OfficeAssistant.slnx
```

## Using the app

- **Click** the assistant to open "What would you like to do?". Type a question (try
  "print") and press Enter or click Search.
- **Drag** the assistant to move it, **double-click** it to play a random animation, and
  **right-click** it for Hide, Options, Choose Assistant and Animate!
- In the main window:
  - **Gallery**: browse the assistants with Back/Next and click **Use this assistant**.
    The speaker button mutes the preview.
  - **Options**: show or hide the assistant, turn sounds on or off, pick the pixel size
    (1x is identical to the original) and the language for the assistant's strings.
  - **Balloons**: open each recreated Office XP balloon.
  - **Animations**: play any animation in the character file.
  - **Localized strings**: the name, description, greetings and reminders stored in the file.

## Assistant files

The original files live in `Assistants/` and are bundled with the app:

- `.acs` is a full character with every animation. Only `CLIPPIT.ACS` is included.
- `.acg` is the gallery preview Office shows in its "Choose Assistant" dialog. It has the
  same format but only contains the `Wave` animation.

So every assistant can be picked, but only Clippit does more than wave. To get the full
set for another assistant, put its `.acs` file (for example `ROCKY.ACS`) in `Assistants/`
and rebuild, or load it at runtime with **Add assistant files (.acs, .acg)...**.

## Running the tests

```powershell
dotnet test tests\AcsFormat.Tests
```

The tests parse every file in `Assistants/`, check the localized strings, text editing and
animation playback, and render Clippit and four balloons for pixel comparisons with the
screenshots.

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

## Repository layout

| Path | Contents |
|------|----------|
| `src/AcsFormat` | Platform-neutral parser for `.acs`/`.acg` files: localized strings, states, animations (frames, branches, exit branches, mouth overlays), images with the Agent decompressor, and WAVE sounds. |
| `src/OfficeAssistant.Core` | Windows runtime: layered Win32 windows for the character and balloon, the Office XP balloon renderer, animation and sound playback, idling and the Answer Wizard. |
| `src/OfficeAssistant` | The WinUI 3 app. |
| `tools/AcsTool` | Developer CLI described above. |
| `tests/AcsFormat.Tests` | Unit and pixel-comparison tests. |
| `Assistants/` | Original assistant files. |
| `Screenshots/` | Office XP reference screenshots used by the tests. |

## Troubleshooting

| Problem | Fix |
|---------|-----|
| `Developer Mode is not enabled on this machine` | Turn on Developer Mode (see Requirements) and run again. |
| `winapp` is not recognized | Install WinApp CLI and open a new terminal. |
| `NETSDK1045` or a missing SDK error | Install the .NET 10 SDK. |
| The app starts but no assistant appears | Check that `Assistants/` contains the `.acs`/`.acg` files, then use **Show the Office Assistant** in Options. |
| The assistant looks small on a high-DPI screen | Choose a larger **Pixel size** in Options; it scales by whole pixels so the art stays sharp. |

## Notes on the file format

- The localized info "extra data" holds Office's own strings: greetings and reminder
  captions, separated by `^^` (sections) and `~~` (entries).
- In a frame, the first image is drawn on top.
- Exit frame values `-1` and `-2` mean "continue with the next frame".
- The Office text ("What would you like to do?", button labels and help topics) is not in
  the character files and is English only.
