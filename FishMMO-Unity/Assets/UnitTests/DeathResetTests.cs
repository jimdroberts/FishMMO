using System;
using System.IO;
using NUnit.Framework;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Death is a full reset. These pin the three parts of that rule which were not true before
	/// the 2026-09-06 death audit.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Cooldowns.</b> They used to survive death. Now <c>Kill</c> clears them on the server,
	/// and the owner's next cooldown reconcile removes them from the hotkey bar. Resurrecting
	/// can therefore wipe a long cooldown; the cost of dying is what discourages that.
	/// </para>
	/// <para>
	/// <b>Pets.</b> A summon used to outlive its owner, leashed to a corpse and still fighting.
	/// The pet system now dismisses it from the kill event through the same path a voluntary
	/// release takes, so it persists and notifies identically.
	/// </para>
	/// <para>
	/// <b>Gravity.</b> The KCC movement gate used to return before the motor step, so a character
	/// killed or stunned in the air hung there until it revived. The gate now suppresses the
	/// controls and lets the motor run, so the body falls and lands.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class DeathResetTests
	{
		private static string Scripts =>
			Path.Combine(Directory.GetCurrentDirectory(), "Assets/Scripts");

		[Test]
		public void KillClearsCooldowns()
		{
			string damage = File.ReadAllText(Path.Combine(Scripts,
				"Shared/Implementation/Entity/Prediction/CharacterAttribute/CharacterDamageController.cs"));

			int kill = damage.IndexOf("public void Kill(ICharacter killer)", StringComparison.Ordinal);
			int clear = damage.IndexOf("cooldowns.Clear();", kill, StringComparison.Ordinal);
			int broadcast = damage.IndexOf("BroadcastDeathState(true);", kill, StringComparison.Ordinal);

			LogAssert.IsTrue(kill >= 0 && clear > kill && clear < broadcast,
				"Kill must clear the character's cooldowns on the server before it announces the death");
		}

		[Test]
		public void OwnerDeathDismissesThePet()
		{
			string pets = File.ReadAllText(Path.Combine(Scripts,
				"Server/Implementation/World/SceneServer/Pet/PetSystem.cs"));

			LogAssert.IsTrue(pets.Contains("ICharacterDamageController.OnKilled += DamageController_OnKilled;"),
				"PetSystem must subscribe to the kill event");
			LogAssert.IsTrue(pets.Contains("ICharacterDamageController.OnKilled -= DamageController_OnKilled;"),
				"PetSystem must unsubscribe from the kill event on shutdown");

			int handler = pets.IndexOf("private void DamageController_OnKilled(ICharacter killer, ICharacter victim)", StringComparison.Ordinal);
			int dismiss = pets.IndexOf("DismissPet(owner, petController, conn);", handler, StringComparison.Ordinal);
			LogAssert.IsTrue(handler >= 0 && dismiss > handler,
				"a dead owner's pet must go through the shared DismissPet path");

			int release = pets.IndexOf("private void OnPetReleaseBroadcastReceived(", StringComparison.Ordinal);
			int releaseDismiss = pets.IndexOf("DismissPet(player, petController, conn);", release, StringComparison.Ordinal);
			LogAssert.IsTrue(release >= 0 && releaseDismiss > release,
				"the voluntary release must use the same DismissPet path, so the two cannot drift apart");
		}

		[Test]
		public void DeadCharacterStillFalls()
		{
			string player = File.ReadAllText(Path.Combine(Scripts,
				"Shared/Implementation/Entity/Prediction/KCC/KCCPlayer.cs"));

			int gate = player.IndexOf("IsHealthDepleted(character))", StringComparison.Ordinal);
			int gateBody = player.IndexOf("controlsSuppressed = true;", gate, StringComparison.Ordinal);
			int setInputs = player.IndexOf("CharacterController.SetInputs(ref kccInput, controlsSuppressed);", StringComparison.Ordinal);

			LogAssert.IsTrue(gate >= 0 && gateBody > gate && gateBody - gate < 200,
				"the death/incapacitation gate must suppress controls rather than return");
			LogAssert.IsTrue(setInputs > gateBody,
				"the suppressed input must still reach the controller so the motor step runs");

			string controller = File.ReadAllText(Path.Combine(Scripts,
				"Shared/Implementation/Entity/Prediction/KCC/KCCController.cs"));
			int suppressed = controller.IndexOf("if (controlsSuppressed)", StringComparison.Ordinal);
			LogAssert.IsTrue(suppressed >= 0 &&
				controller.IndexOf("moveInputVector = Vector3.zero;", suppressed, StringComparison.Ordinal) > suppressed &&
				controller.IndexOf("jumpRequested = false;", suppressed, StringComparison.Ordinal) > suppressed,
				"suppressed controls must zero the move vector and forget any pending jump");
		}
	}
}
