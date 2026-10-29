# projectM in Meloday Music

The Windows x64 app ships unmodified libprojectM 4.1.8, GLEW 2.2.0 and a small C ABI bridge.
There is no runtime download. `Native/win-x64/*.dll` is copied beside the app for build and publish.
projectM needs a graphics driver providing OpenGL 3.3 and WGL offscreen pbuffers. Unsupported
drivers show an explanation in the visualizer; the original Win2D pictures still work.
ARM64 keeps the original visualizers; this native runtime is currently x64 only.

The bridge creates a pbuffer with a core OpenGL context, locks the selected preset, renders its
real output and reads opaque BGRA pixels into Win2D's existing composition surface. WGL contexts
are released after each call, so they can safely move between drawing threads. GLEW initialisation
and rendering are serialized. Each host keeps its own preset, buffers and graphics context.
The output is capped at 720p worth of pixels and the existing quality/power controller can reduce
the size and rate further. The audible stereo tap is read off the audio thread and resampled to
44.1 kHz for projectM's FFT. Casting uses the existing unavailable message.

## Rebuild

Install Visual Studio C++ tools, Windows SDK and CMake, then run:

```powershell
./tools/projectm/build.ps1
# Visual Studio 2022 instead:
./tools/projectm/build.ps1 -Generator 'Visual Studio 17 2022'
```

The exact projectM source archive is shipped under `Assets/ProjectM/Source` (including vendored
dependencies); it is LGPL-2.1-or-later and remains dynamically replaceable. The build script verifies
source checksums, downloads the pinned official GLEW package, builds the library and bridge with
the static MSVC runtime, and exercises all five presets. Smoke checks render changing stereo tones,
then silence, resize the surface, verify non-black animation, and save PPM frames for inspection.
The matching bridge source is here; GLEW's source is at https://github.com/nigels-com/glew/tree/glew-2.2.0.

## Preset selection

There is no published projectM per-preset popularity leaderboard. These are five curated selections
from projectM's default community "Cream of the Crop" pack, each carrying the pack's 5.0 rating:
Airhandler, Neon Space, Spiral Artifact, Cosmic Dust and Molten Glass. That rating is a preset's
embedded author/pack rating, not download statistics. They were selected for variety, compatibility,
and lack of external image-texture dependencies, and retain their original files and author credits.
See the shipped `Assets/ProjectM/PRESETS.md` for exact upstream paths and commit.

Upstream: https://github.com/projectM-visualizer/projectm/releases/tag/v4.1.8
and https://github.com/projectM-visualizer/presets-cream-of-the-crop.
