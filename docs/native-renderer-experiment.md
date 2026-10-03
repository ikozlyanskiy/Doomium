# Native PCB rendering experiment

The local `feature/native-pcb-renderer` branch adds **Tools → Convert → Rect to Doomium (native experiment)** and **Rect to Doomium (regions preview)**. The existing **Rect to Doomium** command still uses the window renderer, so all three paths can be compared on the same machine.

The `native experiment` path converts each game frame to PCB `Fill` objects. It samples the 320×200 frame, uses the colors of up to twelve existing mechanical layers, and merges neighboring pixels into rectangles. Depending on the image, resolution drops as low as 32×20 to keep the count at or below 640 fills. The command creates a pool of 640 hidden fills up front; frames reuse those fills without opening a new PCB transaction every tick.

The `regions preview` path creates sixteen mechanical layers named after their colors and one PCB Region on each layer. Every frame builds geometric polygon contours from merged same-color rectangles, usually at 96×60 to 160×100 cells. The regions and added layers are removed when playback stops; original color settings are restored. If Altium crashes during playback, close the test board without saving to discard the temporary layer stack changes.

Altium colors these objects by **layer**. The fill mode temporarily shows the chosen mechanical layers and restores their previous visibility when playback ends. Both native modes hide a selected solid-fill rectangle during playback so it cannot cover the image, then restore it. A nearly transparent child control receives game input without painting the game image.

Use a disposable `.PcbDoc` in 2D view for the first test. Playback changes the board in memory and may affect its Undo history. Do not save the board while the game is running. If Altium exits unexpectedly, check the test board for leftover fills before saving. The target of 20–30 FPS is unverified; `%LOCALAPPDATA%\Doomium\doomium.log` records the achieved FPS, number of fills, and update time. Middle-click inside the frame to capture or release the mouse. The native mode does not draw the window renderer's FPS/help text; its controls are the same as in the README.

Build and install this branch only after closing Altium:

```powershell
.\Doomium\Doomium\Install-Doomium.ps1
```

To return to the published renderer, close Altium, switch to `main`, and run the same installer again. The source installer builds the selected branch before copying it. The isolated frame planner check runs with:

```powershell
dotnet run --project .\tests\NativeFramePlanner\NativeFramePlanner.csproj -c Release
```
