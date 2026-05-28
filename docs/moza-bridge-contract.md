# MOZA Bridge Contract

The preferred MOZA path is now the SDK's C# runtime loaded dynamically from `drivers\moza-sdk\x64`. Use this native bridge contract only if the C# runtime is not sufficient or exposes a compatibility issue.

The WPF app is intentionally separated from MOZA SDK redistribution and ABI details. If the portable app finds `drivers\MozaForceBridge.dll` next to `MozaStarCitizen.exe`, it loads that DLL and calls the exports below.

The bridge should return `0` for success and a non-zero error code for failure.

```cpp
extern "C" __declspec(dllexport) int __cdecl MozaBridge_Initialize();

extern "C" __declspec(dllexport) int __cdecl MozaBridge_PlayEffect(
    int effectKind,
    double intensity,
    double frequencyHz,
    int durationMs,
    const wchar_t* stateKey);

extern "C" __declspec(dllexport) int __cdecl MozaBridge_StopEffect(
    const wchar_t* stateKey);

extern "C" __declspec(dllexport) int __cdecl MozaBridge_StopAll();
```

Effect kinds (the `effectKind` argument):

- `0`: Periodic vibration, used for quantum spool.
- `1`: Short bump, used for landing and impact.
- `2`: Sustained state vibration, used while in atmosphere.

`durationMs == 0` means sustained until stopped. `stateKey` is used for stateful effects such as `quantum-spool` and `atmosphere`.
