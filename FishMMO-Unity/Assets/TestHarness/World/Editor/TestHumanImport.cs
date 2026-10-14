using UnityEditor;

namespace FishMMO.TestHarness.World.Editor
{
	/// <summary>
	/// Imports the world sim walker's character as a Humanoid with an avatar mapped from its bone
	/// names, so Mecanim can retarget any humanoid clip onto it.
	/// </summary>
	/// <remarks>
	/// The model is a static scan rigged afterwards with a Mixamo-named skeleton (Hips, Spine, Spine1,
	/// Spine2, Neck, Head, LeftArm, LeftUpLeg …), which Unity's auto-mapping recognises. It carries no
	/// clips and its material is built by <see cref="WorldSimWalkerBody"/>, so neither is imported.
	/// Set here rather than in a hand-written .meta, which would have to match the importer's
	/// serialisation exactly or be read back with defaults.
	/// </remarks>
	internal sealed class TestHumanImport : AssetPostprocessor
	{
		public override uint GetVersion() => 1;

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
			importer.importAnimation = false;
			importer.materialImportMode = ModelImporterMaterialImportMode.None;
			importer.importCameras = false;
			importer.importLights = false;
			importer.importBlendShapes = false;
		}
	}
}
