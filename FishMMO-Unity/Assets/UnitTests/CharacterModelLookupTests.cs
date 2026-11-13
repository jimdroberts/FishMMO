using FishMMO.Shared;
using NUnit.Framework;
using UnityEngine;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The body is built on the model that is staying, not the one being replaced.
	/// </summary>
	/// <remarks>
	/// NPC prefabs carry a placeholder model that is swapped for the race's model on the client. The outgoing model
	/// remains in the hierarchy until the end of the frame, so the lookup found it first, the body stayed on it, and the
	/// NPC stood in its bind pose while it walked around.
	/// </remarks>
	[TestFixture]
	public class CharacterModelLookupTests
	{
		private GameObject root;

		[SetUp]
		public void SetUp()
		{
			root = new GameObject("MeshRoot");
		}

		[TearDown]
		public void TearDown()
		{
			Object.DestroyImmediate(root);
		}

		private CharacterModel AddModel(string name)
		{
			var go = new GameObject(name);
			go.transform.SetParent(root.transform);
			return go.AddComponent<CharacterModel>();
		}

		[Test]
		public void AnNpcKeepingItsPrefabModelStillAnnouncesIt()
		{
			/* OnStartCharacter only runs for the local player. A race with no model set never swaps the NPC's prefab model,
			 * so this is the only thing that tells the animation controller there is a body to animate. */
			string source = ProjectFiles.ReadAllText(System.IO.Path.Combine(
				Application.dataPath, "Scripts/Shared/Implementation/Entity/NPC/NPC.cs"));

			Assert.That(System.Text.RegularExpressions.Regex.IsMatch(source, @"else\s*\{\s*//[^\n]*\n\s*NotifyModelReady\(\);"),
				"the branch with no race model must announce the prefab's own model");
		}

		[Test]
		public void AModelBeingReplacedIsSkipped()
		{
			CharacterModel outgoing = AddModel("Placeholder");
			CharacterModel replacement = AddModel("Race Model");
			outgoing.gameObject.SetActive(false);

			LogAssert.IsTrue(CharacterAnimationController.FindLiveModel(root.transform) == replacement,
				"the body must be built on the replacement, not the model about to be destroyed");
		}

		[Test]
		public void AModelUnderAHiddenMeshRootIsStillFound()
		{
			CharacterModel model = AddModel("Race Model");
			root.SetActive(false);

			LogAssert.IsTrue(CharacterAnimationController.FindLiveModel(root.transform) == model,
				"a hidden mesh root is not a model being replaced");
		}

		[Test]
		public void NoLiveModelFindsNothing()
		{
			AddModel("Placeholder").gameObject.SetActive(false);

			LogAssert.IsTrue(CharacterAnimationController.FindLiveModel(root.transform) == null,
				"a mesh root holding only an outgoing model has nothing to animate");
		}
	}
}
