# GP3 Tyre Fix

Fixes the broken tyre/wheel rendering in **Grand Prix 3** and **GP3 2000** on the
hardware renderer, and optionally forces the 3D wheel model
in every camera view.

Copyright © James Knopp 2026.

---

## The problem it fixes

On modern PCs GP3's wheels/tyres render with a **magenta band /
wrong texture**. The cause is power-of-two textures: GP3 only runs its
power-of-two texture path when the GPU reports it *requires* power-of-two textures
— true on 1998 cards, false on modern GPUs. The wrap math then samples past the
real texture into filler pixels.

It supports **GP3.exe (v1.13)** and **gp3_2000.exe**, auto-detecting which one
you're using.

---

## What's included

| Tool | Use it if… |
|---|---|
| **`GP3TyreInjectorGui.exe`** | A windowed app: pick your version, point it at your decrypted exe, and it launches GPxPatch and injects the fix in memory while you play. The game exe on disk is never modified. |

> ⚠️ **You must use the DECRYPTED game exe.** The original encrypted/protected exe
> does not contain the code these tools look for. The GUI checks this for you and
> tells you if the exe is encrypted or the wrong build.

---

## Quick start (GUI)

1. Put `GP3TyreInjectorGui.exe` in your GP3 folder.
2. Run it (it requests administrator — needed to write to the game process).
3. Choose your version: **GP3 (v1.13)** or **GP3 2000**.
4. **Browse** to your decrypted game exe. You should get a green
   *"Decrypted … verified — all 5 patch sites found."*
5. Confirm the **GPxPatch** path (auto-filled if found).
6. Click **Launch & inject**. Start the game as normal; the log shows
   *"applied 5"* once GP3 loads.

Your choices are saved, so next time just open it and click Launch & inject.

---

## Recommended dgVoodoo2 settings

GP3's hardware renderer is DirectDraw / Direct3D 7, which dgVoodoo2 wraps. Because
this fix handles the power-of-two problem *inside the game*, **you do not need
DxWnd** — run dgVoodoo2 at full speed. Use a recent dgVoodoo2 (2.7x or newer).

Open `dgVoodooCpl.exe` (in your GP3 folder) and set:

**General tab**
- **Output API:** DirectX 11 (or the newest your card supports).
- **Scaling mode:** *Stretched, keep aspect ratio (4:3)* so GP3 isn't distorted on
  a widescreen monitor — *unless* you use **WidePrix** for true widescreen, in
  which case use the full-screen stretch.
- Untick **dgVoodoo Watermark**.
- Tick **Fast video memory access**.

**DirectX tab**
- **Adapter:** dgVoodoo Virtual 3D Accelerated Card.
- **VRAM: 512 MB or more.** *This is the only setting that matters for the fix:*
  forcing power-of-two makes a few textures larger (e.g. 640×480 → 1024×512), so
  give it room. 512 MB+ is plenty (the default is usually fine too).
- **Resolution:** Unforced (let GP3 / WidePrix choose), or a fixed value matching
  your monitor.
- **Antialiasing (MSAA):** 4× or 8× — smoother car/track edges (GP3 is light, so
  high AA costs nothing).
- **Anisotropic filtering:** 8× or 16× — sharper textures at shallow angles.
- Leave texture **filtering = Application controlled** (GP3 sets its own).

Apply, then launch GP3 as usual (GPxPatch → dgVoodoo). AA / AF / resolution are
down to taste and your GPU; only the VRAM note is fix-related.

> **Using GPxPatch + WidePrix + dgVoodoo?** Keep them exactly as they are. GPxPatch
> handles launch/compatibility, WidePrix the widescreen, dgVoodoo the rendering,
> and this tool injects the tyre fix on top — they don't conflict.

---


## What it actually changes

Five one-byte-style edit that make GP3 round textures to power-of-two and
(optionally) always use the 3D wheel model. The injector applies them to the
running process only; the patcher applies them to the file (with a backup). Full
addresses, signatures and the mechanism are in **`GP3_TyreFix_PatchSpec.md`**.

The on-disk game exe is never modified by the injector — so GPxPatch and dgVoodoo
behave exactly as before.

---

## Requirements

- Windows with **.NET Framework 4** (present on all modern Windows).
- The **decrypted** GP3.exe (v1.13) or gp3_2000.exe.
- Your existing dgVoodoo / GPxPatch setup.

## Credits

James Knopp, 2026. Source for all tools is in
the `source/` folder.
