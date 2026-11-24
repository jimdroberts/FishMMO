#if UNITY_EDITOR
using FishMMO.Shared.WorldDesign;
using UnityEditor;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// The Stablemaster as one of the towns' service NPCs (issue #109; <see cref="PointOfInterestServiceNpcs"/>): every site that
	/// lays a Stables building and is not hostile (towns, villages, farmsteads, castles) keeps one, standing on the street before
	/// its stable door, selling the mounts' saddles, the Mount premade and the Dismount effect (<see cref="MountContentBuilder"/>).
	/// Every capital keeps one too, at its stables, or on a drawn spot when it laid none.
	/// </summary>
	/// <remarks>
	/// Registered by this module's editor assembly, so without the Mounts module nobody sells mounts. The Stablemaster takes the
	/// site's race (its model, faction and name), as the other service NPCs do. Sites already cut get one at their next re-cut.
	/// </remarks>
	[InitializeOnLoad]
	public static class StablemasterService
	{
		/// <summary>The Stablemaster's name among the services.</summary>
		public const string ServiceName = "Stablemaster";

		/// <summary>The structure kit's piece the stables are.</summary>
		public const string StablesPiece = "Stables";

		static StablemasterService()
		{
			PointOfInterestServiceNpcs.Register(new ServiceNpc
			{
				Name = ServiceName,
				Prefabs = PointOfInterestServiceNpcs.Prefab(() => AssetDatabase.LoadAssetAtPath<GameObject>(MountContentBuilder.StablemasterPrefabPath)),
				Posts = new[] { NpcPostRule.AtBuilding(StablesPiece) },
				KeeperOf = new[] { StablesPiece },
				// Any site with stables that is not hostile, as before: a farmstead's or a castle's too.
				KeeperSites = record => record != null && !record.Info.Has(PointOfInterestTraits.Hostile),
			});
		}
	}
}
#endif
