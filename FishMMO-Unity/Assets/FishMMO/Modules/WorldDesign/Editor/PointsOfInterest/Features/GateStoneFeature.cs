#if UNITY_EDITOR
using System;
using FishMMO.Shared.Core;
using FishNet.Object;
using UnityEditor;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// A Gate Stone at the site (issue #241): a <see cref="Waypoint"/> of kind <see cref="WaypointKind.GateStone"/>, the Gate
	/// spell's destination and its travellers' way home. Indexed by the site's unlock index like a waypoint, so a re-cut keeps
	/// every character's discovery of it.
	/// </summary>
	/// <remarks>
	/// An instance of the shipped <c>Waypoint.prefab</c> set to the Gate Stone kind and named after the site, with a
	/// deterministic SceneId. It takes the site's one unlock index, so a site has a waypoint or a Gate Stone, never both: a
	/// site that already has one skips this. Given by default to the old stone sites (stone circles, henges, ley nexuses,
	/// elf waystones, rune stones).
	/// </remarks>
	[Serializable]
	public class GateStoneFeature : PointOfInterestFeature, IPointOfInterestUnlockFeature
	{
		[Tooltip("Where the Gate Stone stands in the site's frame, metres (x right, y ahead).")]
		public Vector2 Offset = new Vector2(0.0f, 0.0f);

		public override void Build(PointOfInterestSiteContext context)
		{
			if (context.Root.GetComponentInChildren<Waypoint>(true) != null)
			{
				PointOfInterestFeatureKit.Note(context, "already has a waypoint or Gate Stone; the Gate Stone feature was skipped.");
				return;
			}
			int index = PointOfInterestFeatureKit.UnlockIndex(context, "Gate Stone");
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
			instance.name = PointOfInterestFeatureKit.UniqueName(context, "Gate Stone");
			instance.transform.SetParent(context.Root, false);
			instance.transform.SetPositionAndRotation(context.OnGround(Offset), Quaternion.Euler(0.0f, context.Record.Yaw, 0.0f));

			Waypoint waypoint = instance.GetComponent<Waypoint>();
			waypoint.WaypointName = PointOfInterestFeatureKit.SiteName(context);
			var serialized = new SerializedObject(waypoint);
			serialized.FindProperty("waypointIndex").intValue = index;
			serialized.FindProperty("kind").enumValueIndex = (int)WaypointKind.GateStone;
			serialized.ApplyModifiedPropertiesWithoutUndo();

			PointOfInterestFeatureKit.AssignSceneId(instance.GetComponent<NetworkObject>(), context, 0x6A7E);
		}
	}
}
#endif
