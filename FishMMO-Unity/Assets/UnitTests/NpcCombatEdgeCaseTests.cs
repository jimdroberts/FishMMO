using System;
using System.IO;
using NUnit.Framework;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Edge cases found tracing issue #232 end to end: the AI, target and ability systems, and
	/// the death sequences on both sides.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Stale NPC target.</b> <c>CanActivateOptimistic</c> pre-filters an activation against
	/// the target controller's <c>Current</c>, which for a player is the mouse hover. For an NPC it
	/// is whatever the previous cast's acquisition trace hit, and only a successful cast rewrites
	/// it — so once a ranged NPC's old victim was out of reach, every activation at its new target
	/// was refused with nothing able to clear the refusal. The pre-filter is player-only now.
	/// </para>
	/// <para>
	/// <b>Corpses in the sweep.</b> A dead player stays spawned until it respawns; a dead NPC stays
	/// through its decay. The enemy sweep returned both, the attacking state's pickers refused
	/// them, and the brain dropped back to idle — once per sweep for as long as the body lay there.
	/// </para>
	/// <para>
	/// <b>Immortal brain.</b> An immortal NPC has no reason to target anything. The trace
	/// short-circuits for it (see <see cref="NpcTargetControllerTests"/>) and so does acquisition:
	/// neither the sweep nor an incoming hit gives it a target. Acquisition only, so a boss that
	/// turns immortal for a phase mid-fight keeps fighting.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class NpcCombatEdgeCaseTests
	{
		private static string Scripts =>
			Path.Combine(Directory.GetCurrentDirectory(), "Assets/Scripts/Shared");

		[Test]
		public void TargetRangePreFilterIsPlayerOnly()
		{
			string controller = File.ReadAllText(Path.Combine(Scripts,
				"Implementation/Entity/Prediction/Ability/AbilityController.Activation.cs"));

			LogAssert.IsTrue(controller.Contains("if (cachedTargetController != null && PlayerCharacter != null)"),
				"the target/range pre-filter must read the target controller only for a player; " +
				"an NPC's Current is the previous cast's stale trace");
		}

		[Test]
		public void EnemySweepSkipsTheDead()
		{
			string state = File.ReadAllText(Path.Combine(Scripts,
				"Implementation/Entity/NPC/AI/BaseAIState.cs"));

			int sweep = state.IndexOf("public virtual bool SweepForEnemies(", StringComparison.Ordinal);
			int alive = state.IndexOf("if (!AITargetSelection.IsValidTarget(def))", StringComparison.Ordinal);
			int faction = state.IndexOf("defenderFactionController.GetAllianceLevel(ourFactionController) == FactionAllianceLevel.Enemy", StringComparison.Ordinal);

			LogAssert.IsTrue(sweep >= 0 && alive > sweep, "SweepForEnemies must drop dead and inactive characters");
			LogAssert.IsTrue(faction > alive, "the alive check must come before the faction test, so a corpse never reaches the enemy list");
		}

		/// <summary>
		/// A dead NPC gives back its combat slot immediately, not at pool reset.
		/// </summary>
		[Test]
		public void DeadNpcReleasesItsCombatSlot()
		{
			string controller = File.ReadAllText(Path.Combine(Scripts,
				"Implementation/Entity/NPC/AI/AIController.cs"));

			int halt = controller.IndexOf("public void HaltMovement()", StringComparison.Ordinal);
			int release = controller.IndexOf("ReleaseCombatSlots();", halt, StringComparison.Ordinal);
			// HaltMovement is the last member of the file today; bound on the end when nothing follows.
			int nextMethod = controller.IndexOf("\n\t\tpublic ", halt + 1, StringComparison.Ordinal);
			if (nextMethod < 0)
			{
				nextMethod = controller.Length;
			}

			LogAssert.IsTrue(halt >= 0 && release > halt && release < nextMethod,
				"HaltMovement runs on the corpse path in place of the attacking state's Exit, so it must release the combat slot itself");

			string npc = File.ReadAllText(Path.Combine(Scripts, "Implementation/Entity/NPC/NPC.cs"));
			LogAssert.IsTrue(npc.Contains("ai.HaltMovement();"), "NPC.Despawn must halt the brain through HaltMovement");
		}

		[Test]
		public void ImmortalNpcAcquiresNoTarget()
		{
			string controller = File.ReadAllText(Path.Combine(Scripts,
				"Implementation/Entity/NPC/AI/AIController.cs"));

			int threat = controller.IndexOf("public void OnThreatReceived(ICharacter attacker)", StringComparison.Ordinal);
			int threatGuard = controller.IndexOf("if (IsImmortal)", threat, StringComparison.Ordinal);
			int threatAssign = controller.IndexOf("Target = attacker.Transform;", threat, StringComparison.Ordinal);

			LogAssert.IsTrue(threat >= 0 && threatGuard > threat && threatGuard < threatAssign,
				"OnThreatReceived must refuse a target for an immortal NPC before assigning one");

			int sweep = controller.IndexOf("private void SweepForEnemies(float deltaTime)", StringComparison.Ordinal);
			int sweepGuard = controller.IndexOf("if (IsImmortal)", sweep, StringComparison.Ordinal);
			int sweepCall = controller.IndexOf("AttackingState.SweepForEnemies(this, sweepResults)", sweep, StringComparison.Ordinal);

			LogAssert.IsTrue(sweep >= 0 && sweepGuard > sweep && sweepGuard < sweepCall,
				"the enemy sweep must not run for an immortal NPC");
		}
	}
}
