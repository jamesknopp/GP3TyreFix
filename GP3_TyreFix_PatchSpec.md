# GP3 Hardware Tyre Fix

## Summary
On the **hardware** renderer, Grand Prix 3's wheels/tyres render
with a magenta band / wrong texture. Root cause: GP3 only runs its **power-of-two**
texture-handling path when the GPU reports `D3DPTEXTURECAPS_POW2` — true on 1998 cards,
**false** on modern GPUs. The fix below forces GP3's POW2 path directly, so it
works under any wrapper at full speed.

Optionally, patch 5 forces the **3D wheel model** in every camera view (instead of the 2D
sprite GP3 uses in external/TV/replay/trackside cameras).

## Target build
- **GP3.exe v1.13** 
- Image base **0x400000**, **no relocations / no ASLR** → every VA below maps 1:1 in the
  running process, and **file offset = VA − 0x400000**.
- Build fingerprint (pristine): MD5 `2427A9F27DC83AF3F6F984E67DC47E70`.
- 0-shift / version self-check: bytes at VA `0x44556C` = `50 0F B7 1D 40 D2 81 00`.

## The patches
Every patch is a NOP of a conditional jump. **Patches 1–4 = the tyre fix** (force the four
`D3DPTEXTURECAPS_POW2` checks). **Patch 5 = optional** force-3D-wheels.

| # | VA | file off | original | patched | unique signature (jump in **[ ]**) | what it does |
|---|----|----|----|----|----|----|
| 1 | `0x50CA9A` | `0x10CA9A` | `74 51` | `90 90` | `F6 C1 02` **`74 51`** `8B 7C 24 24 BA 01 00 00 00 3B FA C6 05 94 7F CA 00 01` | force POW2 rounding — tex-create path A (surface + UV dims) |
| 2 | `0x50D26E` | `0x10D26E` | `74 4D` | `90 90` | `F6 C1 02` **`74 4D`** `BE 01 00 00 00 C6 05 94 7F CA 00 01` | force POW2 rounding of the **UV-scale dims** — drives the texture-coordinate wrap mask (**the decisive one** that removes the band) |
| 3 | `0x50D5F5` | `0x10D5F5` | `74 4D` | `90 90` | `F6 C2 02` **`74 4D`** `B9 01 00 00 00 C6 05 94 7F CA 00 01` | force POW2 rounding — texSel surface dims (`sub_50D430`) |
| 4 | `0x52032B` | `0x12032B` | `74 3A` | `90 90` | `F6 C2 02` **`74 3A`** `8B CE 3B C6 89 4C 24 40` | force POW2 rounding — D3DTextr `.bmp` surface dims |
| 5 | `0x4748F9` | `0x748F9` | `0F 8C DD 00 00 00` | `90 90 90 90 90 90` | `80 BA 5C 54 7E 00 01` **`0F 8C DD 00 00 00`** | NOP sprite/3D-wheel fork → 3D wheel mesh in all views |

> Always prefer **signature search** over the raw offset — it self-verifies the build and
> survives minor variants. Leave the SQUAREONLY checks (`test ?l,20h`, right after sites
> 1–4) **untouched**: we force power-of-two, not square.

## Build coverage — GP3 v1.13 and GP3 2000
The VAs above are for **GP3.exe v1.13**. **gp3_2000.exe** (MD5 `90B655135F2050D249B9DEDD9AAFA7AA`)
has the **same code, byte-identical except the relocated data-global addresses**, same
`imagebase 0x400000` / no-ASLR / `file_off = VA − 0x400000`. The moved globals: the
"was-rounded" flag `0xCA7F94 → 0xCF692D`, the wheel flag `0x7E545C → 0xB61260`. The jump
bytes and patch logic are unchanged. gp3_2000 addresses:

| # | gp3_2000 VA | file off | original | patched |
|---|----|----|----|----|
| 1 | `0x43F6BA` | `0x3F6BA` | `74 51` | `90 90` |
| 2 | `0x43FEFB` | `0x3FEFB` | `74 4D` | `90 90` |
| 3 | `0x440285` | `0x40285` | `74 4D` | `90 90` |
| 4 | `0x45D1DB` | `0x5D1DB` | `74 3A` | `90 90` |
| 5 | `0x4DD615` | `0xDD615` | `0F 8C DD 00 00 00` | `90 90 90 90 90 90` |

**Recommended:** drive both builds from the **wildcarded signatures** (the absolute-address
bytes set to "any"). They match each build's five sites uniquely, so a single signature set
patches v1.13, GP3 2000, and any same-code build with no hard-coded addresses. Use
`gp3_2000_locate.ps1` to (re)map the sites in any GP3 build. The included tools
(`GP3TyrePatcher.exe`, `GP3TyreInjector.exe`) already do this — the injector scans the live
`.text` for the signatures, so it patches whichever build GPxPatch launched.

## Why all four (mechanism)
GP3 builds its per-texel wrap mask as `index & (texW − 1)` (at `0x4458D7`), which is only
correct when the width is a power of two. It rounds the relevant dimensions up to a power of
two **only** when `D3DPTEXTURECAPS_POW2` is set — in four texture-setup functions. Sites 3–4
round the **surface allocation**; sites 1–2 round the dimensions used to compute the
**texture-coordinate scale / wrap** (site 2, `0x50D26E`, where GP3 does `shl …,0Eh` / `fdivr`
on the rounded width, is the decisive one). Setting the cap fires all four together, so a
faithful patch must force **all four** — forcing only the allocation pair is not enough (it
patches "live but does nothing").

