using System.Runtime.CompilerServices;
using System.Windows;

// App.Tests composes the real service graph via the internal Program.ConfigureServices (T-27
// integration tests) and exercises the internal test-seam ctors on GameLogPathProvider /
// AppSettingsStore. Internal + InternalsVisibleTo per house style — keeps the public API surface honest.
[assembly: InternalsVisibleTo("Moza.ScLink.App.Tests")]

[assembly:ThemeInfo(
    ResourceDictionaryLocation.None,            //where theme specific resource dictionaries are located
                                                //(used if a resource is not found in the page,
                                                // or application resource dictionaries)
    ResourceDictionaryLocation.SourceAssembly   //where the generic resource dictionary is located
                                                //(used if a resource is not found in the page,
                                                // app, or any theme specific resource dictionaries)
)]
