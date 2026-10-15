# Office Assistant (WinUI 3 proof of concept)

The Office XP assistants (Clippit and friends), rebuilt in C# and WinUI 3 from the original
Microsoft Agent character files. Characters and balloons are drawn pixel for pixel: the
tests compare the output with the reference screenshots in `Screenshots/` and require zero
differing pixels.

## Projects

| Path | What it is |
|------|------------|
| `src/AcsFormat` | Platform-neutral parser for `.acs` characters and `.acg` gallery files: localized strings, states, animations (frames, branches, exit branches, mouth overlays), images (including the Agent LZ decompressor) and WAVE sounds. |
| `src/OfficeAssistant.Core` | Windows runtime: layered Win32 windows for the character and balloon, Office XP balloon renderer (GDI), animation player, sound playback, idling and the Answer Wizard. |
| `src/OfficeAssistant` | WinUI 3 app: assistant gallery, options, animation and balloon test panels, localized strings viewer. |
| `tools/AcsTool` | Developer CLI: `info`, `export` (frames as PNG, sounds as WAV) and `balloon` (render a balloon to PNG). |
| `tools/extract_sprites.py` | Regenerates the bullet, arrow and warning-icon sprites from the screenshots. |
| `tests/AcsFormat.Tests` | Parser tests plus pixel comparisons against the screenshots. |

## Running

```powershell
winapp run src\OfficeAssistant --detach
dotnet test tests\AcsFormat.Tests
dotnet run --project tools\AcsTool -- info Assistants\CLIPPIT.ACS
```

Packaged runs need Windows Developer Mode.

## Notes on the files

* `.acs` is a full character. `.acg` uses the same layout but is the gallery preview Office
  shows in the "Choose Assistant" dialog: it holds only the `Wave` animation. Only Clippit
  has an `.acs` here, so the other assistants can be picked but only wave. Drop their
  `.acs` files into `Assistants/` (or use "Add assistant files...") to get every animation.
* The localized info "extra data" holds Office's own strings: greetings and reminder
  captions, separated by `^^` (sections) and `~~` (entries).
* In a frame, the first image is the top-most one.
* Exit frame `-2` (and `-1`) mean "continue with the next frame".
* Balloon metrics (Tahoma 13 px without anti-aliasing, #FFFFCC fill, corner and tail
  shapes, spacing, button layout) were measured from the screenshots. The Office text
  ("What would you like to do?", button labels, help topics) is not in the character files
  and is English only.
