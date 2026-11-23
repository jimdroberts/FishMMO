using System.Runtime.CompilerServices;

// The World Design module was split out of this assembly and keeps the
// access it had to the dashboards' internals (AddressablesDashboard.MoveClientBackdropEntries and the like).
[assembly: InternalsVisibleTo("FishMMO.WorldDesign.Editor")]
[assembly: InternalsVisibleTo("FishMMO.WorldDesign.Client.Editor")]
[assembly: InternalsVisibleTo("FishMMO.WorldDesign.Tests")]
[assembly: InternalsVisibleTo("FishMMO.Shared.NameGenerator.Editor")]
