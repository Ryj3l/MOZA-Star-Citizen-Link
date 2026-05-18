using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Moza.ScLink.DirectInput.Tests")]

// T-07 Issue #27 Pass-2 V9-bug-fix (C): App.Tests and Effects.Tests compose chains via
// FallbackForceFeedbackDevice's internal convenience overload (flat-device-list form). Test-only
// ctors are internal + InternalsVisibleTo per house style — keeps the public surface honest while
// letting cross-layer test assemblies use the back-compat shape.
[assembly: InternalsVisibleTo("Moza.ScLink.App.Tests")]
[assembly: InternalsVisibleTo("Moza.ScLink.Effects.Tests")]
