#if UNITY_EDITOR
using FishMMO.Shared.Biomes;
using UnityEditor;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	public sealed partial class CaveShaper
	{
		// ── Drowned galleries (2026-10-25, phase I2n-c, decision 86) ──

		/// <summary>How near water an opening of a root window may stand, metres: never in a lake, though the swamp's water is all round.</summary>
		private const float RootWindowWaterClear = 3f;

		/// <summary>
		/// Whether a cave site's host ground is wet (decision 86 (a), <see cref="BiomeCaveWaters.WetHost"/>): water within
		/// <see cref="BiomeCaveWaters.WetReach"/> of the site (rings at a third, two thirds and the whole of it), or a host whose habitat
		/// is a wetland's.
		/// </summary>
		private static bool WetHost(PointOfInterestPlanContext context, PointOfInterestRecord site, BiomeTemplate host)
		{
			bool near = false;
			for (int ring = 1; ring <= 3 && !near; ring++)
			{
				near = NearWater(context, site.Position.x, site.Position.z, BiomeCaveWaters.WetReach * ring / 3f);
			}
			return BiomeCaveWaters.WetHost(near, host != null ? host.Habitat : BiomeHabitat.None);
		}

		/// <summary>
		/// The flood depth a cave site is planned with (CaveSystem.Plan's <c>flood</c>): the cave biome's Cave Floods entry for the form
		/// (the interior's where the host links one, else the host's own) — a grotto's depth under its pool, or a natural cave's when its
		/// host's ground is wet and its seed's roll falls in the entry's share. NaN for as before (no entry, another form, a mouth not
		/// over the ground).
		/// </summary>
		private static float FloodOf(PointOfInterestPlanContext context, PointOfInterestRecord site, CaveForm form, CaveOpening opening, in CaveBiome planned)
		{
			if (!BiomeCaveWaters.Floods((BiomeCaveForm)form) || opening.Kind != CaveMouthKind.Ground
				|| !CaveInteriorArt.Flood(planned.Content, form, out BiomeCaveFlood flood))
			{
				return float.NaN;
			}
			if (form == CaveForm.Grotto)
			{
				return BiomeCaveWaters.DepthOf(in flood);
			}
			float roll = (PointOfInterestPlanner.Mix(unchecked((uint)SeedOf(site) ^ 0xD2A0F10Du)) >> 8) * (1f / 16777216f);
			bool drowns = BiomeCaveWaters.Drowns(in flood, WetHost(context, site, planned.Host), roll);
			return drowns ? BiomeCaveWaters.DepthOf(in flood) : float.NaN;
		}

		/// <summary>
		/// Opens a planned system's root windows (<see cref="CaveSystem.TryAddRootWindows"/>, decision 86 (f)) where the cave biome's
		/// Cave Hangings name any for the form and the host's land over it is wooded and wet: after its skylights, before a join, so a
		/// joined system brings them along; the plan's mouth loop holes and keeps them clear as it does a skylight.
		/// </summary>
		private static void TryRootWindows(PointOfInterestPlanContext context, PointOfInterestRecord site, CaveSystem system, CaveGround ground, in CaveBiome planned)
		{
			if (!CaveInteriorArt.Hanging(planned.Content, system.Form, out BiomeCaveHanging hanging) || hanging.RootWindows <= 0)
			{
				return;
			}
			if (!BiomeCaveWaters.OpensRootWindows(CaveHanging.Wooded(planned.Host), WetHost(context, site, planned.Host)))
			{
				return;
			}
			bool Refused(float x, float z) => NearWater(context, x, z, RootWindowWaterClear) || OnOtherSite(context, site.Id, x, z, SkylightSiteClear);
			int seed = unchecked(SeedOf(site) * 0x4F1B + 0x6C3D);
			int added = system.TryAddRootWindows(ground, site.Id, seed, hanging.RootWindows, Refused, out _);
			if (added > 0)
			{
				context.Notes?.Add($"{PointOfInterestKinds.Info(site.Kind).DisplayName} at ({site.Position.x:F0}, {site.Position.z:F0}): {added} root window(s) through its roof.");
			}
		}

		/// <summary>
		/// Gives every body of a cave's water (its pool, its streams and pools; never its lava) the look of the biome <paramref name="lookBiome"/>
		/// (SceneHydrology's LookBiome, decision 86 (b)). Nothing for 0: the bodies are written afresh on every build, by their shores.
		/// </summary>
		private static void StampWaterLook(PointOfInterestPlaceContext context, PointOfInterestRecord site, int lookBiome)
		{
			if (lookBiome == 0 || string.IsNullOrEmpty(context.TerrainFolder) || context.Request == null)
			{
				return;
			}
			var hydrology = AssetDatabase.LoadAssetAtPath<SceneHydrology>(SceneGenerator.HydrologyPath(context.TerrainFolder, context.Request.SceneName));
			if (hydrology == null)
			{
				return;
			}
			int pool = PoolIdBase | (site.Id & 0x00FFFFFF), stamped = 0;
			foreach (SceneHydrology.Lake lake in hydrology.Lakes)
			{
				if (lake != null && !lake.Lava && (lake.Id == pool || CaveStreamWater.OfSite(lake.Id, site.Id)))
				{
					lake.LookBiome = lookBiome;
					stamped++;
				}
			}
			foreach (SceneHydrology.River river in hydrology.Rivers)
			{
				if (river != null && !river.Lava && CaveStreamWater.OfSite(river.Id, site.Id))
				{
					river.LookBiome = lookBiome;
					stamped++;
				}
			}
			if (stamped > 0)
			{
				EditorUtility.SetDirty(hydrology);
				AssetDatabase.SaveAssetIfDirty(hydrology);
			}
		}
	}
}
#endif