## Applying it

### A) Static (on disk)
Write the patched bytes at the file offsets above (verify originals first). Keep a backup.

### B) Injected (in memory) — recommended when running under GPxPatch
No ASLR ⇒ the VAs are valid in the live process. To inject without disturbing anything else:

1. `OpenProcess(PROCESS_VM_OPERATION | PROCESS_VM_WRITE | PROCESS_VM_READ, FALSE, pid)`.
2. For each patch: `ReadProcessMemory(VA, len)` and **verify it equals the ORIGINAL bytes**.
   If not (different build / already patched / another tool changed them) → **skip + report**,
   never blind-write.
3. `WriteProcessMemory(VA, patched, len)` (it flips page protection for you; if a write ever
   fails, wrap it in `VirtualProtectEx(…, PAGE_EXECUTE_READWRITE, …)`).
4. `FlushInstructionCache(hProc, VA, len)` — these are code bytes.

**Timing.** Code patches take effect for everything executed *after* the write. Wheel/tyre
textures are created at **track/garage load**, not exe startup, so injecting any time after
the process exists and before entering a race is early enough. Simplest: poll for the GP3
process and inject as soon as it appears. (If ever injected after a texture was already built,
re-entering the garage re-creates it with the patched code.)

**Non-disruption guarantees (the point to make to other devs):**
- Only these 10 bytes change in the live image; the on-disk exe stays pristine, so GPxPatch
  (or any loader) sees the build it expects.
- GPxPatch's runtime patches are at unrelated addresses; verify-before-write means you never
  clobber bytes another tool set.
- dgVoodoo / `ddraw.dll` is never touched.

## Reference (verify-then-write)
```c
typedef struct { DWORD va; BYTE orig[8]; BYTE patch[8]; int len; } Patch;

static const Patch P[] = {
  { 0x50CA9A, {0x74,0x51}, {0x90,0x90}, 2 },
  { 0x50D26E, {0x74,0x4D}, {0x90,0x90}, 2 },
  { 0x50D5F5, {0x74,0x4D}, {0x90,0x90}, 2 },
  { 0x52032B, {0x74,0x3A}, {0x90,0x90}, 2 },
  { 0x4748F9, {0x0F,0x8C,0xDD,0x00,0x00,0x00}, {0x90,0x90,0x90,0x90,0x90,0x90}, 6 }, // optional: 3D wheels
};

for (int i = 0; i < 5; i++) {
    BYTE buf[8]; SIZE_T n;
    ReadProcessMemory(h, (LPCVOID)P[i].va, buf, P[i].len, &n);
    if (memcmp(buf, P[i].orig, P[i].len) != 0) continue;      // verify -> skip if not pristine
    WriteProcessMemory(h, (LPVOID)P[i].va, P[i].patch, P[i].len, &n);
    FlushInstructionCache(h, (LPCVOID)P[i].va, P[i].len);
}
```
(Signature-search variant: scan the module image for each unique signature and patch the
`[jump]` bytes within it — more robust than raw VAs.)

## Crowd sprites at distance (compare patch)

GP3 draws grandstand crowds two ways and switches per section by depth: **vertical
people-sprites** ("cards", clines JamID 0x3C0) within a threshold, and a **flat painted
backdrop** (farcrwd JamID 0x343) beyond it. The threshold is the dword
`gCrowdCardDepthThreshold` (v1.13 VA `0x809910`, GP3 2000 VA `0xB85714`, both `0x00030000`),
compared in the crowd builder (v1.13 `sub_44F31C` @0x44F378, 2000 @0x47034E) against each
section's projected camera-space depth (`[vtx+8]`): depth ≥ threshold → flat, < → sprites.
**Raising the effective threshold pushes the sprites out to all visible distances**, with no
distortion (sections still get real card geometry).

**Patch the COMPARE, not the data.** On **v1.13** `0x809910` is a static constant (no writers),
so a data write would work — **but on GP3 2000 the global is written at runtime**
(`mov [0xB85714], eax` @0x44A3BB), so a data write is clobbered immediately. The build-robust
fix is to rewrite the **four `cmp reg,[threshold]`** instructions to `cmp reg, imm32` — same
length, uses our value directly, ignores the global and its writer. The block is byte-identical
across builds (only the addresses differ); the four compares are `ecx/ebx/eax/edx` at block
offsets `+0x00/+0x0D/+0x1E/+0x2B`:

```
block sig (50 bytes, matches v1.13 0x44F378 + 2000 0x47034E once each):
  3B0D<va> 7D4C E8<rel> 3B1D<va> 7C33 C605<va>FF  EB1C  3B05<va> 7D2E E8<rel> 3B15<va> 7C
each cmp:  3B <0D|1D|05|15> <disp32>   ->   81 <F9|FB|F8|FA> <imm32>   (cmp ecx|ebx|eax|edx, imm32)
imm32 = 0x30000 × mult   (injector "× normal" multiplier, default 5×, range 1–64×; 1× = stock)
```

These are **code** bytes ⇒ `FlushInstructionCache` after writing. **Open:** beyond the
16-vert→12-vert geometry-LOD cutoff (a separate upstream distance) sections have no card
geometry, so the far horizon can still go flat — raising this covers the full card range but
not past that wall.
