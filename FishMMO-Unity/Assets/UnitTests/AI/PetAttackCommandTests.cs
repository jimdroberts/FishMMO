using System;
using System.IO;
using NUnit.Framework;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests.AI
{
	/// <summary>
	/// The pet attack button sends the pet at the owner's target frame.
	/// </summary>
	/// <remarks>
	/// The button used to send the pet at whatever a raycast down the camera centre hit, which
	/// is not the character in the target card. Now the click names its frame target, the server
	/// verifies that claim (spawned, same scene, in range, and every pet-target rule), falls back
	/// to its own copy of the reported frame, and only then to the NPC holding the most threat
	/// against the owner. The priority is pinned, then current, then highest threat.
	/// </remarks>
	[TestFixture]
	public class PetAttackCommandTests
	{
		private static string Scripts =>
			Path.Combine(Directory.GetCurrentDirectory(), "Assets/Scripts");

		[Test]
		public void TheClickNamesTheTargetFrame()
		{
			string message = File.ReadAllText(Path.Combine(Scripts, "Shared/Implementation/Network/Character/PetBroadcasts.cs"));
			int attack = message.IndexOf("public struct PetAttackBroadcast", StringComparison.Ordinal);
			int pinnedField = message.IndexOf("public int PinnedTargetObjectID;", attack, StringComparison.Ordinal);
			int hoveredField = message.IndexOf("public int HoveredTargetObjectID;", attack, StringComparison.Ordinal);
			int next = message.IndexOf("public struct ", attack + 1, StringComparison.Ordinal);
			LogAssert.IsTrue(attack >= 0 && pinnedField > attack && hoveredField > attack && (next < 0 || hoveredField < next),
				"PetAttackBroadcast must carry the owner's pinned and hovered targets separately, so any priority order can be honoured");

			string panel = File.ReadAllText(Path.Combine(Scripts, "Client/GUI/World/Pet/UITKPetControl.cs"));
			LogAssert.IsTrue(panel.Contains("pinned = ResolveCharacterObjectId(targetController.PinnedTarget);") &&
				panel.Contains("hovered = ResolveCharacterObjectId(targetController.Current.Target);") &&
				panel.Contains("PinnedTargetObjectID = pinned,") && panel.Contains("HoveredTargetObjectID = hovered,"),
				"the attack button must fill both frame ids");
		}

		[Test]
		public void TheServerWalksTheOwnersOrderAndVerifiesEveryStep()
		{
			string pets = File.ReadAllText(Path.Combine(Scripts, "Server/Implementation/World/SceneServer/Pet/PetSystem.cs"));

			int handler = pets.IndexOf("private void OnPetAttackBroadcastReceived(", StringComparison.Ordinal);
			int nextHandler = pets.IndexOf("private void OnPetAttackPriorityBroadcastReceived(", handler, StringComparison.Ordinal);
			int decode = pets.IndexOf("PetAttackPriority.TryDecode(petController.AttackPriority, order)", handler, StringComparison.Ordinal);
			int pinned = pets.IndexOf("TryResolveFrameTarget(player, msg.PinnedTargetObjectID, out candidate);", handler, StringComparison.Ordinal);
			int hovered = pets.IndexOf("TryResolveFrameTarget(player, msg.HoveredTargetObjectID, out candidate)", handler, StringComparison.Ordinal);
			int serverCopy = pets.IndexOf("TryResolveFrameTarget(player, targetController.ClientSelectedTargetObjectId, out candidate);", handler, StringComparison.Ordinal);
			int threat = pets.IndexOf("AggressionDispatcher.TryFindHighestThreatAgainst(player,", handler, StringComparison.Ordinal);
			int validated = pets.IndexOf("if (candidate != null && IsValidPetTarget(petController, player, candidate))", handler, StringComparison.Ordinal);
			int raycast = pets.IndexOf("targetController.UpdateTarget(", handler, StringComparison.Ordinal);

			LogAssert.IsTrue(handler >= 0 && nextHandler > handler && decode > handler && decode < nextHandler,
				"the handler must walk the owner's attack priority");
			LogAssert.IsTrue(pinned > decode && hovered > decode && serverCopy > hovered && threat > decode &&
				pinned < nextHandler && threat < nextHandler,
				"every step — pinned, current (click then server copy), highest threat — must be resolvable in any order");
			LogAssert.IsTrue(validated > decode && validated < nextHandler,
				"whatever a step produces must still pass the pet-target rules before it wins");
			LogAssert.IsTrue(raycast < 0 || raycast > nextHandler, "there is no camera raycast in the attack command");

			int resolver = pets.IndexOf("private static bool TryResolveFrameTarget(", StringComparison.Ordinal);
			LogAssert.IsTrue(resolver >= 0 &&
				pets.IndexOf("ServerManager.Objects.Spawned.TryGetValue(objectId", resolver, StringComparison.Ordinal) > resolver &&
				pets.IndexOf("targetObject.gameObject.scene != owner.GameObject.scene", resolver, StringComparison.Ordinal) > resolver &&
				pets.IndexOf("TargetController.MAX_TARGET_DISTANCE", resolver, StringComparison.Ordinal) > resolver,
				"a claimed id must resolve to a spawned object in the owner's scene within targeting range");
		}

		[Test]
		public void HighestThreatAgainstTheOwnerWins()
		{
			FishMMO.Shared.AggressionDispatcher.Clear();
			try
			{
				FishMMO.UnitTests.Harness.StubCharacter owner = new FishMMO.UnitTests.Harness.StubCharacter { ID = 1 };
				FishMMO.UnitTests.Harness.StubCharacter mild = new FishMMO.UnitTests.Harness.StubCharacter { ID = 10 };
				FishMMO.UnitTests.Harness.StubCharacter furious = new FishMMO.UnitTests.Harness.StubCharacter { ID = 11 };
				FishMMO.UnitTests.Harness.StubCharacter indifferent = new FishMMO.UnitTests.Harness.StubCharacter { ID = 12 };

				FishMMO.Shared.AggressionState mildState = new FishMMO.Shared.AggressionState(mild);
				FishMMO.Shared.AggressionState furiousState = new FishMMO.Shared.AggressionState(furious);
				FishMMO.Shared.AggressionState indifferentState = new FishMMO.Shared.AggressionState(indifferent);
				mildState.Controller.RecordDamage(owner.ID, 5);
				furiousState.Controller.RecordDamage(owner.ID, 50);
				indifferentState.Controller.RecordDamage(99, 500);

				bool found = FishMMO.Shared.AggressionDispatcher.TryFindHighestThreatAgainst(owner, null, out FishMMO.Shared.Core.ICharacter best);
				LogAssert.IsTrue(found && ReferenceEquals(best, furious),
					"the NPC the owner has attacked the most must win, and one that only hates someone else must not be considered");

				bool filtered = FishMMO.Shared.AggressionDispatcher.TryFindHighestThreatAgainst(owner, c => !ReferenceEquals(c, furious), out best);
				LogAssert.IsTrue(filtered && ReferenceEquals(best, mild),
					"a candidate the caller's rule refuses must yield to the next highest");

				LogAssert.IsTrue(!FishMMO.Shared.AggressionDispatcher.TryFindHighestThreatAgainst(new FishMMO.UnitTests.Harness.StubCharacter { ID = 2 }, null, out _),
					"a character nobody hates resolves nothing");
			}
			finally
			{
				FishMMO.Shared.AggressionDispatcher.Clear();
			}
		}
	}
}
