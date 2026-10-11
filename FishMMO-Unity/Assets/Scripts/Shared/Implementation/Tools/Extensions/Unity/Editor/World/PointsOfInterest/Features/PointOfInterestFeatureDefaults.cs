#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using FishMMO.Shared.NameGeneration;
using UnityEditor;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// The gameplay features recommended for each kind of site, for whoever authors the templates to start from.
	/// </summary>
	/// <remarks>
	/// <para>Fresh feature objects every call (a template owns its list). Order matters: the region first so the features
	/// that ride it find it, spawners before the boss so the boss can wait on them.</para>
	/// <list type="bullet">
	/// <item><description>Every site: a name-toast <see cref="RegionFeature"/> and an <see cref="ExplorationFeature"/>
	/// (EVERY POI requires discovery, Jim 2026-10-10).</description></item>
	/// <item><description>Settlements: <see cref="WaypointFeature"/>, <see cref="RespawnFeature"/>, merchant; towns and up add
	/// a banker, a crafter and guards; townsfolk scale with size; a capital gets a second merchant and more guards and
	/// townsfolk than a city (Jim's default: capitals get a larger layout and more service NPCs).</description></item>
	/// <item><description>Strongholds and waystations: a waypoint (and a respawn at a waystation), guards or a merchant.</description></item>
	/// <item><description>Hostile sites: a monster pack, an elite from medium size. A world boss lair: a pack and an empty
	/// <see cref="BossSpawnerFeature"/>.</description></item>
	/// <item><description>Portals: <see cref="PortalFeature"/>. Dungeon entrances, and large caves: <see cref="DungeonEntranceFeature"/>.</description></item>
	/// <item><description>Resource sites (ore veins, crystal formations, herb groves, ancient trees, mines, quarries, lumber
	/// camps, obsidian fields, fallen stars, witches' huts): <see cref="GatheringFeature"/>.</description></item>
	/// </list>
	/// </remarks>
	public static class PointOfInterestFeatureDefaults
	{
		/// <summary>The recommended features for a kind at a size class (0 small, 1 medium, 2 large).</summary>
		public static List<PointOfInterestFeature> For(POIType kind, int sizeClass)
		{
			int size = Mathf.Clamp(sizeClass, 0, 2);
			PointOfInterestKindInfo info = PointOfInterestKinds.Info(kind);
			var features = new List<PointOfInterestFeature>
			{
				new RegionFeature(),
				new ExplorationFeature(),
			};

			switch (kind)
			{
				case POIType.Capital:
					AddSettlement(features, 2, extra: true);
					break;
				case POIType.City:
					AddSettlement(features, 2, extra: false);
					break;
				case POIType.Town:
				case POIType.Port:
					AddSettlement(features, Math.Max(1, size), extra: false);
					break;
				case POIType.Village:
				case POIType.StiltVillage:
				case POIType.Monastery:
				case POIType.TradingPost:
					AddSettlement(features, 0, extra: false);
					break;
				case POIType.Keep:
				case POIType.Castle:
				case POIType.Fortress:
					features.Add(new WaypointFeature());
					features.Add(new NpcSpawnerFeature { Role = PointOfInterestNpcRole.Guard });
					features.Add(new NpcSpawnerFeature { Role = PointOfInterestNpcRole.Merchant });
					break;
				case POIType.Waystation:
					features.Add(new WaypointFeature());
					features.Add(new RespawnFeature());
					features.Add(new NpcSpawnerFeature { Role = PointOfInterestNpcRole.Merchant });
					break;
				case POIType.BossLair:
					features.Add(new NpcSpawnerFeature { Role = PointOfInterestNpcRole.MonsterPack });
					features.Add(new BossSpawnerFeature());
					break;
				case POIType.Portal:
					features.Add(new PortalFeature());
					break;
				case POIType.DungeonEntrance:
					features.Add(new DungeonEntranceFeature());
					break;
				case POIType.Cave:
				case POIType.IceCave:
				case POIType.LavaTube:
					if (size >= 2)
					{
						// Jim, 2026-10-10: a large cave ends in a dungeon entrance; a small one is a walk-in grotto.
						features.Add(new DungeonEntranceFeature());
					}
					break;
			}

			if (info.Has(PointOfInterestTraits.Hostile) && kind != POIType.BossLair)
			{
				features.Add(new NpcSpawnerFeature { Role = PointOfInterestNpcRole.MonsterPack });
				if (size >= 1)
				{
					features.Add(new NpcSpawnerFeature { Role = PointOfInterestNpcRole.Elite });
				}
			}

			if (GatheringResourceCatalogue.FamiliesFor(kind).Length > 0 && kind != POIType.Cave && kind != POIType.Grotto)
			{
				features.Add(new GatheringFeature());
			}
			return features;
		}

		private static void AddSettlement(List<PointOfInterestFeature> features, int tier, bool extra)
		{
			features.Add(new WaypointFeature());
			features.Add(new RespawnFeature());
			features.Add(new NpcSpawnerFeature { Role = PointOfInterestNpcRole.Merchant });
			if (extra)
			{
				features.Add(new NpcSpawnerFeature { Role = PointOfInterestNpcRole.Merchant });
			}
			if (tier >= 1)
			{
				features.Add(new NpcSpawnerFeature { Role = PointOfInterestNpcRole.Banker });
				features.Add(new NpcSpawnerFeature { Role = PointOfInterestNpcRole.Crafter });
				features.Add(new NpcSpawnerFeature { Role = PointOfInterestNpcRole.Guard, Count = extra ? 6 : 0 });
			}
			features.Add(new NpcSpawnerFeature { Role = PointOfInterestNpcRole.Townsfolk, Count = extra ? 10 : 0 });
		}

		/// <summary>Whether a list of features needs the site's unlock index.</summary>
		public static bool NeedsUnlockIndex(IEnumerable<PointOfInterestFeature> features)
		{
			if (features == null)
			{
				return false;
			}
			foreach (PointOfInterestFeature feature in features)
			{
				if (feature is IPointOfInterestUnlockFeature)
				{
					return true;
				}
			}
			return false;
		}
	}

	/// <summary>
	/// Tells the generator which sites take an unlock index (<see cref="PointOfInterestUnlocks.Unlockable"/>): those whose
	/// template carries a waypoint, a portal or an exploration feature.
	/// </summary>
	/// <remarks>
	/// The planner names the chosen template on the record before it assigns indices, so the template is found by its kind
	/// and asset name. Lookups are cached and the cache is dropped whenever the project changes.
	/// </remarks>
	[InitializeOnLoad]
	public static class PointOfInterestGameplayUnlocks
	{
		private static readonly Dictionary<(POIType, string), bool> cache = new Dictionary<(POIType, string), bool>();

		static PointOfInterestGameplayUnlocks()
		{
			PointOfInterestUnlocks.Unlockable = IsUnlockable;
			EditorApplication.projectChanged += cache.Clear;
		}

		/// <summary>Whether a site's template has a feature that needs its unlock index.</summary>
		public static bool IsUnlockable(PointOfInterestRecord record)
		{
			if (record == null || string.IsNullOrEmpty(record.Template))
			{
				return false;
			}
			var key = (record.Kind, record.Template);
			if (!cache.TryGetValue(key, out bool unlockable))
			{
				unlockable = PointOfInterestFeatureDefaults.NeedsUnlockIndex(FindTemplate(record.Kind, record.Template)?.Features);
				cache[key] = unlockable;
			}
			return unlockable;
		}

		private static PointOfInterestTemplate FindTemplate(POIType kind, string name)
		{
			string[] guids = AssetDatabase.FindAssets($"t:{nameof(PointOfInterestTemplate)}");
			Array.Sort(guids, StringComparer.Ordinal);
			foreach (string guid in guids)
			{
				var template = AssetDatabase.LoadAssetAtPath<PointOfInterestTemplate>(AssetDatabase.GUIDToAssetPath(guid));
				if (template != null && template.Kind == kind && template.name == name)
				{
					return template;
				}
			}
			return null;
		}
	}
}
#endif
