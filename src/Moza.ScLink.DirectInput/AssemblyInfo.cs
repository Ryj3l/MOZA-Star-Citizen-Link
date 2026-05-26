using System.Runtime.CompilerServices;

// DirectInput.Tests exercises this project's internal members (VorticeDirectInputDevice internals).
// The former App.Tests/Effects.Tests grants existed only for the legacy FallbackForceFeedbackDevice's
// internal ctor, removed with the legacy device chain in T-27 (#15).
[assembly: InternalsVisibleTo("Moza.ScLink.DirectInput.Tests")]
