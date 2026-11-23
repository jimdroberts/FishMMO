using System.Runtime.CompilerServices;

// The generator's internals (the place builders, PlaceContext) were visible to every test and editor tool while it was part
// of FishMMO.Shared; split out, it keeps that access for its tests, its editor and the World Design
// module that names its places.
[assembly: InternalsVisibleTo("FishMMO.Naming.Tests")]
[assembly: InternalsVisibleTo("FishMMO.Shared.NameGenerator.Editor")]
[assembly: InternalsVisibleTo("FishMMO.WorldDesign.Editor")]
[assembly: InternalsVisibleTo("FishMMO.WorldDesign.Tests")]
[assembly: InternalsVisibleTo("FishMMO.Buffs.Tests")]
