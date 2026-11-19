#if UNITY_EDITOR
using FishMMO.Shared;

// The FishMMO Dashboard tool pages this assembly's tools use (DashboardPageAttribute). A module that brings its own
// tools declares its own pages the same way, in its own editor assembly.
[assembly: DashboardPage(DashboardToolAttribute.Validate, Description = "Checks that report problems in project assets. Most only log; the ones that change assets say so.")]
[assembly: DashboardPage(DashboardToolAttribute.UnitTests, Description = "Runs the FishMMO.UnitTests EditMode assembly in this editor. Results go to the console.")]
[assembly: DashboardPage(DashboardToolAttribute.UITests, Description = "UI Toolkit panel checks, renders and probes. Renders and reports are written to disk; see the console for where. Client editor only: these tools are absent under the Server build subtarget.")]
[assembly: DashboardPage(DashboardToolAttribute.Maintenance, Description = "One-off wiring passes, mock content and test scene generators. Most of these write assets or scenes.")]
[assembly: DashboardPage(DashboardToolAttribute.AITools, Description = "Repairs and migrations for NPC prefabs and AI assets.")]
[assembly: DashboardPage(DashboardToolAttribute.WorldSceneDetails, Description = "The world scene details cache is rebuilt by every build; rebuild it here after changing a world scene's settings.")]
[assembly: DashboardPage(DashboardToolAttribute.SpawnTables)]
[assembly: DashboardPage(DashboardToolAttribute.WorldMap, Description = "World maps are build output. Client builds bake them, build, then remove them and rebuild the world scene details cache. A manual bake is for previewing; run Remove Baked Maps and rebuild World Scene Details before committing.")]
#endif
