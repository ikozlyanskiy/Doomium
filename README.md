<h1 align="center">Doomium</h1>

<p align="center">
  <img src="docs/assets/doomium-in-altium.png" alt="Doomium running inside a PCB frame in Altium Designer" width="741">
</p>

<p align="center">
  <strong>DOOM, right on your PCB canvas.</strong><br>
  Draw a frame, run the command, and play without leaving Altium Designer.
</p>

<p align="center">
  <code>Altium Designer 25</code> &nbsp; <code>Windows x64</code> &nbsp; <code>30 FPS target</code> &nbsp; <code>GPL-2.0-or-later</code>
</p>

---

Doomium runs a color DOOM-compatible game in a live window over the PCB editor. The window tracks a selected PCB frame as you move, resize, or delete it. The game uses the open-source [Managed Doom](https://github.com/sinshu/managed-doom) engine and a DOOM-compatible IWAD such as [Freedoom](https://freedoom.github.io/).

> [!IMPORTANT]
> **Current frame input:** four selected, connected PCB tracks forming an axis-aligned rectangle. Altium's smart **Place → Rectangle** object is not reliably exposed through the AD25 SDK; use **Tools → Convert → Explode Rectangle to Free Primitives**, then select the four resulting tracks. A single axis-aligned fill also works. The menu command is still named **Rect to Doomium**.

## Get playing

1. Download the `Doomium-v0.2.0-ad25-win-x64.zip` release asset, extract it, and close Altium Designer after saving your documents.
2. In PowerShell, from the extracted folder, run:

   ```powershell
   powershell -ExecutionPolicy Bypass -File .\Install-Doomium.ps1 -Prebuilt
   ```

3. Put your own `freedoom2.wad` or other DOOM-compatible IWAD beside the installed `Doomium.dll`, or choose a WAD when first prompted. The installer prints the installed folder. **No WAD is bundled.**
4. Start Altium, open a `.PcbDoc`, select the four-track frame, and choose **Tools → Convert → Rect to Doomium**.

The installer detects a single Altium data directory automatically, checks the menu resource against that installation's `AdvPcb.rcs`, registers Doomium on first install, backs up existing extension files, and verifies copies by SHA-256. If several Altium data directories exist, pass `-AltiumDataRoot 'C:\ProgramData\Altium\Altium Designer {…}'`. Use `-AltiumInstallDir` if AD25 is installed elsewhere. `-ValidateOnly` checks compatibility without changing the installation.

## Controls

| Action | Keyboard | Mouse |
| :-- | :-- | :-- |
| Move / strafe | `W` `S` / `A` `D` | — |
| Turn | `Q` `E` or `←` `→` | Move mouse while captured |
| Move forward / back | `↑` `↓` | — |
| Run | Hold `Shift` | — |
| Fire | `Ctrl` | Left button |
| Use / open | `Space` | Right button |
| Change weapon | `1`–`7` | — |
| Automap | `Tab` | — |
| Capture / release mouse | — | Middle button over the game |
| Show control guide | `F1` | — |
| Stop game | `Esc` or run menu command again | — |

Click the game to focus it. Middle-click over the image to capture the mouse; middle-click again to return to the normal Altium pointer. The top-left counter shows achieved render FPS.

## What happens to the board?

The selected tracks are a movable, resizable frame. Doomium displays the game in a child window over the PCB viewport; it **does not** turn video frames into copper, polygons, fills, or saved PCB data. This keeps gameplay responsive and fabrication output untouched. The video disappears when the game stops or its frame is deleted.

Game simulation runs at 35 tics/s, with a 30 FPS render target. Actual FPS depends on the machine and Altium's workload. The underlying game image is 320×200 and scales to the frame.

## Build from source

Requirements: Altium Designer 25 installed on Windows x64, a .NET SDK able to target `net6.0-windows`, and the vendored Managed Doom source in this repository.

```powershell
dotnet build .\Doomium\Doomium\Doomium.csproj -c Release
.\Doomium\Doomium\Install-Doomium.ps1
```

The source installer builds before installing. To create the distributable ZIP locally, run `./Build-Release.ps1`; it writes the ZIP and a SHA-256 checksum under `dist/`. Altium SDK DLLs are referenced from the local installation and are not redistributed.

## Credits and license

Doomium includes source from [Managed Doom](https://github.com/sinshu/managed-doom) at commit `9365696eb44326a3aab72c4bab217f7db8a87c96`. Its original copyright notices remain in the vendored files. Doomium and the bundled engine source are distributed under the **GNU GPL v2 or later**; see [LICENSE](LICENSE) and [third-party notices](THIRD_PARTY_NOTICES.md). DOOM and Freedoom game data are separate; bring your own IWAD.

<details>
<summary>Кратко по-русски</summary>

Скачайте ZIP из релиза, закройте Altium, распакуйте архив и запустите `powershell -ExecutionPolicy Bypass -File .\Install-Doomium.ps1 -Prebuilt`. Для рамки выделите четыре дорожки, образующие прямоугольник; обычный **Place → Rectangle** сначала разбейте через **Tools → Convert → Explode Rectangle to Free Primitives**. Затем выберите **Tools → Convert → Rect to Doomium**. Средняя кнопка мыши включает и выключает захват, `F1` показывает управление, `Esc` останавливает игру. WAD-файл нужен свой.

</details>
