using System;
using NUnit.Framework;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// A pet announcement binds the live pet, never a pooled copy still registered under its ID (2026-11-17).
	/// </summary>
	/// <remarks>
	/// Clients unregister scene objects on destroy, not on despawn, and the server keeps a re-spawned pet's ID. After any
	/// despawn the announcement could resolve to the pooled copy: the pet panel drew its frozen full health while the
	/// target frame showed the live pet at 70%.
	/// </remarks>
	[TestFixture]
	public class PetAddLivenessTests
	{
		private const string PetControllerPath = "Assets/FishMMO/Shared/Implementation/Entity/NPC/Pet/PetController.cs";

		[Test]
		public void TheAnnouncementResolvesOnlyALivePet_AndReplacesAStaleEntry()
		{
			string code = ProjectFiles.ReadAllText(PetControllerPath).Replace("\r\n", "\n");
			int handler = code.IndexOf("public void OnClientPetAddBroadcastReceived(", StringComparison.Ordinal);
			int found = code.IndexOf("SceneObject.Objects.TryGetValue(msg.ID", handler, StringComparison.Ordinal);
			int live = code.IndexOf("IsLive(pet))", found, StringComparison.Ordinal);
			int apply = code.IndexOf("ApplyPetAdd(msg, pet);", found, StringComparison.Ordinal);
			LogAssert.IsTrue(found > handler && live > found && apply > live,
				"a registered object is the pet only when it is live; a pooled copy waits for the live one's registration");
			LogAssert.IsTrue(code.Contains("pet.NetworkObject.IsSpawned") && code.Contains("activeInHierarchy"),
				"live means spawned and active");

			int add = code.IndexOf("private void ApplyPetAdd(", StringComparison.Ordinal);
			int replace = code.IndexOf("pets[i].ID == msg.ID", add, StringComparison.Ordinal);
			int addPet = code.IndexOf("AddPet(pet);", add, StringComparison.Ordinal);
			LogAssert.IsTrue(replace > add && addPet > replace, "a stale entry with the same ID is removed before the live pet is listed");
		}
	}
}
