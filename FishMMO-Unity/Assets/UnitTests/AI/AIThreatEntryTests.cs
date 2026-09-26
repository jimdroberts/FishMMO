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
	/// Pins how an NPC enters combat from a hit and how it evades on a leash return (hot-path
	/// audit, 2026-09-25: H9, L10, L11, L12).
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>The defect.</b> Combat used to start only when the threat table went from empty to
	/// non-empty, and nothing but a kill emptied it. Hit a mob once while it walked home and it
	/// wandered with a non-empty table; every later hit from beyond its detection radius was
	/// recorded and ignored, and each one refreshed the entry that kept it from going stale.
	/// </para>
	/// <para>
	/// <b>The rule now.</b> Every hit asks the brain, and the brain answers from its own state:
	/// engage unless already fighting or evading. The table is emptied when a fight ends and when
	/// the NPC gets home. And a leash that ends a fight makes the walk home an evade — no damage,
	/// no threat — held by the brain and ANDed with "the return is still the current state", so no
	/// way out of the return can leave a mob immune.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class AIThreatEntryTests
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
		public void ThreatStartsAFight_OnlyForARunningBrainThatIsNeitherFightingNorEvading()
		{
			// hasAttackingState, running, inCombatState, evading -> engage
			Assert.IsTrue(AIController.CanEnterCombatFromThreat(true, true, false, false),
				"an idle, wandering or strolling-home NPC answers a hit — however full its table");
			Assert.IsFalse(AIController.CanEnterCombatFromThreat(true, true, true, false),
				"already fighting, orbiting or fleeing: a hit is part of that fight, not a new one");
			Assert.IsFalse(AIController.CanEnterCombatFromThreat(true, true, false, true),
				"an evading NPC cannot be pulled back into the fight it leashed out of");
			Assert.IsFalse(AIController.CanEnterCombatFromThreat(false, true, false, false),
				"an NPC with no attacking state never fights");
			Assert.IsFalse(AIController.CanEnterCombatFromThreat(true, false, false, false),
				"a corpse, an unprepared or a quarantined brain does nothing");
		}

		[Test]
		public void AnNpcEvades_OnlyWhileAFightEndingLeashReturnIsItsCurrentState()
		{
			// leashEvade, running, inReturnHomeState -> evading
			Assert.IsTrue(AIController.IsEvadingRule(true, true, true));
			Assert.IsFalse(AIController.IsEvadingRule(false, true, true),
				"a calm walk home — ReturnHome picked as a random movement state — is attackable");
			Assert.IsFalse(AIController.IsEvadingRule(true, true, false),
				"once the return is no longer the current state the evade is over, however it ended");
			Assert.IsFalse(AIController.IsEvadingRule(true, false, true),
				"a stopped brain (corpse, quarantine) never leaves its NPC immune");
		}

		[Test]
		public void OnlyALeashThatEndsAFight_StartsAnEvade()
		{
			// isPet, inCombatState, heldThreat -> evade
			Assert.IsTrue(AIController.LeashStartsEvade(false, true, false));
			Assert.IsTrue(AIController.LeashStartsEvade(false, false, true),
				"threat still held counts: a fight that had just left the attacking state is still a fight");
			Assert.IsFalse(AIController.LeashStartsEvade(false, false, false),
				"a calm NPC that drifted past its leash walks home attackable");
			Assert.IsFalse(AIController.LeashStartsEvade(true, true, true), "a pet never evades");
		}

		/// <summary>
		/// A pet answers an attacker it can hit from inside its owner leash, and ignores one it
		/// would have to break the leash to reach.
		/// </summary>
		/// <remarks>
		/// With combat entry asked on every hit, a pet sent after an attacker beyond its leash
		/// would break off at the leash and be sent out again by the next shot at its owner.
		/// </remarks>
		[Test]
		public void APet_AnswersOnlyAttackersItCanReachWithinItsOwnerLeash()
		{
			Vector3 owner = Vector3.zero;

			Assert.IsTrue(AIController.PetCanAnswerAttacker(owner, new Vector3(25f, 0f, 0f), 30f, 1f),
				"inside the leash");
			Assert.IsFalse(AIController.PetCanAnswerAttacker(owner, new Vector3(40f, 0f, 0f), 30f, 1f),
				"a melee pet would break off at 30 m before reaching an attacker at 40 m");
			Assert.IsTrue(AIController.PetCanAnswerAttacker(owner, new Vector3(40f, 0f, 0f), 30f, 18f),
				"an archer pet can shoot an attacker at 40 m from 22 m out, inside its leash");
			Assert.IsTrue(AIController.PetCanAnswerAttacker(owner, new Vector3(500f, 0f, 0f), 0f, 1f),
				"no owner leash, no limit");
		}

		// --- The rules, on a real brain ------------------------------------------------------------

		/// <summary>
		/// On a prepared brain: a calm return is not an evade, a leash return is, and every way
		/// out of it — a transition, arriving home, the corpse path, quarantine, the pool — ends it.
		/// </summary>
		/// <remarks>
		/// <c>CheckLeash</c> needs a live scene to trip, so the flag it sets is set here by
		/// reflection; everything that must END the evade is exercised through the real methods.
		/// </remarks>
		[Test]
		public void EveryWayOutOfTheReturn_EndsTheEvade()
		{
			// An edit-mode NPC has no NavMesh to stand on; the agent's complaints about that are expected.
			UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;

			AIController brain = PrepareOrc(out AIBrainHost host, out NPC npc);
			Assert.IsNotNull(brain.ReturnHomeState, "the orc's archetype must leash for this test to mean anything");

			brain.ChangeState(brain.ReturnHomeState);
			Assert.IsFalse(brain.IsEvading, "ReturnHome entered as a movement state is not an evade");

			BeginLeashEvade(brain);
			Assert.IsTrue(brain.IsEvading);

			brain.TransitionToIdleState();
			Assert.IsFalse(brain.IsEvading, "a transition out of the return ends the evade");

			// Leaving and coming back does not revive a stale evade: the flag was cleared on the way out.
			brain.ChangeState(brain.ReturnHomeState);
			Assert.IsFalse(brain.IsEvading, "re-entering the return is not a leash");

			BeginLeashEvade(brain);
			// Threat can land on the way home: an NPC that owns a pet shares every hit its pet takes.
			brain.AggressionState.Controller.RecordDamage(9001L, 10);
			brain.CompleteReturnHome();
			Assert.IsFalse(brain.IsEvading, "arriving home ends the evade");
			Assert.IsFalse(brain.AggressionState.HasAggression, "arriving home empties the table");

			brain.ChangeState(brain.ReturnHomeState);
			BeginLeashEvade(brain);
			Assert.IsTrue(brain.SuspendForCorpse());
			Assert.IsFalse(brain.IsEvading, "a corpse does not evade");
			brain.ResumeAfterCorpse();

			brain.ChangeState(brain.ReturnHomeState);
			BeginLeashEvade(brain);
			brain.Quarantine();
			Assert.IsFalse(brain.IsEvading, "a quarantined brain must not leave its NPC immune for good");

			host.Prepare(npc, Vector3.zero);
			Assert.IsFalse(brain.Quarantined, "a new spawn is a new chance");

			brain.ChangeState(brain.ReturnHomeState);
			BeginLeashEvade(brain);
			brain.ResetForPool();
			Assert.IsFalse(brain.IsEvading, "a pooled NPC does not carry an evade into its next life");
		}

		/// <summary>
		/// An evading NPC takes no threat and cannot be taunted back into the fight.
		/// </summary>
		[Test]
		public void AnEvadingNpc_TakesNoThreatAndIgnoresTaunts()
		{
			UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;

			AIController brain = PrepareOrc(out _, out _);
			brain.ChangeState(brain.ReturnHomeState);
			BeginLeashEvade(brain);

			Harness.StubCharacter taunter = new Harness.StubCharacter { ID = 777 };
			brain.ApplyTaunt(taunter, 500f, true, 10f, true);
			brain.ApplyAreaThreat(taunter, 50f, 30);

			Assert.IsFalse(brain.AggressionState.HasAggression, "no threat lands while evading");
			Assert.IsTrue(ReferenceEquals(brain.CurrentState, brain.ReturnHomeState), "a taunt must not pull an evading NPC back into combat");
			Assert.IsTrue(brain.IsEvading);
		}

		// --- The wiring, by source -----------------------------------------------------------------

		[Test]
		public void EveryRecordedHit_IsOfferedToTheBrain_WithNoEdge()
		{
			string state = CodeOnly(Read(AI_ROOT + "AggressionState.cs"));
			string handleDamaged = MethodBody(state, "public void HandleDamaged(");

			LogAssert.IsTrue(handleDamaged.Contains("OnHitRecorded?.Invoke(attacker);"),
				"HandleDamaged must hand every recorded hit to the brain");
			LogAssert.IsFalse(handleDamaged.Contains("wasEmpty"),
				"combat entry must not hang on the table's empty-to-non-empty edge");

			string brain = CodeOnly(Read(AI_ROOT + "AIController.cs"));
			string onThreat = MethodBody(brain, "public void OnThreatReceived(ICharacter attacker)");
			LogAssert.IsTrue(onThreat.Contains("CanEnterCombatFromThreat("),
				"OnThreatReceived must decide through the pinned rule");
			LogAssert.IsFalse(onThreat.Contains("CurrentState == ReturnHomeState"),
				"a calm walk home is attackable; only an evade refuses a hit");
			LogAssert.IsTrue(onThreat.Contains("PetStanceAllowsAutoEngage(false)"),
				"a passive pet still does not fight back");

			string taunt = MethodBody(brain, "public void ApplyTaunt(");
			int add = taunt.IndexOf("Aggression.AddPoints(taunter.ID, points);", StringComparison.Ordinal);
			int offer = taunt.IndexOf("OnThreatReceived(taunter);", StringComparison.Ordinal);
			LogAssert.IsTrue(add >= 0 && offer > add,
				"a taunt's threat is offered to the same combat-entry rule a hit is, after it lands");
		}

		[Test]
		public void TheTableIsEmptied_WhenAFightEnds_AndOnArrivingHome()
		{
			string attacking = CodeOnly(Read(AI_ROOT + "States/BaseAttackingState.cs"));
			LogAssert.IsTrue(MethodBody(attacking, "protected virtual void OnCombatEnded(AIController controller)")
					.Contains("controller.AggressionState?.Clear();"),
				"OnCombatEnded must empty the threat table");

			string brain = CodeOnly(Read(AI_ROOT + "AIController.cs"));
			string complete = MethodBody(brain, "internal void CompleteReturnHome()");
			LogAssert.IsTrue(complete.Contains("AggressionState?.Clear();") && complete.Contains("leashEvade = false;"),
				"arriving home ends the evade and empties the table");

			string home = CodeOnly(Read(AI_ROOT + "States/ReturnHomeState.cs"));
			string update = MethodBody(home, "public override void UpdateState(AIController controller, float deltaTime)");
			LogAssert.IsTrue(update.Contains("controller.CompleteReturnHome();"),
				"arrival must go through CompleteReturnHome");
			LogAssert.IsFalse(home.Contains(".Immortal"),
				"the evade must not borrow Immortal: it belongs to the prefab, corpses and administrators");

			string leash = MethodBody(brain, "private void CheckLeash(float deltaTime)");
			LogAssert.IsTrue(leash.Contains("CompleteReturnHome();"),
				"a full-leash warp out of a fight must leave the fight, not run back to the player");

			string change = MethodBody(brain, "public void ChangeState(BaseAIState newState, List<ICharacter> targets = null)");
			int clear = change.IndexOf("leashEvade = false;", StringComparison.Ordinal);
			int exit = change.IndexOf("CurrentState.Exit(this);", StringComparison.Ordinal);
			LogAssert.IsTrue(clear >= 0 && exit > clear, "every real transition ends an evade");
		}

		[Test]
		public void AnEvadingNpc_RefusesDamage_BeforeAnyEventIsRaised()
		{
			string damage = CodeOnly(Read("Assets/Scripts/Shared/Implementation/Entity/Prediction/CharacterAttribute/CharacterDamageController.cs"));
			string body = MethodBody(damage, "public int Damage(ICharacter attacker, int amount, DamageAttributeTemplate damageAttribute");

			int refuse = body.IndexOf("if (Immortal || IsEvading())", StringComparison.Ordinal);
			int combat = body.IndexOf("EnterCombat();", StringComparison.Ordinal);
			int report = body.IndexOf("QueueCombatEvent(", StringComparison.Ordinal);
			int raised = body.IndexOf("ICharacterDamageController.OnDamaged?.Invoke", StringComparison.Ordinal);

			LogAssert.IsTrue(refuse >= 0, "Damage must refuse an evading NPC");
			LogAssert.IsTrue(combat > refuse && report > refuse && raised > refuse,
				"the refusal must come before combat entry, the landed-damage report and the OnDamaged event, so it raises no threat");

			/* O1: the refusal is no longer silent. It reports "Evade" (or "Immune") to the attacker
			 * inside the refusal branch, which still returns before any of the above. */
			int refusal = body.IndexOf("ReportRefusal(attacker, damageAttribute);", refuse, StringComparison.Ordinal);
			int giveUp = body.IndexOf("return 0;", refuse, StringComparison.Ordinal);
			LogAssert.IsTrue(refusal > refuse && giveUp > refusal && combat > giveUp,
				"the refused hit reports its refusal to the attacker and then returns, before combat entry");

			string brainApi = Read("Assets/Scripts/Shared/Core/Entity/NPC/INPCBrain.cs");
			LogAssert.IsTrue(brainApi.Contains("bool IsEvading { get; }"), "shared code reaches the evade through INPCBrain");
		}

		[Test]
		public void ThreatDecay_IsCreditedTheTimeThatReallyPassed()
		{
			string brain = CodeOnly(Read(AI_ROOT + "AIController.cs"));
			string tick = MethodBody(brain, "private void TickAggression(float dt)");

			LogAssert.IsTrue(tick.Contains("AggressionState?.Tick(elapsed);"),
				"each decay pass must be credited the accumulated elapsed time");
			LogAssert.IsFalse(tick.Contains("Tick(AGGRESSION_TICK_INTERVAL)"),
				"crediting the nominal interval ran decay at 62.5% speed in the Nearby tier");
		}

		[Test]
		public void TheHealerScanClock_RunsOnStateTime_AndCachesNobodyInjured()
		{
			string healer = CodeOnly(Read(AI_ROOT + "States/HealerAttackingState.cs"));
			string scan = MethodBody(healer, "private ICharacter GetInjuredAlly(AIController controller)");

			LogAssert.IsFalse(scan.Contains("LastAiDeltaTime"),
				"the brain-tick delta inside a once-a-second state held a 0.5 s cache for about 4 s");
			LogAssert.IsTrue(scan.Contains("if (cached == null)"),
				"a scan that found nobody to heal must be held until the next scan is due");
			LogAssert.IsTrue(MethodBody(healer, "public override void UpdateState(AIController controller, float deltaTime)")
					.Contains("controller.AllyScanTimer -= deltaTime;"),
				"the scan clock advances by the state's own elapsed time, on every update");
		}

		[Test]
		public void AbilityPicks_PassCachedDelegates_AndReuseOneConditionContext()
		{
			string attacking = CodeOnly(Read(AI_ROOT + "States/BaseAttackingState.cs"));
			LogAssert.IsTrue(MethodBody(attacking, "protected virtual Ability PickAbility(AIController controller)").Contains("IsEnemyAbilityFilter"),
				"a method group passed per pick allocates a delegate under C# 9");

			string healer = CodeOnly(Read(AI_ROOT + "States/HealerAttackingState.cs"));
			LogAssert.IsTrue(healer.Contains("isDamageAbilityFilter ??= IsDamageAbility;") && healer.Contains("isHealAbilityFilter ??= IsHealAbility;"),
				"the healer's filters are built once");

			string defender = CodeOnly(Read(AI_ROOT + "States/DefenderAttackingState.cs"));
			LogAssert.IsTrue(defender.Contains("isTauntFilter ??= IsTaunt;"), "the defender's taunt filter is built once");

			string brain = CodeOnly(Read(AI_ROOT + "AIController.cs"));
			LogAssert.IsFalse(brain.Contains("EventData activationCheckData = null;"),
				"the condition context is the controller's, not allocated per pick");
			string rotation = CodeOnly(Read(AI_ROOT + "AIAbilityRotation.cs"));
			LogAssert.IsFalse(rotation.Contains("EventData activationCheckData = null;"),
				"nor per rotation entry");
		}

		[Test]
		public void ABrokenBrain_IsQuarantinedThroughItsFaultLog_NotLoggedEveryTick()
		{
			string host = CodeOnly(Read(AI_ROOT + "AIBrainHost.cs"));
			string onTick = MethodBody(host, "private void OnTick()");

			LogAssert.IsTrue(onTick.Contains("ReportFault(brain, ex);"), "a throwing brain goes through its fault log");
			LogAssert.IsFalse(onTick.Contains("Log.Error("), "no full stack trace per tick per brain");

			string report = MethodBody(host, "private void ReportFault(AIController brain, System.Exception ex)");
			LogAssert.IsTrue(report.Contains("ShouldQuarantine(log.ConsecutiveFailures)") &&
				report.Contains("Untrack(brain);") && report.Contains("brain.Quarantine();"),
				"after the named number of failures in a row the brain is taken off the tick and stopped");
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
