#if UNITY_EDITOR
using System.Collections.Generic;
using FishMMO.Server.Implementation.World.SceneServer.Spawner;
using FishMMO.Shared.WorldDesign;
using UnityEditor;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Stands a Stablemaster at a site's stables (issue #109): every site that lays a Stables building (towns, villages,
	/// farmsteads, castles) gets one spawner on the street outside its first stable door, selling the mounts' saddles, the
	/// Mount premade and the Dismount effect (<see cref="MountContentBuilder"/>).
	/// </summary>
	/// <remarks>
	/// <para>
	/// A site builder, not a template feature, so no point-of-interest template changes: it reads what the site laid (the
	/// stables' doors, <see cref="PointOfInterestSiteContext.Doors"/>), and a site with no stables, or a hostile one, gets
	/// none. Registered by this module's editor assembly, so without the Mounts module nobody sells mounts.
	/// </para>
	/// <para>
	/// The Stablemaster takes the site's race (its model, faction and name), as the other service NPCs do. Sites already cut
	/// get one at their next re-cut (or a "Regenerate POIs" repaint).
	/// </para>
	/// </remarks>
	[InitializeOnLoad]
	public sealed class StablemasterSiteBuilder : IPointOfInterestSiteBuilder
	{
		static StablemasterSiteBuilder() => PointOfInterestSiteBuilders.Register(new StablemasterSiteBuilder());

		/// <summary>The structure kit's piece the stables are.</summary>
		public const string StablesPiece = "Stables";

		/// <summary>How far out in front of the stable door the Stablemaster stands, metres.</summary>
		public const float StandOff = 2.5f;

		public bool Handles(PointOfInterestRecord record) => record != null && !record.Info.Has(PointOfInterestTraits.Hostile);

		public void Build(PointOfInterestSiteContext context)
		{
			if (!TryFindStableDoor(context.Doors, out PointOfInterestSiteContext.PlacedDoor door))
			{
				return;
			}
			GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MountContentBuilder.StablemasterPrefabPath);
			if (prefab == null)
			{
				PointOfInterestFeatureKit.Note(context, "has stables but no Stablemaster: run Build mounts (Dashboard > Mounts).");
				return;
			}
			Vector3 at = StandingSpot(door);
			Vector2 local = ToSiteLocal(context.Record.Position, context.Record.Yaw, at);
			RaceTemplate race = PointOfInterestFeatureKit.SiteRace(context);
			var choices = new List<PointOfInterestNpcChoice> { new PointOfInterestNpcChoice { Prefab = prefab, Weight = 1f } };
			List<SpawnableSettings> spawnables = PointOfInterestFeatureKit.NpcSpawnables(choices, race);
			ObjectSpawner spawner = PointOfInterestFeatureKit.AddSpawner(context,
				PointOfInterestFeatureKit.UniqueName(context, "Stablemaster Spawner"), local, spawnables, 1, 0f);
			// Facing the street, its back to the stable door.
			Vector3 outward = -door.Inward;
			outward.y = 0f;
			if (outward.sqrMagnitude > 1e-4f)
			{
				spawner.transform.rotation = Quaternion.LookRotation(outward.normalized, Vector3.up);
			}
			EditorUtility.SetDirty(spawner);
		}

		/// <summary>The first door of a Stables building, if the site laid one.</summary>
		public static bool TryFindStableDoor(IReadOnlyList<PointOfInterestSiteContext.PlacedDoor> doors, out PointOfInterestSiteContext.PlacedDoor door)
		{
			door = default;
			if (doors == null)
			{
				return false;
			}
			for (int i = 0; i < doors.Count; i++)
			{
				if (doors[i].Building == StablesPiece)
				{
					door = doors[i];
					return true;
				}
			}
			return false;
		}

		/// <summary>The spot <see cref="StandOff"/> metres out from the middle of the doorway, on its sill.</summary>
		public static Vector3 StandingSpot(PointOfInterestSiteContext.PlacedDoor door)
		{
			Vector3 inward = door.Inward;
			inward.y = 0f;
			inward = inward.sqrMagnitude > 1e-4f ? inward.normalized : Vector3.forward;
			Vector3 across = door.Across;
			across.y = 0f;
			across = across.sqrMagnitude > 1e-4f ? across.normalized : Vector3.right;
			return door.Hinge + across * (door.Width * 0.5f) - inward * StandOff;
		}

		/// <summary>A world point in a site's own frame (x right, z ahead along its heading): the inverse of OnGround.</summary>
		public static Vector2 ToSiteLocal(Vector3 sitePosition, float siteYaw, Vector3 world)
		{
			Vector3 offset = Quaternion.Inverse(Quaternion.Euler(0f, siteYaw, 0f)) * (world - sitePosition);
			return new Vector2(offset.x, offset.z);
		}
	}
}
#endif
