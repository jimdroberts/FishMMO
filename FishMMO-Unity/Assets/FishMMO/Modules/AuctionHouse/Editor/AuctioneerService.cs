#if UNITY_EDITOR
using FishMMO.Shared.WorldDesign;
using UnityEditor;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// The Auctioneer as one of the towns' service NPCs (issue #299; <see cref="PointOfInterestServiceNpcs"/>): every town
	/// or stronghold (<see cref="PointOfInterestServiceNpcs.Keeps"/>) that lays a Market Hall or a Counting House keeps one,
	/// standing at it (the market hall first, else beside the Banker at the counting house), and every capital
	/// keeps one, on the market when it laid neither. Every Auctioneer opens the same, world-wide market.
	/// </summary>
	/// <remarks>
	/// Registered by this module's editor assembly, so without the Auction House module nobody stands one. The Auctioneer takes
	/// the site's race, as the other service NPCs do. Sites already cut get theirs at their next re-cut. The prefab is made by
	/// <see cref="AuctioneerBuilder"/>; until it exists the service places nothing.
	/// </remarks>
	[InitializeOnLoad]
	public static class AuctioneerService
	{
		/// <summary>The Auctioneer's name among the services.</summary>
		public const string ServiceName = "Auctioneer";

		/// <summary>The buildings that bring an Auctioneer, best first.</summary>
		public static readonly string[] Buildings = { "MarketHall", "CountingHouse" };

		static AuctioneerService()
		{
			PointOfInterestServiceNpcs.Register(new ServiceNpc
			{
				Name = ServiceName,
				Prefabs = PointOfInterestServiceNpcs.Prefab(() => AssetDatabase.LoadAssetAtPath<GameObject>(AuctioneerBuilder.AuctioneerPrefabPath)),
				Posts = new[] { NpcPostRule.AtBuilding(Buildings[0]), NpcPostRule.AtBuilding(Buildings[1]), NpcPostRule.AtMarket() },
				KeeperOf = Buildings,
				// The towns the core services keep to (settlements and strongholds), not every friendly site with a counting house.
				KeeperSites = PointOfInterestServiceNpcs.Keeps,
			});
		}
	}
}
#endif
