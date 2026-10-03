# Native PCB rendering experiment

The local `feature/native-pcb-renderer` branch adds **Tools → Convert → Rect to Doomium (native experiment)**. The existing **Rect to Doomium** command still uses the window renderer, so the two paths can be compared on the same machine.

The experimental path converts each game frame to a small set of PCB `Fill` objects. It samples the 320×200 frame, uses the colors of up to twelve mechanical layers, and merges neighboring pixels into rectangles. Depending on the image, resolution drops from 80×50 to as low as 32×20 to keep the count at or below 640 fills. These are actual PCB objects. The command creates a pool of 640 hidden fills up front; frames reuse those fills without opening a new PCB transaction every tick. Keyboard and mouse input are polled while the game has captured the mouse; the native mode creates no game window over the PCB.

Altium colors these fills by **layer**, so this path cannot reproduce the full game palette without changing the board's layer colors. It temporarily shows the chosen mechanical layers and restores their previous visibility when playback ends. The native command removes the generated fills when it stops or the frame disappears.

Use a disposable `.PcbDoc` in 2D view for the first test. Playback changes the board in memory and may affect its Undo history. Do not save the board while the game is running. If Altium exits unexpectedly, check the test board for leftover fills before saving. The target of 20–30 FPS is unverified; `%LOCALAPPDATA%\Doomium\doomium.log` records the achieved FPS, number of fills, and update time. Middle-click inside the frame to capture or release the mouse. The native mode does not draw the window renderer's FPS/help text; its controls are the same as in the README.

Build and install this branch only after closing Altium:

```powershell
.\Doomium\Doomium\Install-Doomium.ps1
```

To return to the published renderer, close Altium, switch to `main`, and run the same installer again. The source installer builds the selected branch before copying it. The isolated frame planner check runs with:

```powershell
dotnet run --project .\tests\NativeFramePlanner\NativeFramePlanner.csproj -c Release
```
