#if UNITY_EDITOR
using System.Collections.Generic;
using FishMMO.Shared.NameGeneration;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class PointOfInterestFeatureDefaults
	{
		/// <summary>
		/// The fantasy wave's features (2026-10-27): its lived-in places as settlements, its strongholds' waypoints and guards, its
		/// markets' merchants, its vaults' and tombs' dungeon entrances and the waygate's portal. A hostile kind's pack and elite
		/// come from its trait, after this; a great lair's boss after that (<see cref="FantasyBoss"/>).
		/// </summary>
		private static void Fantasy(POIType kind, int size, List<PointOfInterestFeature> features)
		{
			switch (kind)
			{
				case POIType.Hamlet:
				case POIType.LizardfolkVillage:
				case POIType.NomadCamp:
				case POIType.Kraal:
				case POIType.Crannog:
				case POIType.ReedVillage:
					AddSettlement(features, 0, extra: false);
					break;
				case POIType.TreeVillage:
				case POIType.MountainKingsHall:
				case POIType.SilverMineTown:
				case POIType.MerfolkVillage:
					AddSettlement(features, Mathf.Max(1, size), extra: false);
					break;
				case POIType.WaygateRing:
					features.Add(new PortalFeature());
					break;
				case POIType.DwarvenGate:
				case POIType.IdolVault:
				case POIType.RockCutFacade:
					features.Add(new DungeonEntranceFeature());
					break;
				case POIType.Necropolis:
				case POIType.Pyramid:
				case POIType.StepPyramid:
				case POIType.LostCity:
				case POIType.LichSpire:
				case POIType.IcePalace:
					if (size >= 2)
					{
						features.Add(new DungeonEntranceFeature());
					}
					break;
			}
			switch (kind)
			{
				case POIType.PassFort:
				case POIType.CliffCastle:
				case POIType.Commandery:
				case POIType.TollGate:
					features.Add(new WaypointFeature());
					features.Add(new NpcSpawnerFeature { Role = PointOfInterestNpcRole.Guard });
					break;
			}
			switch (kind)
			{
				case POIType.GoblinMarket:
				case POIType.CaravanHalt:
				case POIType.TollGate:
				case POIType.Grange:
					features.Add(new NpcSpawnerFeature { Role = PointOfInterestNpcRole.Merchant });
					break;
			}
		}

		/// <summary>The fantasy wave's great lairs: an empty boss spawner from medium size, after their pack (a designer assigns the boss).</summary>
		private static void FantasyBoss(POIType kind, int size, List<PointOfInterestFeature> features)
		{
			switch (kind)
			{
				case POIType.DragonLair:
				case POIType.SandwormPit:
				case POIType.FrostGiantSteading:
				case POIType.DragonInTheIce:
				case POIType.LichSpire:
				case POIType.HydraLair:
				case POIType.FireGiantHall:
				case POIType.KrakenGrounds:
				case POIType.LakeMonsterLair:
					if (size >= 1)
					{
						features.Add(new BossSpawnerFeature());
					}
					break;
			}
		}
	}
}
#endif
