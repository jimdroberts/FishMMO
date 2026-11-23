#if UNITY_EDITOR
using System;
using FishMMO.Logging;
using FishMMO.Shared.CustomBuildTool;
using FishMMO.Shared.CustomBuildTool.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Core (split from the World Design module's terrain array and planet surface bake). Bakes the portal
	/// views and a map definition and image for each world scene that has none or has changed since, and rebuilds the
	/// world scene details cache to reference them, before the addressables of a client build are
	/// built. The maps are a gitignored per-machine cache (see
	/// <see cref="FishMMO.Shared.WorldMaps.WorldMapBaker"/>) and stay for the next build;
	/// <see cref="CleanWorldMaps"/> removes only their addressable group and the cache's references
	/// when the build has finished. Server builds never draw a map, so they
	/// skip both. A bake that fails is logged and does not stop the build, because the world map
	/// falls back to a plain background without its image.
	/// </summary>
	internal sealed class WorldMapBuildStep : BuildStep
	{
		public override string Name => "Bake portal views and world maps";
		public override BuildStepPhase Phase => BuildStepPhase.PrepareContent;
		public override int Order => 300;

		public override void Run(BuildStepContext context)
		{
			CustomBuildType customBuildType = context.BuildType;
			if (customBuildType != CustomBuildType.Client)
			{
				Log.Debug("BuildLogger", "Server build: world maps are not baked.");
				return;
			}

			// A module's ground and planet bakes run first (the World Design module's terrain arrays and planet surfaces, Order 290).
			try
			{
				Log.Debug("BuildLogger", "Baking any missing or out of date portal views (before the maps: putting in a missing portal surface saves its scene)...");
				PortalViewBaker.BakeOutOfDate();
			}
			catch (OperationCanceledException) { throw; }
			catch (Exception ex)
			{
				Log.Warning("BuildLogger", $"Portal view bake failed; portals show only their glow where no view was baked: {ex.Message}");
			}

			WorldMapDefinition.ReferenceBakedMaps = true;
			try
			{
				Log.Debug("BuildLogger", "Baking any missing or out of date world maps...");
				FishMMO.Shared.WorldMaps.WorldMapBaker.BakeOutOfDate();
			}
			catch (OperationCanceledException) { throw; }
			catch (Exception ex)
			{
				Log.Warning("BuildLogger", $"World map bake failed; the build continues with the maps already baked: {ex.Message}");
			}

			/* Whatever the bakes did: this is what points every scene at its current definition, so the maps
			 * kept from earlier builds, and any baked again with new asset IDs, reach the client. */
			try
			{
				Log.Debug("BuildLogger", "Rebuilding the world scene details cache with the baked maps...");
				WorldSceneDetailsCacheBuilder.Rebuild();
			}
			catch (OperationCanceledException) { throw; }
			catch (Exception ex)
			{
				Log.Warning("BuildLogger", $"Rebuilding the world scene details cache failed; this build's world maps draw a plain background: {ex.Message}");
			}
		
		}
	}

	internal sealed class WorldMapCleanupStep : BuildStep
	{
		public override string Name => "Remove the world map and portal view build groups";
		public override BuildStepPhase Phase => BuildStepPhase.Cleanup;
		public override int Order => 200;

		public override void Run(BuildStepContext context)
		{
			CustomBuildType customBuildType = context.BuildType;
			if (customBuildType != CustomBuildType.Client)
			{
				return;
			}

			WorldMapDefinition.ReferenceBakedMaps = false;
			try
			{
				Log.Debug("BuildLogger", "Removing the portal views' addressable group (the baked views stay for the next build)...");
				PortalViewBaker.RemoveBuildGroup();

				Log.Debug("BuildLogger", "Removing the world maps' addressable group (the baked maps stay for the next build)...");
				FishMMO.Shared.WorldMaps.WorldMapBaker.RemoveBuildGroup();
				WorldSceneDetailsCacheBuilder.Rebuild();
			}
			catch (Exception ex)
			{
				Log.Warning("BuildLogger", $"Removing the world map or portal view addressable group failed; remove the '{FishMMO.Shared.WorldMaps.WorldMapBaker.AddressableGroupName}' group in the Addressables Groups window and run FishMMO Dashboard → World → World Scene Details → Rebuild World Scene Details by hand: {ex.Message}");
			}
		
		}
	}
}
#endif
