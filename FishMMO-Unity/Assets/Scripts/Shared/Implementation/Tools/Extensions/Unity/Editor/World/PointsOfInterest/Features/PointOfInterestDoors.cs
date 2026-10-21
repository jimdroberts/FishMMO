#if UNITY_EDITOR
using System.IO;
using FishMMO.Shared.Core;
using FishNet.Object;
using UnityEditor;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// The doors of a site's buildings (2026-10-11): each doorway the structure kit leaves open
	/// (<see cref="StructureDraft.Door"/>) gets a real door, a scene object the server runs, not part of the baked building.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>What a door is.</b> A scene NetworkObject at the hinge (a deterministic SceneId), its <c>Leaf</c> swinging on a
	/// <see cref="SwitchTargetMover"/> into the building, and on the leaf the <c>Panel</c>: the kit's door leaf (DoorLeaf,
	/// scaled to the doorway), a box collider and the <see cref="Switch"/> a player uses ("Door Interact": a
	/// <see cref="SwitchAction"/>). The server decides; the change goes to the door's observers with the tick it began at,
	/// so every peer swings it on the same ticks, and a player arriving later gets its state in the spawn payload.
	/// </para>
	/// <para>
	/// <b>Never in the way while it moves.</b> The panel's collider is solid only while the door is shut and still
	/// (<c>SwitchTargetMover.solidWhenClosed</c>); swinging or open it is a trigger, which the character controller
	/// ignores and the interaction ray still hits. A door can't shove a player or fight their prediction mid-swing, and an
	/// open door can still be clicked shut.
	/// </para>
	/// </remarks>
	[InitializeOnLoad]
	public sealed class PointOfInterestDoors : IPointOfInterestSiteBuilder
	{
		static PointOfInterestDoors() => PointOfInterestSiteBuilders.Register(new PointOfInterestDoors());

		public const string TriggerPath = "Assets/Templates/Entity/ECA/Interactions/Door Interact.asset";
		/// <summary>How far a door swings open, degrees (short of square, so it never touches the wall it opens against).</summary>
		public const float SwingDegrees = 86f;
		/// <summary>Seconds a door takes to open or shut.</summary>
		public const float TravelSeconds = 1.1f;
		/// <summary>The leaf's thickness, metres.</summary>
		public const float Thickness = 0.1f;
		private const int SaltBase = 0x0D00;

		public bool Handles(PointOfInterestRecord record) => true;

		public void Build(PointOfInterestSiteContext context)
		{
			if (context.Doors.Count == 0)
			{
				return;
			}
			Trigger trigger = EnsureTrigger();
			for (int i = 0; i < context.Doors.Count; i++)
			{
				Make(context, context.Doors[i], i, trigger);
			}
		}

		private static void Make(PointOfInterestSiteContext context, PointOfInterestSiteContext.PlacedDoor door, int index, Trigger trigger)
		{
			// The root at the hinge, facing into the building: local +z inward, the leaf along ±x.
			GameObject root = context.AddChild(PointOfInterestFeatureKit.UniqueName(context, $"Door {index + 1}"));
			Quaternion facing = Quaternion.LookRotation(door.Inward, Vector3.up);
			root.transform.SetPositionAndRotation(door.Hinge, facing);
			NetworkObject networkObject = PointOfInterestFeatureKit.AddNetworkObject(root);
			float side = Vector3.Dot(door.Across, facing * Vector3.right) >= 0f ? 1f : -1f;

			var leaf = new GameObject("Leaf");
			leaf.transform.SetParent(root.transform, false);
			var panel = new GameObject("Panel");
			panel.transform.SetParent(leaf.transform, false);
			panel.transform.localPosition = new Vector3(side * door.Width * 0.5f, door.Height * 0.5f, 0f);

			bool lift = door.Motion == StructureDoorMotion.Lift, bridge = door.Motion == StructureDoorMotion.Drawbridge;
			BoxCollider box = panel.AddComponent<BoxCollider>();
			float thick = (bridge ? 0.22f : lift ? 0.12f : Thickness) * Mathf.Max(0.5f, door.Scale);
			if (bridge)
			{
				// Raised, it stands on the outer side of its hinge line; lowered, it lies out over the ditch.
				panel.transform.localPosition += new Vector3(0f, 0f, -thick * 0.5f);
			}
			box.size = new Vector3(Mathf.Max(0.1f, door.Width - 0.02f), Mathf.Max(0.1f, door.Height - 0.02f), thick);

			// The kit's leaf, scaled to the doorway (its hinge side at x = -0.5; mirrored for a door hung on the right): a
			// portcullis's grid for a lifting gate, heavy studded boards for a drawbridge.
			GameObject prefab = lift ? StructureKit.Prefab("Portcullis", door.Kind == StructureDoorKind.Planks ? 1 : 0) : StructureKit.Prefab("DoorLeaf", bridge ? (int)StructureDoorKind.Studded : (int)door.Kind);
			if (prefab != null)
			{
				var visual = (GameObject)PrefabUtility.InstantiatePrefab(prefab, panel.transform);
				visual.name = "Visual";
				// In the site's structure palette, as its building's door (a stone church's door is the stone style's).
				StructurePaletteDressing.Dress(visual, prefab, context.Palette, StructurePaletteDressing.StyleOf(door.Building));
				visual.transform.localPosition = new Vector3(0f, -door.Height * 0.5f, 0f);
				visual.transform.localRotation = Quaternion.identity;
				visual.transform.localScale = new Vector3(side * door.Width, door.Height * (lift ? 1f : 0.5f), Mathf.Max(0.5f, door.Scale) * (bridge ? 3f : 1f));
				foreach (Collider c in visual.GetComponentsInChildren<Collider>(true))
				{
					Object.DestroyImmediate(c);
				}
			}
			else
			{
				PointOfInterestFeatureKit.Note(context, "the door leaf (Structure_DoorLeaf) is not generated yet: Biome Tools → Generate biome art, then regenerate. Doors stand invisible.");
			}

			SwitchTargetMover mover = leaf.AddComponent<SwitchTargetMover>();
			var moverSerialized = new SerializedObject(mover);
			moverSerialized.FindProperty("movedTransform").objectReferenceValue = leaf.transform;
			// A door swings into the building; a portcullis rises into its arch; a drawbridge lowers outward about its foot.
			moverSerialized.FindProperty("openPositionOffset").vector3Value = lift ? new Vector3(0f, door.Height * 0.92f, 0f) : Vector3.zero;
			moverSerialized.FindProperty("openRotationOffset").vector3Value = lift ? Vector3.zero : bridge ? new Vector3(-90f, 0f, 0f) : new Vector3(0f, -side * SwingDegrees, 0f);
			moverSerialized.FindProperty("travelSeconds").floatValue = lift ? 3f : bridge ? 4f : TravelSeconds;
			moverSerialized.FindProperty("startActivated").boolValue = door.StartsOpen;
			moverSerialized.FindProperty("solidWhenOpenToo").boolValue = bridge;
			/* A drawbridge that starts down is saved down: raised in the scene until the game ran, the editor showed a door
			 * standing at the ditch's inner lip and nothing over the moat, and the NavMesh (baked from the scene's colliders)
			 * had no way across it (2026-10-15: "missing draw bridges"). Its closed pose is worked out from this one. */
			if (bridge && door.StartsOpen)
			{
				leaf.transform.localRotation = Quaternion.Euler(moverSerialized.FindProperty("openRotationOffset").vector3Value);
				moverSerialized.FindProperty("authoredOpen").boolValue = true;
			}
			SerializedProperty solid = moverSerialized.FindProperty("solidWhenClosed");
			solid.arraySize = 1;
			solid.GetArrayElementAtIndex(0).objectReferenceValue = box;
			moverSerialized.ApplyModifiedPropertiesWithoutUndo();

			Switch door2 = panel.AddComponent<Switch>();
			door2.IsToggle = true;
			var switchSerialized = new SerializedObject(door2);
			switchSerialized.FindProperty("target").objectReferenceValue = leaf;
			switchSerialized.FindProperty("title").stringValue = lift ? "Portcullis" : bridge ? "Drawbridge" : door.Width > 1.5f && door.Kind == StructureDoorKind.Planks ? "Gate" : "Door";
			switchSerialized.ApplyModifiedPropertiesWithoutUndo();
			if (lift || bridge)
			{
				// Worked from the gate passage or the bank, not with a hand on the leaf.
				door2.InteractionRange = bridge ? 8f : 6f;
			}
			// A door keeps its own name: the switch's required namer would otherwise give it a person's.
			SceneObjectNamer namer = panel.GetComponent<SceneObjectNamer>();
			if (namer != null)
			{
				var namerSerialized = new SerializedObject(namer);
				SerializedProperty mode = namerSerialized.FindProperty("settings.Mode");
				if (mode != null)
				{
					mode.enumValueIndex = System.Array.IndexOf(System.Enum.GetValues(typeof(SceneObjectNamingMode)), SceneObjectNamingMode.Authored);
					namerSerialized.ApplyModifiedPropertiesWithoutUndo();
				}
			}
			if (trigger != null)
			{
				PointOfInterestFeatureKit.SetInteractTriggers(door2, trigger);
			}
			PointOfInterestFeatureKit.AssignSceneId(networkObject, context, SaltBase + index);
			if (door.Motion == StructureDoorMotion.Swing)
			{
				PersonSteps(context, door);
			}
			EditorUtility.SetDirty(door2);
			EditorUtility.SetDirty(mover);
		}

		/// <summary>The highest riser a person's character controller climbs, metres (MaxStepHeight 0.25 m).</summary>
		private const float PersonRise = 0.2f;

		/// <summary>
		/// A person's flight of stone steps up to a door where the building's own do not reach the ground in a person's step:
		/// <list type="bullet">
		/// <item>A big race's building has steps of its own size — a giant's are half a metre high, a wall to a person — so a
		/// person's flight goes up beside them from the ground to the threshold.</item>
		/// <item>A building on a slope is seated as much as its foundation allows below its centre's ground
		/// (PropsFeature.SeatHeight), and its own steps start at its origin: on the downhill side their first riser stood as
		/// much as 0.65 m off the ground, more than the controller's 0.25 m step (2026-10-15: "interiors aren't always fully
		/// walkable"). A flight goes on from their foot down to the ground.</item>
		/// </list>
		/// </summary>
		/// <remarks>
		/// 2026-10-19 (round 3: "the stairs don't fit properly a lot of the time"): the building's own flight is found where it is
		/// (<see cref="OwnFlight"/>: its foot and lowest tread read off the building's collider along the door's line) instead of
		/// reckoned from a 0.45 m wall, which left a gap or an overlap of up to 0.3 m between the two flights on thinner or
		/// thicker walls and missed a stilt deck's stair altogether; and the ground is measured again at the flight's own foot,
		/// which on falling ground lies lower than at its head, so its first riser is a step, not a ledge.
		/// </remarks>
		private static void PersonSteps(PointOfInterestSiteContext context, PointOfInterestSiteContext.PlacedDoor door)
		{
			if (context.GroundAt == null)
			{
				return;
			}
			// From the doorway's middle, outward along the way in.
			Vector3 mid = door.Hinge + door.Across * (door.Width * 0.5f);
			Vector3 outward = -door.Inward;
			float top, from;
			if (door.Scale > 1.05f)
			{
				// Out past the wall and up beside the building's own flight, to the threshold.
				top = mid.y;
				from = 0.35f * door.Scale;
			}
			else if (OwnFlight(door.Building, door.Width / door.Scale, door.Height / door.Scale, out float footOut, out float tread))
			{
				// From the foot of the building's own flight (its lowest tread), or from the wall's or plinth's face with none. A
				// door whose way down is not straight out from it (a keep's door up a forestair along its wall, a tower's door onto
				// the wall-walk) gets no flight of a person's: its own way down is the building's.
				if (tread > 0.5f)
				{
					return;
				}
				top = door.Base + tread * door.Scale;
				from = footOut * door.Scale;
			}
			else
			{
				// From the foot of the building's own flight (StructurePieces.Steps: its lowest tread one riser over the
				// origin, a tread's run per riser out from 0.12 m past the wall's face), or the wall's face with none.
				float rise = mid.y - door.Base;
				int own = rise > StructurePieces.StepRise + 1e-3f ? Mathf.CeilToInt(rise / StructurePieces.StepRise) : 0;
				top = own > 0 ? door.Base + rise / own : mid.y;
				from = WallToHinge + (own > 0 ? 0.12f + (own - 1) * StructurePieces.StepTread : 0f);
			}
			Vector3 face = mid + outward * from;
			Vector3 foot = face + outward * 0.15f;
			float ground = context.GroundAt(foot.x, foot.z);
			// The flight's foot is (n - 1) treads out from the face: on falling ground lower than at its head, so measured again
			// there until the flight reaches the ground it starts from.
			for (int pass = 0; pass < 6; pass++)
			{
				float rise = top - ground;
				if (rise <= 0.25f || rise > MaxPersonFlight)
				{
					break;
				}
				Vector3 end = face + outward * ((Mathf.CeilToInt(rise / PersonRise) - 1) * PersonTread + 0.15f);
				float there = context.GroundAt(end.x, end.z);
				if (there >= ground - 0.02f)
				{
					break;
				}
				ground = there;
			}
			float drop = top - ground;
			if (drop <= 0.25f || drop > MaxPersonFlight)
			{
				return;
			}
			GameObject stone = StructureKit.Prefab("StepStone", 0);
			if (stone == null)
			{
				return;
			}
			int n = Mathf.CeilToInt(drop / PersonRise);
			Quaternion turn = Quaternion.LookRotation(outward, Vector3.up);
			// Two abreast where the doorway is that wide, and never narrower than a body with room either side.
			float width = Mathf.Clamp(door.Width + 0.4f, 1.2f, StructurePieces.StairWidth);
			// Step i stands i risers up from the ground, set out from the face by (n - i) treads; the top one is where the
			// flight arrives (the threshold, or the building's own lowest tread).
			for (int i = 1; i < n; i++)
			{
				float height = ground + drop * i / n, run = (n - i) * PersonTread;
				Vector3 centre = face + outward * (run * 0.5f);
				float bottom = context.GroundAt(centre.x, centre.z) - 0.3f;
				context.AddProp(stone, new Vector3(centre.x, bottom, centre.z), turn, new Vector3(width, Mathf.Max(0.05f, height - bottom), run));
			}
		}

		/// <summary>A person's step's tread, metres (StructurePieces.StepTread).</summary>
		private const float PersonTread = 0.3f;

		private static readonly System.Collections.Generic.Dictionary<(string, float, float), (bool ok, float foot, float tread)> ownFlights =
			new System.Collections.Generic.Dictionary<(string, float, float), (bool, float, float)>();

		/// <summary>
		/// Where a building's own way down from a door reaches its ground line, in its own frame: how far out from the doorway's
		/// middle the foot of its flight is (<paramref name="footOut"/>), and its lowest tread's height over the building's origin
		/// (<paramref name="tread"/>; the threshold's when no step leads down, the foot then the wall's or plinth's face). Read off
		/// the collider level along the door's centre line from its threshold out, as a character walks it, so a plinth's steps,
		/// a forestair or a stilt deck's stair are each found where they stand. False when the kit has no such piece and door.
		/// Cached per building and doorway.
		/// </summary>
		private static bool OwnFlight(string building, float width, float height, out float footOut, out float tread)
		{
			footOut = 0f;
			tread = 0f;
			if (string.IsNullOrEmpty(building))
			{
				return false;
			}
			var key = (building, Mathf.Round(width * 100f) / 100f, Mathf.Round(height * 100f) / 100f);
			lock (ownFlights)
			{
				if (ownFlights.TryGetValue(key, out var known))
				{
					footOut = known.foot;
					tread = known.tread;
					return known.ok;
				}
			}
			bool ok = false;
			StructurePiece piece = StructurePieces.Find(building);
			for (int v = 0; piece != null && v < piece.Variants && !ok; v++)
			{
				foreach (StructureDoor d in StructurePieces.DoorsOf(piece, v))
				{
					if (Mathf.Abs(d.Width - width) > 0.02f || Mathf.Abs(d.Height - height) > 0.02f)
					{
						continue;
					}
					MeshBuilder mesh = StructurePieces.Build(piece, v, StructurePieces.LevelCount - 1, StructurePieces.DefaultSeed).Mesh;
					Vector3 m = d.Hinge + d.Across * (d.Width * 0.5f);
					Vector3 o = -d.Inward;
					float h = m.y;
					for (float s = 0f; s <= 12f; s += 0.05f)
					{
						Vector3 p = m + o * s;
						// The highest surface under a step up from where the walker is.
						float surface = HighestBelow(mesh, p.x, p.z, h + StructurePieces.StepRise + 0.1f);
						if (surface <= 0.02f)
						{
							footOut = s;
							tread = h;
							ok = true;
							break;
						}
						h = surface;
					}
					break;
				}
			}
			lock (ownFlights)
			{
				ownFlights[key] = (ok, footOut, tread);
			}
			return ok;
		}

		/// <summary>The highest point of the mesh straight under (x, from, z), or negative infinity when nothing is under it.</summary>
		private static float HighestBelow(MeshBuilder mesh, float x, float z, float from)
		{
			float best = float.NegativeInfinity;
			for (int sub = 0; sub < mesh.Submeshes.Count; sub++)
			{
				var list = mesh.Submeshes[sub];
				for (int i = 0; i + 2 < list.Count; i += 3)
				{
					Vector3 a = mesh.Positions[list[i]], b = mesh.Positions[list[i + 1]], c = mesh.Positions[list[i + 2]];
					float area = (b.x - a.x) * (c.z - a.z) - (b.z - a.z) * (c.x - a.x);
					if (Mathf.Abs(area) < 1e-9f)
					{
						continue;
					}
					float w0 = ((b.x - x) * (c.z - z) - (b.z - z) * (c.x - x)) / area;
					float w1 = ((c.x - x) * (a.z - z) - (c.z - z) * (a.x - x)) / area;
					float w2 = 1f - w0 - w1;
					if (w0 < 0f || w1 < 0f || w2 < 0f)
					{
						continue;
					}
					float y = w0 * a.y + w1 * b.y + w2 * c.y;
					if (y <= from && y > best)
					{
						best = y;
					}
				}
			}
			return best;
		}

		/// <summary>From a door's hinge (on the wall's inside face) out to the wall's outside face, metres: a house's 0.45 m wall less the leaf's inset.</summary>
		private const float WallToHinge = 0.4f;

		/// <summary>The most a person's flight climbs, metres; more and the building stands on a drop no flight belongs on.</summary>
		private const float MaxPersonFlight = 3f;

		/// <summary>The door's interaction: a trigger asset with a <see cref="SwitchAction"/>, made the first time it is wanted.</summary>
		public static Trigger EnsureTrigger()
		{
			Trigger trigger = AssetDatabase.LoadAssetAtPath<Trigger>(TriggerPath);
			if (trigger == null)
			{
				string folder = Path.GetDirectoryName(TriggerPath).Replace('\\', '/');
				if (!AssetDatabase.IsValidFolder(folder))
				{
					return null;
				}
				trigger = ScriptableObject.CreateInstance<Trigger>();
				AssetDatabase.CreateAsset(trigger, TriggerPath);
			}
			bool hasAction = false;
			foreach (BaseAction action in trigger.OnConditionsMetActions)
			{
				hasAction |= action is SwitchAction;
			}
			if (!hasAction)
			{
				trigger.OnConditionsMetActions.Add(new SwitchAction());
				EditorUtility.SetDirty(trigger);
			}
			WorldEditorAssets.RegisterAddressable(trigger);
			return trigger;
		}
	}
}
#endif
