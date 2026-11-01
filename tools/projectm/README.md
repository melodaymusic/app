# projectM

The x64 app ships libprojectM 4.1.8 and GLEW 2.2.0 unmodified, plus a small bridge (`bridge.cpp`)
that renders projectM offscreen and hands the frames to Win2D. The DLLs live in
`src/MelodayMusic.App/Native/win-x64`. projectM needs OpenGL 3.3; on drivers without it the
visualizer menu says so and the other visualizers still work.

## Rebuilding

Install the Visual Studio C++ tools, the Windows SDK and CMake, then run:

```powershell
./tools/projectm/build.ps1
# with Visual Studio 2022:
./tools/projectm/build.ps1 -Generator 'Visual Studio 17 2022'
```

The script checks the source checksums, downloads the pinned GLEW release, builds projectM and the
bridge, copies the DLLs into `Native/win-x64`, and renders each preset once with `smoke.cpp` to
check it works.

projectM is LGPL-2.1-or-later. Its exact source is included at
`src/MelodayMusic.App/Assets/ProjectM/Source`. GLEW's source is at
https://github.com/nigels-com/glew/tree/glew-2.2.0.

The five presets are listed in `src/MelodayMusic.App/Assets/ProjectM/PRESETS.md`.
