# DirectInput Output

The app tries Windows DirectInput force feedback before falling back to preview output. It enumerates attached game controllers with force feedback support, prefers devices with `MOZA`, `AB6`, or `AB9` in the name, and then creates effects for the three initial mappings.

Current mappings:

- Quantum spool: sine periodic effect, 34 Hz, finite duration from the parsed event pattern.
- Landing/impact: short constant-force bump.
- In-atmosphere: sustained sine periodic effect, 18 Hz, stopped by an atmosphere-exit event.

DirectInput effect intensity is clamped to the normal `0..10000` force-feedback range. Sustained effects use a state key so repeated events replace the old effect instead of stacking force indefinitely.

This path still depends on how the AB6 driver exposes the device to Windows. If the device does not advertise DirectInput force feedback, the app will use preview output unless a MOZA SDK bridge DLL is present.
