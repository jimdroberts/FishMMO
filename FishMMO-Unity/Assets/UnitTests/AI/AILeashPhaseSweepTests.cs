using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using FishMMO.Shared;
using FishMMO.Server.Implementation.World.SceneServer.AI;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests.AI
{
	/// <summary>
	/// Pins four follow-ups to the leash evade (hot-path audit H9): the full evade on a live brain
	/// (O2), the heal that belongs to the leash rather than the return state (O12), the boss phase
	/// attacking state installed when the phase starts (O13), and the enemy sweep that stays out of
	/// every combat sub-state (O14).
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>O12.</b> <c>ReturnHomeState</c> is both the leash's way home and one of the calm movement
	/// states, and it healed on <c>Enter</c> — so a damaged NPC that merely strolled home between
	/// fights was topped up to full. The heal now lives in the leash check, the only place that
	/// knows a leash sent the NPC.
	/// </para>
	/// <para>
	/// <b>O13 and O14 are one defect seen from both ends.</b> A phase's attacking-state override was
	/// read by <c>AttackingState</c> but only took effect when something re-entered an attacking
	/// state, and the thing that usually did was the enemy sweep — running, wrongly, whenever the
	/// NPC was in a combat sub-state, where it cut orbits and flees short. The sweep now stays out
	/// of every state that keeps the combat target, and the phase hands the fight over itself.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class AILeashPhaseSweepTests
	{
		private const string ORC = "Assets/Prefabs/Shared/Entity/NPCs/Monsters/Orcs/an orc.prefab";
		private const string AI_ROOT = "Assets/Scripts/Server/Implementation/World/SceneServer/AI/";

		private readonly List<UnityEngine.Object> created = new List<UnityEngine.Object>();

		[TearDown]
		public void DestroyCreated()
		{
			foreach (UnityEngine.Object o in created)
			{
				if (o != null)
				{
					UnityEngine.Object.DestroyImmediate(o);
				}
			}
			created.Clear();
			AggressionDispatcher.Clear();
			UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;
		}

		// --- The rules, as truth tables ------------------------------------------------------------

		[Test]
		public void OnlyALeashThatReallyEnteredTheReturn_Heals_AndOnlyWhenTheArchetypeSaysSo()
		{
			// enteredReturn, completeHealOnReturn -> heal
			Assert.IsTrue(AIController.LeashReturnHeals(true, true));
			Assert.IsFalse(AIController.LeashReturnHeals(false, true), "a transition that declined is not a return");
			Assert.IsFalse(AIController.LeashReturnHeals(true, false), "the archetype's return state can opt out");
			Assert.IsFalse(AIController.LeashReturnHeals(false, false));
		}

		[Test]
		public void TheSweep_RunsOnlyForACalmNpcThatCanFight()
		{
			// hasAttackingState, returningHome, inCombatState -> sweep
			Assert.IsTrue(AIController.SweepMayRun(true, false, false), "idle, wandering, patrolling");
			Assert.IsFalse(AIController.SweepMayRun(true, false, true),
				"attacking, orbiting, flanking or fleeing: a fight has its own target and re-evaluation");
			Assert.IsFalse(AIController.SweepMayRun(true, true, false), "going home");
			Assert.IsFalse(AIController.SweepMayRun(false, false, false), "an NPC that cannot fight has nothing to look for");
		}

		[Test]
		public void AFight_IsHandedOver_OnlyFromAnAttackingStateThatIsNoLongerTheOneInForce()
		{
			// hasAttackingState, inAttackingState, inResolvedAttackingState -> hand over
			Assert.IsTrue(AIController.FightNeedsHandOver(true, true, false), "a phase replaced the attacking state mid-fight");
			Assert.IsFalse(AIController.FightNeedsHandOver(true, true, true), "already fighting with the right one");
			Assert.IsFalse(AIController.FightNeedsHandOver(true, false, false),
				"a sub-state or a calm NPC picks the override up when it next enters the attacking state");
			Assert.IsFalse(AIController.FightNeedsHandOver(false, true, false));
		}

		// --- On a real brain -----------------------------------------------------------------------

		/// <summary>
		/// A phase's attacking state takes over the fight the moment the phase installs it, keeping
		/// the target; a plain transition between attacking states is still a disengage.
		/// </summary>
		[Test]
		public void APhaseAttackingState_TakesOverTheFightAtOnce_KeepingTheTarget()
		{
			UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;

			AIController brain = PrepareOrc(out _, out _);
			Assert.IsNotNull(brain.AttackingState, "the orc's archetype must fight for this test to mean anything");
			BaseAIState archetypeAttacking = brain.AttackingState;

			GameObject victim = new GameObject("PhaseVictim");
			created.Add(victim);

			brain.ChangeState(archetypeAttacking);
			brain.Target = victim.transform;

			MeleeAttackingState phaseAttacking = ScriptableObject.CreateInstance<MeleeAttackingState>();
			created.Add(phaseAttacking);
			brain.SetPhaseOverrides(phaseAttacking, null, null);

			Assert.IsTrue(ReferenceEquals(brain.CurrentState, phaseAttacking), "the phase's attacking state is fighting now, not on the next re-entry");
			Assert.IsTrue(ReferenceEquals(brain.Target, victim.transform), "the hand-over keeps the target");
			Assert.IsFalse(brain.IsHandingOverFight, "the hand-over mark lasts only for the transition");

			// Control: an ordinary transition between attacking states is a disengage, which is
			// exactly why the phase needs a hand-over rather than a ChangeState.
			brain.ChangeState(archetypeAttacking);
			Assert.IsNull(brain.Target, "without the hand-over the outgoing state drops the target");
		}

		/// <summary>
		/// A phase that starts while the boss is out of its attacking state changes nothing now; the
		/// override is simply what the next engagement enters.
		/// </summary>
		[Test]
		public void APhaseAttackingState_LeavesACalmOrManoeuvringNpcWhereItIs()
		{
			UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;

			AIController brain = PrepareOrc(out _, out _);
			BaseAIState idle = brain.CurrentState;
			Assert.IsFalse(brain.CurrentState is BaseAttackingState, "precondition: the orc starts calm");

			MeleeAttackingState phaseAttacking = ScriptableObject.CreateInstance<MeleeAttackingState>();
			created.Add(phaseAttacking);
			brain.SetPhaseOverrides(phaseAttacking, null, null);

			Assert.IsTrue(ReferenceEquals(brain.CurrentState, idle), "a calm boss is not pulled into a fight by its own phase");
			Assert.IsTrue(ReferenceEquals(brain.AttackingState, phaseAttacking), "but its next fight uses the phase's state");

			// A combat sub-state returns to AttackingState on its own, so it is left to finish.
			GameObject victim = new GameObject("OrbitVictim");
			created.Add(victim);
			OrbitState orbit = ScriptableObject.CreateInstance<OrbitState>();
			orbit.KeepsCombatTarget = true;
			created.Add(orbit);

			brain.Target = victim.transform;
			brain.ChangeState(orbit);
			Assert.IsTrue(ReferenceEquals(brain.CurrentState, orbit), "precondition: orbiting");

			MeleeAttackingState laterPhase = ScriptableObject.CreateInstance<MeleeAttackingState>();
			created.Add(laterPhase);
			brain.SetPhaseOverrides(laterPhase, null, null);
			Assert.IsTrue(ReferenceEquals(brain.CurrentState, orbit), "the orbit is not cut short");
			Assert.IsTrue(ReferenceEquals(brain.AttackingState, laterPhase));
		}

		/// <summary>
		/// On a live evading brain the shared gate refuses a debuff from anyone else, and stops the
		/// moment the evade does.
		/// </summary>
		[Test]
		public void AnEvadingNpc_RefusesDebuffsFromOthers_UntilTheEvadeEnds()
		{
			UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;

			AIController brain = PrepareOrc(out _, out NPC npc);
			Harness.StubCharacter attacker = new Harness.StubCharacter { ID = 4242 };

			StateBuffTemplate stun = ScriptableObject.CreateInstance<StateBuffTemplate>();
			stun.Flag = CharacterFlags.IsStunned;
			stun.IsDebuff = true;
			created.Add(stun);
			AttributeBuffTemplate blessing = ScriptableObject.CreateInstance<AttributeBuffTemplate>();
			blessing.IsDebuff = false;
			created.Add(blessing);

			Assert.IsFalse(CharacterEvade.RefusesHostileEffects(npc), "a calm NPC refuses nothing");
			Assert.IsFalse(CharacterEvade.RefusesBuff(npc, stun, attacker));

			brain.ChangeState(brain.ReturnHomeState);
			BeginLeashEvade(brain);
			Assert.IsTrue(brain.IsEvading, "precondition: evading");

			Assert.IsTrue(CharacterEvade.RefusesHostileEffects(npc), "no damage, no knockback while evading");
			Assert.IsTrue(CharacterEvade.RefusesBuff(npc, stun, attacker), "no debuff from an attacker while evading");
			Assert.IsFalse(CharacterEvade.RefusesBuff(npc, blessing, attacker), "a buff is not hostile");
			Assert.IsFalse(CharacterEvade.RefusesBuff(npc, stun, npc), "its own debuff on itself is its own business");
			Assert.IsFalse(CharacterEvade.RefusesBuff(npc, stun, null), "an effect with no caster is not an attack");

			brain.CompleteReturnHome();
			Assert.IsFalse(CharacterEvade.RefusesHostileEffects(npc), "home: attackable again");
			Assert.IsFalse(CharacterEvade.RefusesBuff(npc, stun, attacker));
		}

		// --- The wiring, by source -----------------------------------------------------------------

		[Test]
		public void TheHeal_IsTheLeashs_AndACalmStrollHomeStaysACalmMovement()
		{
			string home = CodeOnly(Read(AI_ROOT + "States/ReturnHomeState.cs"));
			LogAssert.IsFalse(home.Contains("CompleteHeal()"),
				"the return state must not heal: it is also a calm movement state, and cannot tell why it was entered");
			LogAssert.IsTrue(home.Contains("public bool CompleteHealOnReturn"), "the authored flag stays on the asset");

			string brain = CodeOnly(Read(AI_ROOT + "AIController.cs"));
			string leash = MethodBody(brain, "private void CheckLeash(float deltaTime)");
			int enter = leash.IndexOf("ChangeState(ReturnHomeState);", StringComparison.Ordinal);
			int rule = leash.IndexOf("LeashReturnHeals(returning,", StringComparison.Ordinal);
			int heal = leash.IndexOf("returningDamageController.CompleteHeal();", StringComparison.Ordinal);
			LogAssert.IsTrue(enter >= 0 && rule > enter && heal > rule,
				"the walk-home leash heals through the pinned rule, after the return is in force");

			string random = MethodBody(brain, "public virtual void TransitionToRandomMovementState()");
			LogAssert.IsTrue(random.Contains("movementStates.Add(ReturnHomeState);"),
				"strolling home is still one of the calm movements — it just no longer heals");
		}

		[Test]
		public void TheSweep_AsksTheCombatStateRule_AndTheHandOverSparesTheTarget()
		{
			string brain = CodeOnly(Read(AI_ROOT + "AIController.cs"));
			string sweep = MethodBody(brain, "private void SweepForEnemies(float deltaTime)");
			LogAssert.IsTrue(sweep.Contains("SweepMayRun(") && sweep.Contains("IsInCombatState"),
				"the sweep must stay out of every state that keeps the combat target");
			LogAssert.IsFalse(sweep.Contains("CurrentState == AttackingState"),
				"testing the attacking state alone let the sweep cut orbits and flees short");

			string overrides = MethodBody(brain, "public void SetPhaseOverrides(");
			LogAssert.IsTrue(overrides.Contains("FightNeedsHandOver(") && overrides.Contains("HandOverFight();"),
				"a phase's attacking state is installed when the phase starts");

			string attacking = CodeOnly(Read(AI_ROOT + "States/BaseAttackingState.cs"));
			string exit = MethodBody(attacking, "public override void Exit(AIController controller)");
			int spared = exit.IndexOf("controller.IsHandingOverFight", StringComparison.Ordinal);
			int dropped = exit.IndexOf("controller.Target = null;", StringComparison.Ordinal);
			LogAssert.IsTrue(spared >= 0 && dropped > spared,
				"a hand-over returns before the target is dropped and the cast interrupted");
		}

		// --- Helpers -------------------------------------------------------------------------------

		/// <summary>Sets the flag <c>CheckLeash</c> sets when a leash ends a fight.</summary>
		private static void BeginLeashEvade(AIController brain)
		{
			typeof(AIController)
				.GetField("leashEvade", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
				.SetValue(brain, true);
		}

		/// <summary>
		/// An edit-mode orc, prepared by a host, with the references <c>BaseCharacter.Awake</c>
		/// would have set.
		/// </summary>
		private AIController PrepareOrc(out AIBrainHost host, out NPC npc)
		{
			GameObject instance = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(ORC));
			created.Add(instance);
			npc = instance.GetComponent<NPC>();
			System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public;
			typeof(BaseCharacter).GetProperty(nameof(BaseCharacter.GameObject), flags).SetValue(npc, instance);
			typeof(BaseCharacter).GetProperty(nameof(BaseCharacter.Transform), flags).SetValue(npc, instance.transform);

			AIBrainCatalogue catalogue = AssetDatabase.LoadAssetAtPath<AIBrainCatalogue>(BrainCatalogueYaml.CataloguePath);
			Assert.IsNotNull(catalogue, $"no brain catalogue at {BrainCatalogueYaml.CataloguePath}");
			catalogue.Invalidate();

			host = new AIBrainHost(null, catalogue);
			AIController brain = host.Prepare(npc, Vector3.zero);
			Assert.IsNotNull(brain);
			Assert.IsTrue(brain.IsRunning);
			return brain;
		}

		private static string Read(string relativePath)
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), relativePath);
			LogAssert.IsTrue(File.Exists(path), $"{relativePath} not found at {path}.");
			return File.ReadAllText(path).Replace("\r\n", "\n");
		}

		/// <summary>Drops comment lines, so prose about a construct does not satisfy a scan for it.</summary>
		private static string CodeOnly(string source)
		{
			System.Text.StringBuilder code = new System.Text.StringBuilder(source.Length);
			foreach (string line in source.Split('\n'))
			{
				string trimmed = line.TrimStart();
				if (trimmed.StartsWith("//") || trimmed.StartsWith("/*") || trimmed.StartsWith("*"))
				{
					continue;
				}
				code.Append(line).Append('\n');
			}
			return code.ToString();
		}

		/// <summary>The brace-matched body that follows <paramref name="signature"/>.</summary>
		private static string MethodBody(string source, string signature)
		{
			int start = source.IndexOf(signature, StringComparison.Ordinal);
			LogAssert.IsTrue(start >= 0, $"signature not found: {signature}");
			int open = source.IndexOf('{', start);
			int depth = 0;
			for (int i = open; i < source.Length; ++i)
			{
				if (source[i] == '{')
				{
					++depth;
				}
				else if (source[i] == '}' && --depth == 0)
				{
					return source.Substring(open, i - open + 1);
				}
			}
			LogAssert.Fail($"unbalanced body after: {signature}");
			return string.Empty;
		}
	}
}
