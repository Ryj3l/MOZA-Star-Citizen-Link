using System.Runtime.CompilerServices;
using System.Windows;

// T-07 Issue #27 Pass-2 (S2): the App project exposes an internal injection constructor on
// MainViewModel (in addition to the public parameterless production ctor) so the App.Tests
// project can compose MainViewModel with a pre-built ForceFeedbackController for the D4
// reactivity tests. Test-only ctors are internal + InternalsVisibleTo per house style — keeps
// the production public API surface honest.
[assembly: InternalsVisibleTo("Moza.ScLink.App.Tests")]

[assembly:ThemeInfo(
    ResourceDictionaryLocation.None,            //where theme specific resource dictionaries are located
                                                //(used if a resource is not found in the page,
                                                // or application resource dictionaries)
    ResourceDictionaryLocation.SourceAssembly   //where the generic resource dictionary is located
                                                //(used if a resource is not found in the page,
                                                // app, or any theme specific resource dictionaries)
)]
