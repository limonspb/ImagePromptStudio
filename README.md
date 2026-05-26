# Image Prompt Studio

A local Windows desktop app (WPF, .NET 8) for generating images via the OpenAI Images API. Self-contained C# — no Python, no external CLI.

## Run

Double-click `launch.vbs` for the clean no-console launcher. It runs the published `published\ImagePromptStudio.exe` if present, otherwise falls back to `dotnet run`.

If you want to see console errors while debugging, double-click `run.bat`.

## Build

Double-click `build.bat`, or run:

```
dotnet publish -c Release -r win-x64 --self-contained true -o published
```

This produces a single self-contained `published\ImagePromptStudio.exe` (~72 MB) that bundles the .NET 8 runtime — no separate runtime install required on the target machine. The `published/` folder is ignored by git.

## Repository Layout

```
.image-prompt-studio-root   marker that tells the app this is the dev workspace
build.bat                   one-click publish into published\
launch.vbs                  silent launcher (uses published\ exe if present, else dotnet run)
run.bat                     dev launcher with a visible console
src/                        all source code
    ImagePromptStudio.csproj
    App.xaml + App.xaml.cs  WPF application entry point (StartupUri = Views/MainWindow.xaml)
    AssemblyInfo.cs         WPF theme info
    Infrastructure/         shared building blocks (ObservableObject, ...)
    Models/                 plain data types (Generation*, Project*)
    Services/               app logic (image generation, history, OpenAI, paths)
    Views/                  WPF windows and dialogs
published/                  build.bat output (gitignored)
test-data/                  shared local app state (gitignored, created on first run)
```

## Files And Projects

When the app starts it walks up from the executable looking for the `.image-prompt-studio-root` marker:

- **Marker found** (dev or published exe run from this repo) — all state lives under `<repo>\test-data\`. Both `bin\...\ImagePromptStudio.exe` (dev build) and `published\ImagePromptStudio.exe` share the same folder, so generated images from one mode are visible from the other.
- **Marker not found** (distributed exe) — all state lives under `<exe-folder>\data\`. Nothing scattered next to the exe.
- **`IMAGE_PROMPT_STUDIO_DATA`** (or legacy `IMAGE_PROMPT_STUDIO_ROOT`) — set this env var to override the data folder explicitly.

Inside the data folder:

- `projects.json` — project registry: active project plus the list of projects. If an old singular `project.json` exists and `projects.json` does not, the app reads it once and saves back to `projects.json`.
- `history.json` and `generated/` — the default project.
- `projects/<project-slug>/history.json` and `projects/<project-slug>/generated/` — non-default projects.

Generated image paths are always created inside the selected project's `generated` folder. The model dropdown loads available `gpt-image-*` model ids from the OpenAI Models API (filtering out dated snapshots), and falls back to a built-in list if that lookup fails. Per-model image settings are constrained locally to match the OpenAI Images API rules.

## What It Does

- Enter `Prompt`, `Constraints`, and `Negative`.
- Choose model settings from dropdowns in the settings panel.
- Click `Generate`.
- Each click creates a history item immediately and runs as its own job, so several images can generate at once.
- The generated image appears in the preview panel.
- In the preview panel, use the mouse wheel to zoom, drag the zoomed image to pan, double-click to fit, switch preview background color, copy the image, or save a copy.
- History rows clearly show `In Progress`, `Done`, `Failed`, `Canceled`, or `Missing File`.
- Completed history rows show image thumbnails.
- Click a history row to show the image plus the prompts and settings used for it.
- Use `Reuse` on the viewed item to load its prompts and settings back into the left-side form.
- Use `Edit Image` or `Edit Viewed` on a completed history item to make that image the edit reference. The prompt boxes are cleared, and edit runs may use an empty prompt when the selected model supports image edits.
- In-progress history items show an indeterminate progress bar in the scrolling history list.
- Use the project controls in the top-left to switch, create, open, or remove projects.
- The top-left OpenAI pill shows month-to-date API spend only when the key can access the organization Costs API.
- Multi-select history rows and right-click for `Cancel Selected`, `Delete Selected`, or `Delete All`.
- Drag any image file (`.png`, `.jpg`, `.jpeg`, `.webp`) onto the window to set it as the edit reference; the app auto-switches to `Edit reference image` mode when the selected model supports edits.
- Keyboard shortcuts: `Ctrl+Enter` runs `Generate`, `F5` re-runs the currently viewed item with the same prompts and settings, and `Delete` removes the selected rows when the history list has focus.
- The `Regenerate` button on the viewed item re-runs that exact prompt and settings without touching the left-side form.
- The preview context menu has `Show in Explorer` to reveal the generated file.
- The window title shows the active project and how many generations are currently running.

## Requirements

- To **build** from source: .NET 8 SDK. This workspace can use the user-local SDK at `%LOCALAPPDATA%\Microsoft\dotnet`.
- To **run** the published self-contained exe: nothing — the .NET 8 runtime is bundled into `ImagePromptStudio.exe`.
- `OPENAI_API_KEY` set in your Windows environment. If it is missing, the app prompts for it and saves it to the Windows user environment before starting.
- To show month-to-date OpenAI spend, the app calls the organization Costs API. OpenAI's examples use an admin key for this endpoint; if your normal API key cannot read costs, set `OPENAI_ADMIN_KEY` in your Windows environment. If costs are not accessible, the app hides the spend pill.

The app calls the OpenAI Images API (`/v1/images/generations` and `/v1/images/edits`) directly over HTTPS. No Python, no external CLI, no other binaries required.

The app uses `gpt-image-1.5` by default because it supports transparent PNG output.
