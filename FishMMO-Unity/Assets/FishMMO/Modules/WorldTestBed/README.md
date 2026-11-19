# World Test Bed module

**Short description:** The world test bed's probes and switches (HarnessSettings, the player perf and WebGPU smoke probes,
the scene perf probe, cloud walk, cut digest, HLOD baker, scene generator, shader variant log): development tooling that
drives the world modules' internals. No game code names it.

Phase 4 of the [core and modules plan](https://claude.ai/artifact/FwoyRum2ScqjbE1gm8sm9k), moved from
Modules/WorldTestBed/Scene 2026-11-18. The test bed's scene components (WorldSimController, WorldSimCamera, WorldSimPanel,
WorldSimWalker) stay in the core's Modules/WorldTestBed/Scene: World Design dresses every generated world scene with them, so
they must load whatever modules are installed.

## Project Structure

```
Assets/FishMMO/Modules/WorldTestBed/
├── Scene/     FishMMO.TestHarness.World          WorldSimController, WorldSimCamera, WorldSimPanel, WorldSimWalker
├── World/     FishMMO.WorldTestBed               HarnessSettings, PlayerPerfProbe, WebGpuSmokeProbe
│   └── Editor FishMMO.TestHarness.World.Editor   ScenePerfProbe, WorldSimSceneGenerator, WorldHlodBaker, …
├── Weather/   FishMMO.TestHarness.Weather.Editor The weather sim render
├── Sky/       FishMMO.TestHarness.Sky.Editor     The sky sim render
└── README.md
```

The editor assembly keeps its name; the runtime part is the new FishMMO.WorldTestBed.

## Depends on

Every world module it drives (Terrain now; the weather, sky and water as they leave the core). Removing any of them removes
this module too. World Design's harness (Modules/WorldDesign/Tests/Harness) builds the perf player from it.

## License

This project is subject to the FishMMO project license.
