#if UNITY_EDITOR
using UnityEditor;
using UnityEditorInternal;
using System.IO;
using System.Collections.Generic;
using System;
using FishMMO.Logging;
using FishMMO.Server.Implementation.World.SceneServer.AI;
using UnityEngine;

using FishMMO.Shared.CustomBuildTool;
using FishMMO.Shared.CustomBuildTool.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Generates the procedural biome art — payload (textures, meshes), wrappers (terrain layers,
	/// materials, prefabs) and the terrain layers built from loose biome textures, all gitignored
	/// and rebuilt on every machine — when any of it is missing or was made by other generator code,
	/// for client AND server builds alike, before anything else is baked or built.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Committed scenes, terrain data and biomes reference generated art by path-derived GUIDs and
	/// deterministic object IDs (<see cref="FishMMO.Shared.WorldDesign.ProceduralArtPayload"/>), so
	/// a clone that never opened the editor would otherwise build trees with no prefabs and ground
	/// with no layers. Servers need it too: their terrain keeps its tree prototypes for the
	/// colliders, and those prefabs pull in their meshes and materials (phase two would swap them
	/// for collider-only proxies). Order: this, then the terrain arrays (which read the ground
	/// textures), then the world maps (which photograph the arrays).
	/// </para>
	/// <para>
	/// Generated art left from before deterministic IDs is migrated first (references rewritten,
	/// backed up under Library/FishMMO). Anything generated that is still missing or mis-addressed
	/// afterwards stops the build, because a player with invisible trees is not worth shipping.
	/// </para>
	/// </remarks>
	/// <exception cref="InvalidOperationException">The generated art could not be brought up to date.</exception>
	internal sealed class BiomeArtBuildStep : BuildStep
	{
		public override string Name => "Generate procedural biome art";
		public override BuildStepPhase Phase => BuildStepPhase.PrepareContent;
		public override int Order => 100;

		public override void Run(BuildStepContext context)
		{
			Log.Debug("BuildLogger", "Checking procedural biome art...");
			var log = new List<string>();
			try
			{
				FishMMO.Shared.WorldDesign.BiomeArtGenerator.EnsureCurrentForBuild(log);
			}
			finally
			{
				foreach (string line in log)
				{
					Log.Debug("BuildLogger", line);
				}
			}
		
		}
	}

	/// <summary>
	/// Bakes every body's climate table before the bundles, so neither build solves one at load
	/// (<see cref="FishMMO.Shared.WorldDesign.ClimateTableBaker"/>). Both builds: the weather runs on both.
	/// </summary>
	internal sealed class ClimateTableBuildStep : BuildStep
	{
		public override string Name => "Bake climate tables";
		public override BuildStepPhase Phase => BuildStepPhase.PrepareContent;
		// After the biome art, before the spawn tables (the order the build always used).
		public override int Order => 150;

		public override void Run(BuildStepContext context)
		{
			// Every body's climate table, so neither build solves one at load (ClimateTableBaker). Both builds: the weather runs on both.
			int climates = FishMMO.Shared.WorldDesign.ClimateTableBaker.BakeAll(out int currentClimates);
			Log.Debug("BuildLogger", $"Climate tables: {climates} baked, {currentClimates} already current.");
		}
	}

	/// <summary>
	/// Bakes the generated scenes' terrain arrays and the planet surfaces before a client build's world maps photograph them
	/// (the core's WorldMapBuildStep runs next, Order 300). Their folders are per-machine caches and stay after the build;
	/// only their addressable groups are removed again (WorldDesignContentCleanupStep). Server builds skip it. A bake that
	/// fails is logged and does not stop the build.
	/// </summary>
	internal sealed class WorldDesignContentBuildStep : BuildStep
	{
		public override string Name => "Bake terrain arrays and planet surfaces";
		public override BuildStepPhase Phase => BuildStepPhase.PrepareContent;
		public override int Order => 290;

		public override void Run(BuildStepContext context)
		{
			if (context.BuildType != CustomBuildType.Client)
			{
				Log.Debug("BuildLogger", "Server build: terrain arrays and planet surfaces are not baked.");
				return;
			}

			/* Terrain arrays first: the world maps photograph the ground, and must see the array-textured terrain rather
			 * than grey. Their own try, so a failed array bake never costs the build its maps. */
			FishMMO.Shared.WorldDesign.TerrainArrayAutoBake.BuildRunning = true;
			try
			{
				Log.Debug("BuildLogger", "Baking terrain arrays...");
				var arrayLog = new List<string>();
				int arrays = FishMMO.Shared.WorldDesign.TerrainArrayBaker.BakeForBuild(arrayLog);
				foreach (string line in arrayLog)
				{
					Log.Debug("BuildLogger", line);
				}
				Log.Debug("BuildLogger", $"Baked {arrays} scene(s) of terrain arrays.");
			}
			catch (Exception ex)
			{
				Log.Warning("BuildLogger", $"Terrain array bake failed; generated ground draws plain grey in this build: {ex.Message}");
			}

			try
			{
				Log.Debug("BuildLogger", "Baking planet surfaces...");
				FishMMO.Shared.WorldDesign.PlanetSurfaceBaker.BakeForBuild();
			}
			catch (OperationCanceledException) { throw; }
			catch (Exception ex)
			{
				Log.Warning("BuildLogger", $"Planet surface bake failed; the build continues without those images: {ex.Message}");
			}
		}
	}

	/// <summary>
	/// Takes generated biome art out of the committed groups (<see cref="FishMMO.Shared.WorldDesign.ProceduralArtBuildGroup.RemoveCommittedEntries"/>).
	/// It is not registered as entries of its own any more: it travels as implicit dependencies, inside the one bundle
	/// that uses it or, when several do, isolated by the set of bundles that use it
	/// (<see cref="FishMMO.Shared.CustomBuildTool.Addressables.SharedDependencyBuildGroup"/>). Registered, all of it
	/// went into one PackTogether bundle (about 2 GB), so a single reference loaded every biome's art.
	/// </summary>
	internal sealed class GeneratedArtRegistrationStep : BuildStep
	{
		public override string Name => "Remove generated biome art from the committed groups";
		public override BuildStepPhase Phase => BuildStepPhase.RegisterContent;
		public override int Order => 100;

		public override void Run(BuildStepContext context)
		{
			Log.Debug("BuildLogger", "Removing generated biome art from the committed groups...");
			var log = new List<string>();
			FishMMO.Shared.WorldDesign.ProceduralArtBuildGroup.RemoveCommittedEntries(log);
			// A group an interrupted earlier build left behind would still pack everything together.
			FishMMO.Shared.WorldDesign.ProceduralArtBuildGroup.RemoveGroup();
			foreach (string line in log)
			{
				Log.Debug("BuildLogger", line);
			}
		
		}
	}

	/// <summary>Removes the generated art's build-time group, leaving the project as it was found.</summary>
	internal sealed class GeneratedArtCleanupStep : BuildStep
	{
		public override string Name => "Remove the generated art build group";
		public override BuildStepPhase Phase => BuildStepPhase.Cleanup;
		public override int Order => 100;

		public override void Run(BuildStepContext context)
		{
			try
			{
				FishMMO.Shared.WorldDesign.ProceduralArtBuildGroup.RemoveGroup();
			}
			catch (Exception ex)
			{
				Log.Warning("BuildLogger", $"Removing the '{FishMMO.Shared.WorldDesign.ProceduralArtBuildGroup.GroupName}' addressable group failed; remove it by hand in the Addressables Groups window: {ex.Message}");
			}
		
		}
	}

	/// <summary>
	/// Removes the world maps' addressable group after a client build and rebuilds the world scene
	/// details cache without references to them, so the project is left as it was found. The baked
	/// maps themselves stay on disk for the next build.
	/// </summary>
	/// <summary>Removes the terrain arrays' and planet surfaces' addressable groups after a client build (the baked caches stay).</summary>
	internal sealed class WorldDesignContentCleanupStep : BuildStep
	{
		public override string Name => "Remove the planet surface and terrain array build groups";
		public override BuildStepPhase Phase => BuildStepPhase.Cleanup;
		public override int Order => 190;

		public override void Run(BuildStepContext context)
		{
			if (context.BuildType != CustomBuildType.Client)
			{
				return;
			}

			try
			{
				Log.Debug("BuildLogger", "Removing the terrain arrays' addressable group (the baked arrays stay as the editor's cache)...");
				FishMMO.Shared.WorldDesign.TerrainArrayBaker.RemoveBuildGroup();
			}
			catch (Exception ex)
			{
				Log.Warning("BuildLogger", $"Removing the '{TerrainArraySet.AddressableGroupName}' addressable group failed; remove it by hand in the Addressables Groups window: {ex.Message}");
			}
			finally
			{
				FishMMO.Shared.WorldDesign.TerrainArrayAutoBake.BuildRunning = false;
			}

			try
			{
				Log.Debug("BuildLogger", "Removing the planet surfaces' addressable group (the baked images stay for the next build)...");
				FishMMO.Shared.WorldDesign.PlanetSurfaceBaker.RemoveBuildGroup();
			}
			catch (Exception ex)
			{
				Log.Warning("BuildLogger", $"Removing the '{FishMMO.Shared.WorldDesign.PlanetSurfaceBaker.AddressableGroupName}' addressable group failed; remove it by hand in the Addressables Groups window: {ex.Message}");
			}
		}
	}
}
#endif
