#if UNITY_EDITOR
using System;
using FishNet.Object;
using UnityEditor;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// A <see cref="Waypoint"/> at the site, indexed by the site's unlock index so a re-cut keeps every character's
	/// discovery of it (Jim, 2026-10-10: settlements get a waypoint).
	/// </summary>
	/// <remarks>
	/// An instance of the shipped <c>Waypoint.prefab</c>, named after the site, given a deterministic SceneId
	/// (<see cref="PointOfInterestFeatureKit.AssignSceneId"/>). The world scene details cache harvests it on its next
	/// rebuild, which is what puts it on the map. One per site: a second waypoint feature on the same template is skipped.
	/// </remarks>
	[Serializable]
	public class WaypointFeature : PointOfInterestFeature, IPointOfInterestUnlockFeature
	{
		[Tooltip("Where the waypoint stands in the site's frame, metres (x right, y ahead).")]
		public Vector2 Offset = new Vector2(0.0f, -4.0f);

		public override void Build(PointOfInterestSiteContext context)
		{
			if (context.Root.GetComponentInChildren<Waypoint>(true) != null)
			{
				PointOfInterestFeatureKit.Note(context, "already has a waypoint; a second waypoint feature was skipped.");
				return;
			}
			int index = PointOfInterestFeatureKit.UnlockIndex(context, "waypoint");
			if (index < 0)
			{
				return;
			}
			GameObject prefab = PointOfInterestFeatureKit.Load<GameObject>(context, PointOfInterestFeatureKit.WaypointPrefabPath);
			if (prefab == null)
			{
				return;
			}

			var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, context.Scene);
			instance.name = PointOfInterestFeatureKit.UniqueName(context, "Waypoint");
			instance.transform.SetParent(context.Root, false);
			instance.transform.SetPositionAndRotation(context.OnGround(Offset), Quaternion.Euler(0.0f, context.Record.Yaw, 0.0f));

			Waypoint waypoint = instance.GetComponent<Waypoint>();
			waypoint.WaypointName = PointOfInterestFeatureKit.SiteName(context);
			var serialized = new SerializedObject(waypoint);
			serialized.FindProperty("waypointIndex").intValue = index;
			serialized.ApplyModifiedPropertiesWithoutUndo();

			PointOfInterestFeatureKit.AssignSceneId(instance.GetComponent<NetworkObject>(), context, 0x57A1);
		}
	}

	/// <summary>
	/// A respawn point at the site (<see cref="CharacterRespawnPosition"/>): where a character who dies nearby can come
	/// back. Its name carries the site id, because respawn points are keyed by name in the scene details cache.
	/// </summary>
	[Serializable]
	public class RespawnFeature : PointOfInterestFeature
	{
		[Tooltip("Where the respawn point stands in the site's frame, metres.")]
		public Vector2 Offset = new Vector2(3.0f, -6.0f);

		public override void Build(PointOfInterestSiteContext context)
		{
			GameObject point = PointOfInterestFeatureKit.AddChildAt(context, PointOfInterestFeatureKit.UniqueName(context, "Respawn"), Offset, 180.0f, 0.1f);
			point.AddComponent<CharacterRespawnPosition>();
		}
	}
}
#endif
