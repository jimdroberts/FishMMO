using UnityEditor;

namespace FishMMO.TestHarness.World.Editor
{
	/// <summary>
	/// Imports the world sim walker's character as a Humanoid with an avatar mapped from its bone
	/// names, so Mecanim can retarget any humanoid clip onto it, and its own clips (swim, tread, climb,
	/// punches) named and looped for <see cref="WorldSimWalkerBody"/>.
	/// </summary>
	/// <remarks>
	/// The model is a static scan rigged afterwards with a Mixamo-named skeleton (Hips, Spine, Spine1,
	/// Spine2, Neck, Head, LeftArm, LeftUpLeg …), which Unity's auto-mapping recognises, and its clips
	/// were keyframed on that skeleton (memory tools autorig/anim.py). Its material is built by
	/// <see cref="WorldSimWalkerBody"/>, so none is imported. Set here rather than in a hand-written
	/// .meta, which would have to match the importer's serialisation exactly or be read back with defaults.
	/// </remarks>
	internal sealed class TestHumanImport : AssetPostprocessor
	{
		/// <summary>The clips that cycle; the punches play once.</summary>
		private static readonly string[] Looping = { "SwimCrawl", "SwimTread", "Climb" };

		/// <summary>
		/// The rest pose's own take, first in the file so the importer takes the model's default pose (and so the avatar's
		/// T-pose) from it: imported as no clip.
		/// </summary>
		private const string RestTake = "0Rest";

		public override uint GetVersion() => 3;

		private void OnPreprocessModel()
		{
			if (assetPath != WorldSimWalkerBody.ModelPath)
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
		}

		private void OnPreprocessAnimation()
		{
			if (assetPath != WorldSimWalkerBody.ModelPath)
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
				// In place: the walker moves itself, so the clips' root neither travels, turns nor bobs.
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
