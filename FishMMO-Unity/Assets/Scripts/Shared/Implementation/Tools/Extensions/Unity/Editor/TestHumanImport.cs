using UnityEditor;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Imports the character model (the TestHuman every race wears) as a Humanoid with an avatar mapped from its bone
	/// names, so Mecanim can retarget any humanoid clip onto it, at the height of a character's capsule, with its own
	/// clips (strokes, tread, climb, punches, cast, guard, roll, death, sits) named and looped for
	/// <see cref="CharacterAnimationSet"/>.
	/// </summary>
	/// <remarks>
	/// The model is a static scan rigged afterwards on the game's master skeleton (<see cref="SkeletonBones"/>: Root, Hips,
	/// Spine, Chest, UpperChest, Neck, Head, LeftUpperArm, LeftUpperLeg …, the names Unity's auto-mapping knows best), and
	/// its clips were keyframed on that skeleton (memory tools autorig/anim.py). It is exported 1.8 m tall and imported at
	/// <see cref="Height"/>, the playable capsule's height. Its material is the prefab's, so none is imported. Set here
	/// rather than in a hand-written .meta, which would have to match the importer's serialisation exactly or be read back
	/// with defaults.
	/// </remarks>
	internal sealed class TestHumanImport : AssetPostprocessor
	{
		public const string ModelPath = "Assets/Prefabs/Client/Models/TestHuman/TestHuman.fbx";

		/// <summary>How tall the model is exported, metres.</summary>
		public const float ExportedHeight = 1.8f;

		/// <summary>How tall it is imported, metres: a playable character's capsule (Jim's call: the model meets the capsule).</summary>
		public const float Height = 1.6f;

		/// <summary>The clips that cycle (a sit and a guard are a slow breath held round); the punches, the cast, the roll and
		/// the death play once.</summary>
		private static readonly string[] Looping = { "SwimSurface", "SwimUnder", "SwimTread", "Climb", "SitGround", "SitSeat", "Block" };

		/// <summary>
		/// The rest pose's own take, first in the file so the importer takes the model's default pose (and so the avatar's
		/// T-pose) from it: imported as no clip.
		/// </summary>
		private const string RestTake = "0Rest";

		/// <summary>The rig tool's names for bones the master skeleton calls otherwise (autorig/anim.py RENAME).</summary>
		private static readonly string[] RetiredBones =
		{
			"Spine1", "Spine2", "LeftArm", "RightArm", "LeftForeArm", "RightForeArm",
			"LeftUpLeg", "RightUpLeg", "LeftLeg", "RightLeg", "LeftToeBase", "RightToeBase",
		};

		public override uint GetVersion() => 7;

		private void OnPreprocessModel()
		{
			if (assetPath != ModelPath)
			{
				return;
			}
			var importer = (ModelImporter)assetImporter;
			importer.animationType = ModelImporterAnimationType.Human;
			importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
			importer.autoGenerateAvatarMappingIfUnspecified = true;
			importer.importAnimation = true;
			importer.materialImportMode = ModelImporterMaterialImportMode.None;
			importer.importCameras = false;
			importer.importLights = false;
			importer.importBlendShapes = false;
			importer.globalScale = Height / ExportedHeight;
			/* Unity maps the avatar itself only while no map is stored, and keeps the one it made: after the rig was renamed to
			 * the master skeleton the stored map still named the rig tool's bones ("Transform 'LeftUpLeg' for human bone
			 * 'LeftUpperLeg' not found") and the avatar failed. A map naming a retired bone is cleared, so it is made afresh;
			 * the muscle settings beside it are kept. */
			HumanDescription description = importer.humanDescription;
			bool stale = description.human != null && System.Array.Exists(description.human, bone => System.Array.IndexOf(RetiredBones, bone.boneName) >= 0);
			if (stale)
			{
				description.human = new HumanBone[0];
				description.skeleton = new SkeletonBone[0];
				importer.humanDescription = description;
			}
		}

		private void OnPreprocessAnimation()
		{
			if (assetPath != ModelPath)
			{
				return;
			}
			var importer = (ModelImporter)assetImporter;
			var clips = new System.Collections.Generic.List<ModelImporterClipAnimation>();
			foreach (ModelImporterClipAnimation clip in importer.defaultClipAnimations)
			{
				// Blender names a take "<armature>|<action>": the clip is the action.
				int bar = clip.takeName.LastIndexOf('|');
				clip.name = bar >= 0 ? clip.takeName.Substring(bar + 1) : clip.takeName;
				if (clip.name == RestTake)
				{
					continue;
				}
				clips.Add(clip);
				clip.loopTime = System.Array.IndexOf(Looping, clip.name) >= 0;
				if (clip.loopTime && clip.lastFrame - clip.firstFrame > 2f)
				{
					// A cycle is keyed back to its first pose on its last frame: played round, that pose would show twice.
					clip.lastFrame -= 1f;
				}
				// In place: the walker moves itself, so the clips' root neither travels, turns nor bobs. The body's own height
				// is kept as keyed (baked into the pose, from the original): a sit lowers the hips onto its seat.
				clip.lockRootRotation = true;
				clip.lockRootHeightY = true;
				clip.lockRootPositionXZ = true;
				clip.keepOriginalOrientation = true;
				clip.keepOriginalPositionY = true;
				clip.keepOriginalPositionXZ = true;
			}
			importer.clipAnimations = clips.ToArray();
		}
	}
}
