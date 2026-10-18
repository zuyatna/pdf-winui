# Obsidian, GitHub and JetBrains page reading themes

The Layer 2 selector now offers Original, Dark (Warm Palette), OLED (Pure Black), Obsidian Dark, GitHub Dark and JetBrains Dark. All dark themes share the existing rendering path: luminance remap, image preservation, scanned-page handling and cache invalidation. Layer 1 is unchanged.

## Preset colors

| Theme | Page background | Text | Canvas | Source |
| --- | --- | --- | --- | --- |
| Obsidian Dark | `#1C1C1C` | `#DADADA` | `#282828` | Obsidian CSS variables docs: dark `--color-base-00`, `--color-base-100`, `--color-base-20` |
| GitHub Dark | `#0D1117` | `#F0F6FC` | `#010409` | Primer primitives: dark `bgColor.default` (neutral.1), `fgColor.default` (neutral.12), `bgColor.inset` (neutral.0) |
| JetBrains Dark | `#1E1F22` | `#BCBEC4` | `#2B2D30` | IntelliJ `expUI_dark.theme.json`: Gray1 / Gray2. Editor text `#BCBEC4` was not confirmed from an official source |

## Settings change

Per-theme palettes are stored in `CustomPalettes` (theme name to page/text/canvas hex). Themes without an entry use their preset. Legacy flat `DarkPageBackgroundHex`, `DarkTextHex`, `DarkCanvasHex`, `OledPageBackgroundHex`, `OledTextHex` and `OledCanvasHex` values are migrated when settings load and are omitted on the next save. Existing numeric `ReadingTheme` values remain valid.

## Automated checks

```powershell
dotnet build .\PdfReader.slnx -c Debug -p:Platform=x64                               # 0 Warning(s), 0 Error(s)
dotnet test .\PdfReader.Core.Tests\PdfReader.Core.Tests.csproj -c Debug              # 66/66 passed
dotnet test .\PdfReader.Settings.Tests\PdfReader.Settings.Tests.csproj -c Debug      # 18/18 passed
```

TDD RED: new tests failed compilation on missing `ReadingThemeMode` members, `ReadingThemePalette.GetDefault`, `ReadingPaletteHex` and `CustomPalettes`. GREEN after implementation. Core remains free of UI references; `PdfiumWorker` and native handle ownership were not touched.

## Pending interactive checks

No F5, UI Automation or screenshot check was performed. Select each new theme; confirm page, text and canvas colors; edit/apply and reset per theme; toggle Dark reading off/on; restart and confirm persistence; open with an older `settings.json` containing custom Warm/OLED colors and confirm they survive.
