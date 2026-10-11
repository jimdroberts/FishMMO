#if UNITY_EDITOR
using System;
using FishMMO.Shared.Core;
using FishNet.Object;
using UnityEditor;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// A dormant portal at the site: a same-scene <see cref="Teleporter"/> or a cross-scene <see cref="SceneTeleporter"/>,
	/// with a <see cref="PortalActivation"/> whose scope is a seeded draw (Jim, 2026-10-10: both kinds; destinations empty
	/// and set by hand; inactive until activated; any-of conditions; the key designer-assigned and left empty; scope per
	/// portal, a seeded mix when generated).
	/// </summary>
	/// <remarks>
	/// <para>The portal's index is the site's unlock index, so a re-cut keeps every character's activation. With no
	/// conditions and no key it opens for the first player who uses it — a designer adds the requirements.</para>
	/// <para>A <see cref="Teleporter"/> is a networked interactable (NetworkObject, collider, the shipped "Teleporter
	/// Interact" trigger) with an empty <c>Target</c>; a <see cref="SceneTeleporter"/> is a trigger volume with an empty
	/// <c>DestinationID</c>, keyed by its GameObject name, which is why the name carries the site id. The frame, if the
	/// structure kit has a "portal" piece for the site's style, is laid as a prop.</para>
	/// </remarks>
	[Serializable]
	public class PortalFeature : PointOfInterestFeature, IPointOfInterestUnlockFeature
	{
		[Tooltip("Chance the portal leads to another scene (a SceneTeleporter) rather than within this one (a Teleporter).")]
		[Range(0.0f, 1.0f)]
		public float CrossSceneChance = 0.5f;

		[Header("Scope mix (relative weights)")]
		[Min(0.0f)] public float PerCharacterWeight = 0.5f;
		[Min(0.0f)] public float WorldTimedWeight = 0.3f;
		[Min(0.0f)] public float WorldPermanentWeight = 0.2f;

		[Tooltip("Range a world-timed opening is drawn from, minutes; rounded to quarter hours.")]
		public Vector2 TimedMinutes = new Vector2(30.0f, 240.0f);

		[Tooltip("Where the portal stands in the site's frame, metres.")]
		public Vector2 Offset = Vector2.zero;

		/// <summary>A seeded scope draw from the weights.</summary>
		public static PortalActivationScope DrawScope(float perCharacter, float worldTimed, float worldPermanent, float roll01)
		{
			float a = Mathf.Max(0.0f, perCharacter), b = Mathf.Max(0.0f, worldTimed), c = Mathf.Max(0.0f, worldPermanent);
			float total = a + b + c;
			if (total <= 0.0f)
			{
				return PortalActivationScope.PerCharacter;
			}
			float roll = Mathf.Clamp01(roll01) * total;
			if (roll < a)
			{
				return PortalActivationScope.PerCharacter;
			}
			return roll < a + b ? PortalActivationScope.WorldTimed : PortalActivationScope.WorldPermanent;
		}

		public override void Build(PointOfInterestSiteContext context)
		{
			if (context.Root.GetComponentInChildren<PortalActivation>(true) != null)
			{
				PointOfInterestFeatureKit.Note(context, "already has a portal; a second portal feature was skipped (one unlock index per site).");
				return;
			}
			int index = PointOfInterestFeatureKit.UnlockIndex(context, "portal");
			if (index < 0)
			{
				return;
			}

			// Every draw in a fixed order, so changing a weight never shifts another site's draws.
			bool crossScene = context.Random.NextFloat() < CrossSceneChance;
			PortalActivationScope scope = DrawScope(PerCharacterWeight, WorldTimedWeight, WorldPermanentWeight, context.Random.NextFloat());
			float minutes = Mathf.Lerp(Mathf.Min(TimedMinutes.x, TimedMinutes.y), Mathf.Max(TimedMinutes.x, TimedMinutes.y), context.Random.NextFloat());
			minutes = Mathf.Max(15.0f, Mathf.Round(minutes / 15.0f) * 15.0f);

			GameObject portal = PointOfInterestFeatureKit.AddChildAt(context, PointOfInterestFeatureKit.UniqueName(context, "Portal"), Offset);
			if (crossScene)
			{
				BoxCollider volume = portal.AddComponent<BoxCollider>();
				volume.isTrigger = true;
				volume.size = new Vector3(3.0f, 4.0f, 1.5f);
				volume.center = new Vector3(0.0f, 2.0f, 0.0f);
				portal.AddComponent<SceneTeleporter>();
			}
			else
			{
				NetworkObject networkObject = PointOfInterestFeatureKit.AddNetworkObject(portal);
				CapsuleCollider body = portal.AddComponent<CapsuleCollider>();
				body.radius = 1.0f;
				body.height = 4.0f;
				body.center = new Vector3(0.0f, 2.0f, 0.0f);
				Teleporter teleporter = portal.AddComponent<Teleporter>();
				PointOfInterestFeatureKit.SetInteractTriggers(teleporter, PointOfInterestFeatureKit.Load<Trigger>(context, PointOfInterestFeatureKit.TeleporterTriggerPath));
				PointOfInterestFeatureKit.AssignSceneId(networkObject, context, 0x7031);
				EditorUtility.SetDirty(teleporter);
			}

			PortalActivation activation = portal.AddComponent<PortalActivation>();
			activation.Configure(index, scope, minutes * 60.0f);
			EditorUtility.SetDirty(activation);

			GameObject frame = context.ResolvePiece("portal", context.Record.SiteSeed);
			if (frame != null)
			{
				context.AddProp(frame, portal.transform.position, portal.transform.rotation, Vector3.one);
			}
			PointOfInterestFeatureKit.Note(context, $"portal {index}: {(crossScene ? "cross-scene" : "same-scene")}, {scope}" +
				(scope == PortalActivationScope.WorldTimed ? $" {minutes:0} min" : string.Empty) + "; destination and requirements are left for a designer.");
		}
	}

	/// <summary>
	/// A <see cref="DungeonEntrance"/> with an EMPTY dungeon name (Jim, 2026-10-10: dungeon interiors are hand-made scenes
	/// on other atlas layers, linked by hand). At the far end of the site's carved tunnel when its shaper left one, else at
	/// the site.
	/// </summary>
	[Serializable]
	public class DungeonEntranceFeature : PointOfInterestFeature
	{
		[Tooltip("Stand at the end of the site's carved tunnel (its shape), not at the site's anchor.")]
		public bool AtTunnelEnd = true;

		[Tooltip("Where the entrance stands in the site's frame when there is no tunnel, metres.")]
		public Vector2 Offset = new Vector2(0.0f, 6.0f);

		public override void Build(PointOfInterestSiteContext context)
		{
			Vector3 position = context.OnGround(Offset);
			float yaw = context.Record.Yaw;
			PointOfInterestShape shape = AtTunnelEnd ? FindShape(context) : null;
			if (shape != null && shape.Size.z > 0.0f)
			{
				Vector3 along = Quaternion.Euler(0.0f, shape.Yaw, 0.0f) * Vector3.forward;
				position = shape.Position + along * Mathf.Max(0.0f, shape.Size.z - 2.0f);
				yaw = shape.Yaw;
			}

			GameObject entrance = context.AddChild(PointOfInterestFeatureKit.UniqueName(context, "Dungeon Entrance"));
			entrance.transform.SetPositionAndRotation(position + Vector3.up * 2.0f, Quaternion.Euler(0.0f, yaw, 0.0f));
			NetworkObject networkObject = PointOfInterestFeatureKit.AddNetworkObject(entrance);
			BoxCollider box = entrance.AddComponent<BoxCollider>();
			box.size = new Vector3(4.0f, 4.0f, 1.0f);
			DungeonEntrance dungeon = entrance.AddComponent<DungeonEntrance>();
			dungeon.DungeonName = string.Empty;
			dungeon.InteractionRange = 6.0f;
			PointOfInterestFeatureKit.SetInteractTriggers(dungeon, PointOfInterestFeatureKit.Load<Trigger>(context, PointOfInterestFeatureKit.DungeonEntranceTriggerPath));

			// The site itself is the map entry, behind discovery; the entrance's own marker shows only up close.
			MapMarker marker = entrance.GetComponent<MapMarker>();
			if (marker != null)
			{
				marker.Type = MapMarkerType.DungeonEntrance;
				marker.ShowOnWorldMap = false;
				marker.ShowOnMinimap = true;
				EditorUtility.SetDirty(marker);
			}
			PointOfInterestFeatureKit.AssignSceneId(networkObject, context, 0xD06E);
			EditorUtility.SetDirty(dungeon);
			PointOfInterestFeatureKit.Note(context, "dungeon entrance placed with an empty DungeonName: link it to its interior by hand.");
		}

		private static PointOfInterestShape FindShape(PointOfInterestSiteContext context)
		{
			if (context.Points?.Shapes == null)
			{
				return null;
			}
			foreach (PointOfInterestShape shape in context.Points.Shapes)
			{
				if (shape != null && shape.SiteId == context.Record.Id)
				{
					return shape;
				}
			}
			return null;
		}
	}
}
#endif
