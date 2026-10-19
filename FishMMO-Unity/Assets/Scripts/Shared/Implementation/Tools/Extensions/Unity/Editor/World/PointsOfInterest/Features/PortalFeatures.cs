#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using FishMMO.Shared.Core;
using FishNet.Object;
using UnityEditor;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// A dormant portal at the site: a same-scene <see cref="Teleporter"/> or a cross-scene <see cref="SceneTeleporter"/>,
	/// with a <see cref="PortalActivation"/> whose scope is a seeded draw (2026-10-10: both kinds; destinations empty
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
			// The frame the site's props already laid (a portal template's centrepiece): the portal stands in it, and no second
			// frame is laid over it at another turn and height (2026-10-18: two frames, often an arch through a ring).
			bool framed = TryLaidFrame(context, out PointOfInterestSiteContext.PlacedProp laidFrame);
			if (framed)
			{
				portal.transform.SetPositionAndRotation(laidFrame.Position, laidFrame.Rotation);
			}
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

			GameObject frame = framed ? null : context.ResolvePiece("portal", context.Record.SiteSeed);
			if (frame != null)
			{
				context.AddProp(frame, portal.transform.position, portal.transform.rotation, Vector3.one);
			}
			PointOfInterestFeatureKit.Note(context, $"portal {index}: {(crossScene ? "cross-scene" : "same-scene")}, {scope}" +
				(scope == PortalActivationScope.WorldTimed ? $" {minutes:0} min" : string.Empty) + "; destination and requirements are left for a designer.");
		}


		/// <summary>The first portal frame laid at the site before this feature (the props' centrepiece), false for none.</summary>
		private static bool TryLaidFrame(PointOfInterestSiteContext context, out PointOfInterestSiteContext.PlacedProp frame)
		{
			frame = default;
			if (context.Laid == null)
			{
				return false;
			}
			foreach (PointOfInterestSiteContext.PlacedProp laid in context.Laid)
			{
				if (laid.Prefab != null && StructurePieces.TryParsePrefab(laid.Prefab.name, out StructurePiece piece, out _) && piece.HasTag("portal"))
				{
					frame = laid;
					return true;
				}
			}
			return false;
		}
	}

	/// <summary>
	/// A <see cref="DungeonEntrance"/> with an EMPTY dungeon name (2026-10-10: dungeon interiors are hand-made scenes
	/// on other atlas layers, linked by hand), in the doorway of its door: at its own site the door the props laid (the
	/// DungeonPortal or DungeonBarrow centrepiece, at the threshold DungeonEntranceShaper cut into the face), in a large cave
	/// the gate this lays against its great cavern's wall (DungeonEntranceShaper.TryCavernGate; PointOfInterestChamberFeature
	/// gives it the chamber).
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>In the doorway</b> (2026-10-18: "boring and misaligned"; it was a bare 4 × 4 m box 7 m out in front of a solid
	/// slab): the interaction box fills the doorway's reveal, from the threshold to the passage, its clear width and height
	/// (<see cref="DoorwayBox"/>), turned and scaled with the door; the passage behind it is a dark room for the client
	/// (SceneRoom). The forecourt is dressed about the door's axis on this feature's own stream: braziers either side of the
	/// steps, a pair of guardians and banners, fallen stone and bones at its edges, never on water (context.IsWet).
	/// </para>
	/// <para>A site whose props laid no door (its art not generated) keeps the old box at <see cref="Offset"/>.</para>
	/// </remarks>
	[Serializable]
	public class DungeonEntranceFeature : PointOfInterestFeature
	{
		[Tooltip("Where the entrance stands in the site's frame when no door stands at the site, metres.")]
		public Vector2 Offset = Vector2.zero;

		[Tooltip("The door this feature lays itself where the site's props laid none (a large cave's great cavern): a structure kit query.")]
		public string Architecture = "=DungeonGate";

		[Tooltip("Dress the forecourt: braziers, guardians and banners either side of the door, fallen stone and bones at its edges.")]
		public bool Dressing = true;

		/// <summary>How deep the interaction box reaches into a doorway with no reveal of its own, metres.</summary>
		public const float MinDepth = 0.6f;

		/// <summary>A point of a piece's own frame turned by <paramref name="yaw"/> degrees about +y (as Quaternion.Euler(0, yaw, 0) turns it).</summary>
		public static Vector3 Turn(float yaw, Vector3 v)
		{
			float r = yaw * Mathf.Deg2Rad, c = Mathf.Cos(r), s = Mathf.Sin(r);
			return new Vector3(v.x * c + v.z * s, v.y, -v.x * s + v.z * c);
		}

		/// <summary>
		/// The interaction box in a door laid at <paramref name="position"/>, turned <paramref name="yaw"/> and scaled
		/// <paramref name="scale"/> (pure): its centre (world) and size in the door's frame — the doorway's clear width and height
		/// over its sill, from the threshold's face line through the wall's thickness to the passage.
		/// </summary>
		public static void DoorwayBox(Vector3 position, float yaw, float scale, in StructureDoorway door, out Vector3 centre, out Vector3 size)
		{
			float depth = Mathf.Max(MinDepth, door.PassageCentre.z - door.PassageHalfSize.z - door.Sill.z);
			var local = new Vector3(door.Sill.x, door.Sill.y + 0.5f * door.Height, door.Sill.z + 0.5f * depth);
			centre = position + Turn(yaw, local * scale);
			size = new Vector3(door.Width, door.Height, depth) * scale;
		}

		/// <summary>The door laid at the site: a laid piece that has a doorway (StructurePieces.TryDoorwayOf), its place and its doorway; false for none.</summary>
		public static bool TryLaidDoor(IReadOnlyList<PointOfInterestSiteContext.PlacedProp> laid, out PointOfInterestSiteContext.PlacedProp door, out StructureDoorway doorway)
		{
			door = default;
			doorway = default;
			if (laid == null)
			{
				return false;
			}
			foreach (PointOfInterestSiteContext.PlacedProp prop in laid)
			{
				if (prop.Prefab != null && StructurePieces.TryParsePrefab(prop.Prefab.name, out StructurePiece piece, out int variant) && StructurePieces.TryDoorwayOf(piece, variant, out doorway))
				{
					door = prop;
					return true;
				}
			}
			return false;
		}

		public override void Build(PointOfInterestSiteContext context)
		{
			// The door: the one the props laid, else the cavern's gate this lays, else none (the old bare box).
			Vector3 origin;
			float yaw, scale = 1f;
			bool hasDoor = false, rustic = false;
			StructureDoorway doorway = default;
			CaveSystem cave = null;
			if (TryLaidDoor(context.Laid, out PointOfInterestSiteContext.PlacedProp laid, out doorway))
			{
				origin = laid.Position;
				yaw = laid.Rotation.eulerAngles.y;
				scale = Mathf.Abs(laid.Scale.y);
				hasDoor = true;
				// Before a barrow or an old crypt door: torches and standing stones, not braziers and statues.
				StructurePieces.TryParsePrefab(laid.Prefab.name, out StructurePiece laidPiece, out _);
				rustic = laidPiece != null && (laidPiece.Id == "DungeonBarrow" || !laidPiece.HasTag("dungeon"));
			}
			else if (CaveShaper.TryForm(context.Record.Kind, out _) && (cave = CaveShaper.SystemOf(context.Points, context.Record.Id)) != null
				&& DungeonEntranceShaper.TryCavernGate(cave, context.Record.Id, out DungeonEntranceShaper.CavernGate gate))
			{
				origin = gate.Threshold;
				yaw = gate.PieceYaw;
				// A giant's dungeon has a giant's gate (RaceStature), on top of the cavern's own scale.
				scale = gate.Scale * Mathf.Max(1f, RaceStature.BuildingScale(context.Record.Race));
				GameObject prefab = context.ResolvePiece(Architecture, context.Record.SiteSeed);
				if (prefab != null && StructurePieces.TryParsePrefab(prefab.name, out StructurePiece piece, out int variant) && StructurePieces.TryDoorwayOf(piece, variant, out doorway))
				{
					context.AddProp(prefab, origin, Quaternion.Euler(0f, yaw, 0f), Vector3.one * scale);
					hasDoor = true;
				}
				else
				{
					PointOfInterestFeatureKit.Note(context, $"dungeon gate: no art for '{Architecture}' yet (Generate Biome Art), so its entrance stands bare at the cavern's wall.");
				}
			}
			else
			{
				origin = context.OnGround(Offset);
				yaw = context.Record.Yaw;
			}

			Vector3 position = origin + Vector3.up * 2f;
			Vector3 size = new Vector3(4f, 4f, 1f);
			if (hasDoor)
			{
				DoorwayBox(origin, yaw, scale, doorway, out position, out size);
				AddPassageRoom(context, origin, yaw, scale, doorway);
				if (Dressing)
				{
					Dress(context, origin, yaw, scale, doorway, cave, rustic);
				}
			}
			GameObject entrance = context.AddChild(PointOfInterestFeatureKit.UniqueName(context, "Dungeon Entrance"));
			entrance.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
			NetworkObject networkObject = PointOfInterestFeatureKit.AddNetworkObject(entrance);
			BoxCollider box = entrance.AddComponent<BoxCollider>();
			box.size = size;
			DungeonEntrance dungeon = entrance.AddComponent<DungeonEntrance>();
			dungeon.DungeonName = string.Empty;
			// Reached from the forecourt before a big door as from before a small one.
			dungeon.InteractionRange = Mathf.Max(6f, 0.5f * size.x + 4f);
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

		/// <summary>The passage behind the doorway as a dark room (SceneRoom): the client darkens what is drawn in it as indoors.</summary>
		private static void AddPassageRoom(PointOfInterestSiteContext context, Vector3 origin, float yaw, float scale, in StructureDoorway door)
		{
			if (context.Points == null)
			{
				return;
			}
			context.Points.Rooms ??= new List<SceneRoom>();
			context.Points.Rooms.Add(new SceneRoom
			{
				Centre = origin + Turn(yaw, door.PassageCentre * scale),
				Yaw = yaw,
				HalfSize = Vector3.Max(Vector3.one * 0.2f, door.PassageHalfSize - Vector3.one * 0.1f) * scale,
			});
		}

		/// <summary>
		/// The forecourt's dressing, in pairs about the door's axis (the door's frame: x across, −z out along the approach):
		/// braziers (torches before a barrow) either side of the steps' foot, a pair of guardian statues (standing stones before a
		/// barrow) and banners, then fallen stone on one side and a few bones. Every draw first, in a fixed order; nothing on water
		/// or, in a cavern, where its floor is not.
		/// </summary>
		private static void Dress(PointOfInterestSiteContext context, Vector3 origin, float yaw, float scale, in StructureDoorway door, CaveSystem cave, bool rustic)
		{
			DeterministicRNG random = context.Random;
			bool guardians = random.NextFloat() < 0.75f, banners = random.NextFloat() < 0.6f, rubble = random.NextFloat() < 0.8f;
			int rubbleSide = random.NextFloat() < 0.5f ? -1 : 1;
			bool column = random.NextFloat() < 0.5f;
			float rubbleTurn = random.NextFloat() * 360f;
			int bones = random.Next(3);
			var boneAt = new Vector3[bones];
			for (int i = 0; i < bones; i++)
			{
				boneAt[i] = new Vector3((random.NextFloat() < 0.5f ? -1f : 1f) * random.Range(2.6f, 4.0f), random.Range(-6.8f, -3.6f), random.NextFloat() * 360f);
			}
			float half = Mathf.Max(0.9f, 0.5f * door.Width);
			int laid = 0;
			void Lay(string tag, float x, float z, float turn, int salt)
			{
				Vector3 at = origin + Turn(yaw, new Vector3(x, 0f, z) * scale);
				float y;
				if (cave != null)
				{
					if (!DungeonEntranceShaper.CavernFloor(cave, at, out y))
					{
						return;
					}
				}
				else
				{
					if (context.IsWet(at.x, at.z, 0.3f))
					{
						return;
					}
					y = context.GroundAt != null ? context.GroundAt(at.x, at.z) : at.y;
				}
				GameObject prefab = context.ResolvePiece(tag, unchecked(context.Record.SiteSeed + salt * 7919));
				if (prefab == null)
				{
					return;
				}
				context.AddProp(prefab, new Vector3(at.x, y, at.z), Quaternion.Euler(0f, yaw + turn, 0f), Vector3.one * scale);
				laid++;
			}
			for (int s = -1; s <= 1; s += 2)
			{
				Lay(rustic ? "=TorchPost" : "=Brazier", s * (half + 1f), -3.2f, 0f, 1 + s);
				if (guardians)
				{
					Lay(rustic ? "=StandingStone" : "=Statue", s * (half + 2f), -5.6f, 0f, 5 + s);
				}
				if (banners && !rustic)
				{
					Lay("=BannerPole", s * 4.7f, -2.4f, 0f, 9 + s);
				}
			}
			if (rubble)
			{
				Lay(column && !rustic ? "=FallenColumn" : "=RubblePile", rubbleSide * 5.6f, -6.4f, rubbleTurn, 12);
			}
			for (int i = 0; i < bones; i++)
			{
				Lay("=BonePile", boneAt[i].x, boneAt[i].y, boneAt[i].z, 20 + i);
			}
			PointOfInterestFeatureKit.Note(context, $"dungeon forecourt: {laid} piece(s) laid about the door.");
		}
	}
}
#endif
