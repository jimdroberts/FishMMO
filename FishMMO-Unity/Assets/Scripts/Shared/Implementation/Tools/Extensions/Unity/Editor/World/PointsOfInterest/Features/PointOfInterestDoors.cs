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
		/// A big race's building has steps of its own size — a giant's are half a metre high, a wall to a person. Where its
		/// threshold stands above a person's step, a person's flight of stone steps goes up beside the giant's to the door,
		/// so a giant's hall can still be walked into.
		/// </summary>
		private static void PersonSteps(PointOfInterestSiteContext context, PointOfInterestSiteContext.PlacedDoor door)
		{
			if (door.Scale <= 1.05f || context.GroundAt == null)
			{
				return;
			}
			GameObject stone = StructureKit.Prefab("StepStone", 0);
			if (stone == null)
			{
				return;
			}
			// From the doorway's middle, outward along the way in: the threshold's height over the ground there.
			Vector3 mid = door.Hinge + door.Across * (door.Width * 0.5f);
			Vector3 outward = -door.Inward;
			// Out past the wall and the building's own steps' foot (a giant's flight runs about its rise ÷ 0.2 × 0.3 m).
			float threshold = mid.y;
			Vector3 foot = mid + outward * (0.6f * door.Scale);
			float ground = context.GroundAt(foot.x, foot.z);
			float rise = threshold - ground;
			if (rise <= 0.25f)
			{
				return;
			}
			int n = Mathf.CeilToInt(rise / PersonRise);
			Quaternion turn = Quaternion.LookRotation(outward, Vector3.up);
			float width = Mathf.Min(1.4f, door.Width);
			// Step i stands i risers up, set out from the wall face by (n - i) treads; the top one is the threshold.
			Vector3 face = mid + outward * (0.35f * door.Scale);
			for (int i = 1; i < n; i++)
			{
				float top = ground + rise * i / n, run = (n - i) * 0.3f;
				Vector3 centre = face + outward * (run * 0.5f);
				float bottom = context.GroundAt(centre.x, centre.z) - 0.3f;
				context.AddProp(stone, new Vector3(centre.x, bottom, centre.z), turn, new Vector3(width, Mathf.Max(0.05f, top - bottom), run));
			}
		}

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
