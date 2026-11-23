using System;
using NUnit.Framework;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// A pet announcement binds the live pet, never a pooled copy still registered under its ID.
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
	

		/// <summary>
		/// A pet dropped from its owner's view and sent again arrives as a new client object with no announcement; the list
		/// and the panel follow it (panel frozen at full while the pinned target frame showed 65%).
		/// </summary>
		[Test]
		public void ARespawnedPet_ReplacesTheListedObject_AndThePanelRebinds()
		{
			string code = ProjectFiles.ReadAllText(PetControllerPath).Replace("\r\n", "\n");
			int start = code.IndexOf("public override void OnStartCharacter()", StringComparison.Ordinal);
			LogAssert.IsTrue(code.IndexOf("SceneObject.Registered += SceneObject_PetRespawned;", start, StringComparison.Ordinal) > start,
				"the owner watches registrations for its pets");
			int handler = code.IndexOf("private void SceneObject_PetRespawned(", StringComparison.Ordinal);
			int find = code.IndexOf("FindPet(sceneObject.ID)", handler, StringComparison.Ordinal);
			int same = code.IndexOf("ReferenceEquals(listed, arrived)", handler, StringComparison.Ordinal);
			int remove = code.IndexOf("RemovePet(listed);", handler, StringComparison.Ordinal);
			int addPet = code.IndexOf("AddPet(arrived);", handler, StringComparison.Ordinal);
			int notify = code.IndexOf("IPetController.OnPetSummoned?.Invoke(arrived);", handler, StringComparison.Ordinal);
			LogAssert.IsTrue(handler > 0 && find > handler && same > find && remove > same && addPet > remove && notify > addPet,
				"a listed pet's new object replaces the old one and the panel is told");
			LogAssert.IsTrue(code.Contains("arrived.Slot = listed.Slot;"), "it keeps its slot (the panel orders rows by it)");
		}

		/// <summary>The server keeps a player's own pets in their view whatever the crowd (the observer budget's pins).</summary>
		[Test]
		public void APlayersOwnPets_ArePinnedInTheirObserverBudget()
		{
			string code = SourceScanPins.ReadCode("Assets/FishMMO/Shared/Implementation/Entity/Prediction/ObserverStreaming/ObserverStreamingRegistry.cs");
			LogAssert.IsTrue(code.Contains("observed.Character is NPC companion") && code.Contains("companion.CompanionOwner.ID == viewer.Character.ID"),
				"a pet whose owner is the viewer");
			LogAssert.IsTrue(code.Contains("engagedWith ||\n\t\t\t\townPet;") || code.Contains("ownPet;"), "is pinned");
		}
	}
}
