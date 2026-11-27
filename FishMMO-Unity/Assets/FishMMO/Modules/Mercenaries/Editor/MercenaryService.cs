#if UNITY_EDITOR
using System.Collections.Generic;
using FishMMO.Shared.WorldDesign;
using UnityEditor;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Puts Mercenaries in towns at the next re-cut: one of each kit wherever a site keeps a barracks, a guard house, a
	/// tavern or an inn, and in every capital, standing at that building (or on the market).
	/// </summary>
	[InitializeOnLoad]
	public static class MercenaryService
	{
		/// <summary>The service's name: its spawners are "&lt;site&gt; Mercenary Spawner".</summary>
		public const string ServiceName = "Mercenary";

		/// <summary>The buildings that keep Mercenaries, the first the most fitting.</summary>
		public static readonly string[] Buildings = { "Barracks", "GuardHouse", "Tavern", "Inn" };

		static MercenaryService()
		{
			PointOfInterestServiceNpcs.Register(new ServiceNpc
			{
				Name = ServiceName,
				// Every kit: each recruiter is its own NPC at the post.
				Prefabs = (context, race) => Recruiters(),
				Posts = new[] { NpcPostRule.AtBuilding(Buildings), NpcPostRule.AtMarket() },
				KeeperOf = Buildings,
				KeeperSites = PointOfInterestServiceNpcs.Keeps,
			});
		}

		/// <summary>The recruiter prefabs the builder made, one per kit, in kit order.</summary>
		public static IReadOnlyList<GameObject> Recruiters()
		{
			var prefabs = new List<GameObject>();
			foreach (MercenaryBuilder.KitSpec spec in MercenaryBuilder.Kits)
			{
				GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MercenaryBuilder.RecruiterPath(spec));
				if (prefab != null)
				{
					prefabs.Add(prefab);
				}
			}
			return prefabs;
		}
	}
}
#endif
