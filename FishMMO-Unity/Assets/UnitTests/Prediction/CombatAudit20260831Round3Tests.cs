using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using NUnit.Framework;
using UnityEngine;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;
using UnityLogAssert = UnityEngine.TestTools.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Regressions for the 2026-08-31 round-3 combat/prediction audit.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The round's defects, grouped: the NoSpawn reconcile flag stamped for abilities that
	/// deterministically spawn nothing and its correction sweeping confirmed later spawns; the
	/// consumable path skipping its cooldown on reconcile replay; a combat death never telling
	/// clients combat ended; an NPC's cached target silently re-pointing at a pooled object's next
	/// occupant; the caster's predicted number drawn pre-mitigation and confirmed against a
	/// post-mitigation report; the prediction pairing key missing the damage type; the rejected
	/// label mechanism expiring at exactly the confirmation window and recoloring recycled pooled
	/// labels; a redirected projectile granting late joiners its full lifetime back; the observer
	/// streaming density pass culling active fights and its candidacy filter bypassing the
	/// visibility budget; the coalescer's overflow fallback merging damage into heals; the taunt
	/// guarantee divided by a transient multiplier; and a taunt consuming the aggression table's
	/// combat-initiation edge.
	/// </para>
	/// <para>
	/// What can be exercised for real is. What needs a spawned NetworkObject, a physics scene or
	/// two connected peers is asserted on the SOURCE instead — the idiom the earlier audit
	/// fixtures already use — and every such test says so in its remarks.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class CombatAudit20260831Round3Tests
	{
		private readonly List<GameObject> gameObjects = new List<GameObject>();
		private readonly List<ScriptableObject> templates = new List<ScriptableObject>();

		[SetUp]
		public void SetUp()
		{
			PredictedCombatEvents.Clear();
		}

		[TearDown]
		public void TearDown()
		{
			PredictedCombatEvents.Clear();

			for (int i = 0; i < gameObjects.Count; ++i)
			{
				if (gameObjects[i] != null)
				{
					UnityEngine.Object.DestroyImmediate(gameObjects[i]);
				}
			}
			gameObjects.Clear();

			for (int i = 0; i < templates.Count; ++i)
			{
				if (templates[i] != null)
				{
					UnityEngine.Object.DestroyImmediate(templates[i]);
				}
			}
			templates.Clear();
		}

		// ── W5-1: the prediction pairing key carries the damage type ─────────────────

		/// <summary>
		/// One caster can run several damage streams onto one victim at once — a server-ticked DoT
		/// it never predicted, and a predicted projectile. With (source, target, kind) alone the
		/// DoT's report consumed the projectile's pending entry: the DoT went undrawn and the
		/// projectile was drawn twice.
		/// </summary>
		[Test]
		public void TryConfirm_DoesNotMatchAcrossDamageTypes()
		{
			ICharacter attacker = MakeCharacter("TypeKeyAttacker", objectId: 21);
			ICharacter target = MakeCharacter("TypeKeyTarget", objectId: 22);
			DamageAttributeTemplate fire = MakeDamageType("Audit0831R3_Fire");
			DamageAttributeTemplate frost = MakeDamageType("Audit0831R3_Frost");
			LogAssert.IsTrue(fire.ID != frost.ID, "Two named templates must hold distinct ids for this test to mean anything.");

			PredictedCombatEvents.Predict(attacker, target, 30, PredictedCombatEvents.Kind.Damage, fire, 0f);

			LogAssert.IsFalse(
				PredictedCombatEvents.TryConfirm(attacker, target, PredictedCombatEvents.Kind.Damage, 1, frost),
				"A report for a DIFFERENT damage type from the same caster is a different stream — it must not consume this prediction.");
			LogAssert.AreEqual(1, PredictedCombatEvents.PendingCount, "The fire prediction must still be pending.");

			LogAssert.IsTrue(
				PredictedCombatEvents.TryConfirm(attacker, target, PredictedCombatEvents.Kind.Damage, 1, fire),
				"The matching type settles it.");
			LogAssert.AreEqual(0, PredictedCombatEvents.PendingCount, "Settled.");
		}

		/// <summary>Typeless damage (null template) is its own type and only matches itself.</summary>
		[Test]
		public void TryConfirm_TreatsTypelessDamageAsItsOwnType()
		{
			ICharacter attacker = MakeCharacter("TypelessAttacker", objectId: 23);
			ICharacter target = MakeCharacter("TypelessTarget", objectId: 24);
			DamageAttributeTemplate fire = MakeDamageType("Audit0831R3_TypelessFire");

			PredictedCombatEvents.Predict(attacker, target, 10, PredictedCombatEvents.Kind.Damage, null, 0f);

			LogAssert.IsFalse(
				PredictedCombatEvents.TryConfirm(attacker, target, PredictedCombatEvents.Kind.Damage, 1, fire),
				"A typed report must not consume a typeless prediction.");
			LogAssert.IsTrue(
				PredictedCombatEvents.TryConfirm(attacker, target, PredictedCombatEvents.Kind.Damage, 1, null),
				"A typeless report matches the typeless prediction.");
		}

		// ── W5-4: coalescer overflow never crosses kinds ─────────────────────────────

		/// <summary>
		/// Overflow used to fold into entry 0 whatever its kind, so a victim's ninth damage stream
		/// inflated a heal number and settled the wrong kind's predictions. With no same-kind host
		/// the hit is dropped instead.
		/// </summary>
		[Test]
		public void Coalescer_Overflow_NeverMergesDamageIntoAHeal()
		{
			CombatEventCoalescer coalescer = new CombatEventCoalescer();
			for (int source = 1; source <= CombatEventCoalescer.MaxEntries; ++source)
			{
				coalescer.Add(source, CombatEventKind.Heal, 0, 10);
			}
			LogAssert.AreEqual(CombatEventCoalescer.MaxEntries, coalescer.Count, "Precondition: the table is full of heals.");

			coalescer.Add(99, CombatEventKind.Damage, 5, 25);

			List<CombatEventCoalescer.Entry> entries = new List<CombatEventCoalescer.Entry>();
			coalescer.Flush(entries);
			int healTotal = 0;
			foreach (CombatEventCoalescer.Entry entry in entries)
			{
				LogAssert.AreEqual(CombatEventKind.Heal, entry.Kind, "No entry may change kind to absorb overflow.");
				healTotal += entry.Amount;
				LogAssert.AreEqual(1, entry.Occurrences, "No heal entry may count the dropped damage hit against heal predictions.");
			}
			LogAssert.AreEqual(10 * CombatEventCoalescer.MaxEntries, healTotal,
				"The damage overflow must be dropped, not folded into a heal number.");
		}

		/// <summary>Overflow prefers a same-kind host even when the damage type differs.</summary>
		[Test]
		public void Coalescer_Overflow_FoldsIntoASameKindHost()
		{
			CombatEventCoalescer coalescer = new CombatEventCoalescer();
			for (int source = 1; source <= CombatEventCoalescer.MaxEntries - 1; ++source)
			{
				coalescer.Add(source, CombatEventKind.Heal, 0, 10);
			}
			coalescer.Add(50, CombatEventKind.Damage, 5, 10);
			LogAssert.AreEqual(CombatEventCoalescer.MaxEntries, coalescer.Count, "Precondition: full, with one damage entry.");

			coalescer.Add(99, CombatEventKind.Damage, 6, 25);

			List<CombatEventCoalescer.Entry> entries = new List<CombatEventCoalescer.Entry>();
			coalescer.Flush(entries);
			bool foundDamageHost = false;
			foreach (CombatEventCoalescer.Entry entry in entries)
			{
				if (entry.Kind != CombatEventKind.Damage)
				{
					continue;
				}
				foundDamageHost = true;
				LogAssert.AreEqual(35, entry.Amount, "The overflow damage folds into the damage entry, not a heal.");
				LogAssert.AreEqual(2, entry.Occurrences, "Both damage hits are counted where damage predictions settle.");
			}
			LogAssert.IsTrue(foundDamageHost, "The damage host entry must survive the flush.");
		}

		// ── W8-1: NoSpawn scope — only the reconcile tick's own spawns ───────────────

		/// <summary>
		/// The at-tick destroy must leave later spawns alone: with agreeing seeds the reconcile
		/// proves ONLY tick T spawned nothing on the server, and objects from T+1 are exactly as
		/// confirmed as before the reconcile arrived — the old &gt;= sweep erased them permanently
		/// because the replay never re-spawns.
		/// </summary>
		[Test]
		public void DestroyAbilityObjectsAtTick_SparesLaterConfirmedSpawns()
		{
			AbilityTemplate template = NewTemplate<AbilityTemplate>("Audit0831R3_AtTick");
			Ability ability = new Ability(1L, template);
			AbilityObject atTick = NewAbilityObject("SpawnAtT");
			AbilityObject later = NewAbilityObject("SpawnAtTPlusOne");
			atTick.SpawnTick = new PredictionTick(100u);
			later.SpawnTick = new PredictionTick(101u);
			ability.Objects = new Dictionary<int, Dictionary<int, AbilityObject>>
			{
				[1] = new Dictionary<int, AbilityObject> { [0] = atTick },
				[2] = new Dictionary<int, AbilityObject> { [0] = later },
			};

			// DestroyAbilityObjectInternal calls Object.Destroy, which edit mode logs an error
			// for and then declines; the state transition under test happens before that call.
			UnityLogAssert.ignoreFailingMessages = true;
			try
			{
				ability.DestroyAbilityObjectsAtTick(100u);
			}
			finally
			{
				UnityLogAssert.ignoreFailingMessages = false;
			}

			LogAssert.IsTrue(atTick.IsDestroyed, "The tick-T object is the unconfirmed one and must go.");
			LogAssert.IsFalse(later.IsDestroyed, "The T+1 object is confirmed by the agreeing seeds and must survive.");
			LogAssert.IsFalse(ability.Objects.ContainsKey(1), "The emptied container is removed.");
			LogAssert.IsTrue(ability.Objects.ContainsKey(2), "The surviving container stays.");
		}

		/// <summary>
		/// SOURCE assertions for the two NoSpawn halves that need a networked controller to run
		/// for real: the server-side stamp is gated on <c>SpawnsWorldObject</c> (a Self/pet/
		/// prefab-less cast spawns nothing on EVERY peer deterministically, so there is nothing to
		/// take back — stamping those made every completed self-buff destroy the owner's confirmed
		/// later projectiles), and the owner-side correction narrows to the single tick when the
		/// seeds agree.
		/// </summary>
		[Test]
		public void NoSpawn_IsGatedOnSpawnsWorldObject_AndScopedToItsTick()
		{
			string activation = ReadSource(
				"Assets/Scripts/Shared/Implementation/Entity/Prediction/Ability/AbilityController.Activation.cs");
			LogAssert.IsTrue(
				activation.Contains("if (spawned == null && base.IsServerStarted && AbilityObject.SpawnsWorldObject(ability.Template))"),
				"The serverSpawnedNothingTick stamp must be gated on SpawnsWorldObject: Spawn returns null " +
				"deterministically for pet/self/prefab-less templates, and flagging those turns every completed " +
				"self-buff cast into a NoSpawn correction against the owner's confirmed objects.");

			string controller = ReadSource(
				"Assets/Scripts/Shared/Implementation/Entity/Prediction/Ability/AbilityController.cs");
			LogAssert.IsTrue(
				controller.Contains("bool onlyAtTick = destroyAtTick && havePredicted && predictedSeed == rd.Seed;"),
				"NoSpawn with AGREEING seeds proves a miss at tick T only; the correction must not widen to later ticks.");
			LogAssert.IsTrue(
				controller.Contains("DestroyAbilityObjectsAtTick(reconcileTick)"),
				"The narrow scope must actually be used for the agreeing-seed NoSpawn correction.");
		}

		// ── W8-2: consumable replay re-applies its cooldown ──────────────────────────

		/// <summary>
		/// SOURCE assertion — FinishConsumable needs a networked controller. Every reconcile for a
		/// tick before the use wipes the predicted cooldown, and the replay of the use tick is the
		/// only thing that can put it back; the ability path (FinishAbility) already re-applies on
		/// replay for exactly this reason, and the consumable path skipped everything.
		/// </summary>
		[Test]
		public void ConsumableReplay_ReappliesTheCooldown()
		{
			string activation = ReadSource(
				"Assets/Scripts/Shared/Implementation/Entity/Prediction/Ability/AbilityController.Activation.cs");
			LogAssert.IsTrue(
				activation.Contains("else if (consumable.Cooldown > 0.0f && cachedCooldownController != null)"),
				"FinishConsumable must have a replay branch for the cooldown.");
			LogAssert.IsTrue(
				activation.Contains("cachedCooldownController.AddCooldown(consumable.ID, new CooldownInstance("),
				"And that branch must re-add the cooldown the pre-use reconciles wiped.");
		}

		// ── F1: death broadcasts the combat clear ────────────────────────────────────

		/// <summary>
		/// SOURCE assertion — Kill is server-gated. The timer-expiry path was the only sender of
		/// InCombat=false and Kill silences the timer, so every observing client kept the combat
		/// flag set on the corpse until the character's NEXT fight ended by timer.
		/// </summary>
		[Test]
		public void Kill_BroadcastsInCombatFalse()
		{
			string source = ReadSource(
				"Assets/Scripts/Shared/Implementation/Entity/Prediction/CharacterAttribute/CharacterDamageController.cs");
			int killStart = source.IndexOf("public void Kill(ICharacter killer)", StringComparison.Ordinal);
			LogAssert.IsTrue(killStart >= 0, "Kill must exist.");
			int killEnd = source.IndexOf("ResolveKillCredit", killStart, StringComparison.Ordinal);
			LogAssert.IsTrue(killEnd > killStart, "Kill's combat-clear block precedes kill credit.");
			string killPrefix = source.Substring(killStart, killEnd - killStart);
			LogAssert.IsTrue(killPrefix.Contains("BroadcastCombatState(false)"),
				"Kill must broadcast InCombat=false — no other sender can fire once the combat timer is cleared here.");
			LogAssert.IsTrue(killPrefix.Contains("bool wasInCombat = Character.IsFlagged(CharacterFlags.IsInCombat);"),
				"Guarded, so a character killed outside combat broadcasts nothing.");
		}

		// ── F2: the NPC's cached target detects a pooled occupant swap ───────────────

		/// <summary>
		/// A pooled NetworkObject keeps its Transform and components across occupants, so when a
		/// targeted character despawns and the object is re-issued, the Transform still compares
		/// equal, the setter never re-runs, and every null/active/alive check passes — the NPC
		/// keeps attacking a character that never engaged it. The identity recorded at target time
		/// is what detects the swap.
		/// </summary>
		[Test]
		public void TargetCharacter_RejectsAPooledOccupantSwap()
		{
			GameObject npcGo = new GameObject("Audit0831R3_NPC");
			gameObjects.Add(npcGo);
			AIController controller = npcGo.AddComponent<AIController>();

			GameObject victimGo = new GameObject("Audit0831R3_Victim");
			gameObjects.Add(victimGo);
			ProbeCharacter victim = victimGo.AddComponent<ProbeCharacter>();
			victim.ID = 7;

			controller.Target = victimGo.transform;
			LogAssert.IsTrue(ReferenceEquals(controller.TargetCharacter, victim),
				"While the occupant is unchanged the cached component is served.");

			// The pool re-issues the same object as a different character: same Transform, same
			// component instance, new identity.
			victim.ID = 8;

			LogAssert.IsNull(controller.TargetCharacter,
				"An identity change behind an unchanged Transform is a pooled swap; the stale target must read as gone, " +
				"so the attacking state's null check drops it instead of attacking the new occupant.");
		}

		// ── W5-2: predicted numbers are the applied amounts ──────────────────────────

		/// <summary>
		/// SOURCE assertions — the damage pipeline needs a networked controller. The caster's
		/// client runs resistances and mitigation in its own Damage() call (the mitigation note in
		/// CharacterDamageController says it can and should), but the label was drawn from the raw
		/// provider amount; TryConfirm deliberately ignores amounts, so the pre-mitigation number
		/// was confirmed and stood forever.
		/// </summary>
		[Test]
		public void PredictedNumbers_DrawTheAppliedAmount()
		{
			string damage = ReadSource(
				"Assets/Scripts/Shared/Implementation/Entity/ECA/Actions/Character/ApplyDamageAction.cs");
			LogAssert.IsTrue(
				damage.Contains("int applied = defenderDamageController.Damage(initiator, amount, DamageAttributeTemplate);"),
				"ApplyDamageAction must capture what actually landed.");
			LogAssert.IsTrue(
				damage.Contains("PredictedCombatEvents.Predict(initiator, target, applied,"),
				"And the predicted label must be drawn from it, not from the raw provider amount.");

			string heal = ReadSource(
				"Assets/Scripts/Shared/Implementation/Entity/ECA/Actions/Character/ApplyHealAction.cs");
			LogAssert.IsTrue(
				heal.Contains("int applied = defenderDamageController.Heal(initiator, amount);"),
				"ApplyHealAction must capture whether the heal had any effect.");
			LogAssert.IsTrue(
				heal.Contains("PredictedCombatEvents.Predict(initiator, target, applied, PredictedCombatEvents.Kind.Heal,"),
				"A no-effect heal sends no report, so its label could only ever grey out.");
		}

		// ── W5-3: the rejected-label mechanism can actually mark its label ───────────

		/// <summary>The pool checkout counter that makes a kept label reference safe to act on.</summary>
		[Test]
		public void WorldLabel_LeaseMovesOnEveryCheckout()
		{
			GameObject go = new GameObject("Audit0831R3_Label");
			gameObjects.Add(go);
			FishMMO.Client.UITKWorldLabel label = go.AddComponent<FishMMO.Client.UITKWorldLabel>();

			uint before = label.Lease;
			label.Initialize("10", Vector3.zero, Color.white, 2f, 1f, false, 0);
			uint first = label.Lease;
			label.Initialize("25", Vector3.zero, Color.white, 2f, 1f, false, 0);
			uint second = label.Lease;

			LogAssert.IsTrue(first != before && second != first,
				"Every checkout must move the lease, or a reference kept across a recycle recolors the next occupant's number.");
		}

		/// <summary>
		/// SOURCE assertions for the display glue: the rejection recolor is lease-guarded, and a
		/// predicted label persists past the confirmation window — with equal lifetimes every
		/// rejection landed at the exact moment its label expired, so the grey-out either marked
		/// nothing or marked a recycled label now showing someone else's live hit.
		/// </summary>
		[Test]
		public void RejectedLabels_AreLeaseGuarded_AndOutliveTheWindow()
		{
			string display = ReadSource("Assets/Scripts/Client/World/ClientCombatDisplay.cs");
			LogAssert.IsTrue(display.Contains("predicted.Label.Lease == predicted.Lease"),
				"The rejection recolor must verify the lease it captured at draw time.");
			LogAssert.IsTrue(display.Contains("PredictedCombatEvents.ConfirmationWindowSeconds + RejectedVisibleSeconds"),
				"A predicted label must persist past the window that is the only thing able to reject it.");
		}

		// ── W5-5: a redirected object's lifetime for late joiners ────────────────────

		/// <summary>Redirect moves the finished leg into the lifetime-only counter.</summary>
		[Test]
		public void Redirect_AccumulatesTheFinishedLeg()
		{
			AbilityObject abilityObject = NewAbilityObject("Audit0831R3_Redirect");

			abilityObject.ElapsedTicks = 60u;
			abilityObject.Redirect(Quaternion.Euler(0f, 90f, 0f));
			LogAssert.AreEqual(60u, abilityObject.PreRedirectElapsedTicks, "The first leg's ticks move to the lifetime counter.");
			LogAssert.AreEqual(0u, abilityObject.ElapsedTicks, "The trajectory clock restarts for the new leg.");

			abilityObject.ElapsedTicks = 30u;
			abilityObject.Redirect(Quaternion.Euler(0f, 180f, 0f));
			LogAssert.AreEqual(90u, abilityObject.PreRedirectElapsedTicks, "Legs accumulate across redirects.");
		}

		/// <summary>
		/// ConsumeLifetime charges lifetime without advancing the trajectory clock, and expires
		/// quietly — the receiver's half of streaming a redirected object.
		/// </summary>
		[Test]
		public void ConsumeLifetime_ChargesWithoutMovingTheLeg()
		{
			AbilityTemplate template = NewTemplate<AbilityTemplate>("Audit0831R3_Life");
			template.LifeTime = 5f;
			Ability ability = new Ability(2L, template);
			ability.Objects = new Dictionary<int, Dictionary<int, AbilityObject>>();

			AbilityObject abilityObject = NewAbilityObject("Audit0831R3_LifeObject");
			abilityObject.Ability = ability;
			abilityObject.RemainingLifeTime = 5f;
			FieldInfo tickDelta = typeof(AbilityObject).GetField("tickDelta", BindingFlags.Instance | BindingFlags.NonPublic);
			LogAssert.IsNotNull(tickDelta, "The private tick delta must exist.");
			tickDelta.SetValue(abilityObject, 0.1f);

			abilityObject.ConsumeLifetime(10u);
			LogAssert.IsTrue(Mathf.Approximately(4f, abilityObject.RemainingLifeTime),
				"Ten ticks at 0.1 s charge one second of lifetime.");
			LogAssert.AreEqual(0u, abilityObject.ElapsedTicks,
				"The trajectory clock must not move — the pose is fully described by the current leg's fast-forward.");
			LogAssert.IsFalse(abilityObject.IsDestroyed, "Still alive.");

			UnityLogAssert.ignoreFailingMessages = true;
			try
			{
				abilityObject.ConsumeLifetime(50u);
			}
			finally
			{
				UnityLogAssert.ignoreFailingMessages = false;
			}
			LogAssert.IsTrue(abilityObject.IsDestroyed,
				"Charging past the remaining lifetime destroys quietly, like a fast-forward past expiry.");
		}

		/// <summary>
		/// SOURCE assertions for the wire: the in-flight payload carries the pre-redirect life,
		/// writer and reader in the same position, and the receiver charges it. Without this a late
		/// joiner rebuilt a redirected projectile with its full lifetime less only the current leg
		/// and watched it detonate seconds after the server's copy was gone.
		/// </summary>
		[Test]
		public void InFlightPayload_CarriesPreRedirectLife()
		{
			string networking = ReadSource(
				"Assets/Scripts/Shared/Implementation/Entity/Prediction/Ability/AbilityController.Networking.cs");

			int writeAnchor = networking.IndexOf("writer.WriteUInt32(entry.ServerStartTick);", StringComparison.Ordinal);
			int writeLife = networking.IndexOf("writer.WriteUInt32(entry.LifeElapsedBeforeStartTicks);", StringComparison.Ordinal);
			LogAssert.IsTrue(writeAnchor >= 0 && writeLife > writeAnchor,
				"The writer must put the pre-redirect life immediately after the leg's start tick.");

			int readAnchor = networking.IndexOf("uint inFlightServerStartTick = reader.ReadUInt32();", StringComparison.Ordinal);
			int readLife = networking.IndexOf("uint inFlightLifeElapsedBeforeStart = reader.ReadUInt32();", StringComparison.Ordinal);
			LogAssert.IsTrue(readAnchor >= 0 && readLife > readAnchor,
				"The reader must consume it in the same wire position.");

			LogAssert.IsTrue(networking.Contains("spawned.ConsumeLifetime(entry.LifeElapsedBeforeStartTicks);"),
				"And the receiver must charge the earlier legs against the reproduced object's lifetime.");
		}

		// ── Culling F1/F2: the streaming ranges hold what the pins promise ───────────

		/// <summary>
		/// SOURCE assertions — the density pass runs over live FishNet conditions. The engaged/
		/// target pins answer only the budget condition; the per-object range feeds the distance
		/// condition, which the pins never see, so an unfloored density shrink could despawn an
		/// in-combat character from their own attacker's client. And candidacy filtered by the
		/// VIEWER's range while the distance condition admits by the OBSERVED object's, letting
		/// every pair in the gap bypass the visibility budget and the full-rate cap.
		/// </summary>
		[Test]
		public void ObserverStreaming_RangesHoldTheCombatAndBudgetInvariants()
		{
			string registry = ReadSource(
				"Assets/Scripts/Shared/Implementation/Entity/Prediction/ObserverStreaming/ObserverStreamingRegistry.cs");

			LogAssert.IsTrue(registry.Contains("if (entry.InCombat)"),
				"The density pass must special-case in-combat characters.");
			LogAssert.IsTrue(
				registry.Contains("range = Mathf.Max(range, Mathf.Min(entry.BaseRange, ObserverStreamingPolicy.EngagementRangeCeiling));"),
				"An in-combat character's range is floored at the engagement ceiling (never above the authored base).");

			LogAssert.IsTrue(
				registry.Contains("float admittingRange = observed.HasDistanceCondition && observed.AppliedRange > 0f"),
				"Candidacy must measure against the range the observed object's DistanceCondition actually admits by, " +
				"or unranked-but-admitted pairs bypass the budget unconditionally.");
		}

		// ── F3/F5: taunt guarantee and the consumed aggression edge ──────────────────

		/// <summary>
		/// SOURCE assertions — the taunt needs a live AIController with aggression state. The
		/// guarantee must not divide by the taunter's own vulnerability multiplier: it is transient
		/// (gone on the first heal) while the granted points are permanent, so the discounted score
		/// fell back under the previous top's ceiling the moment the tank was healed. And a taunt
		/// that writes the table's FIRST entry must fire the combat-initiation edge itself, or the
		/// first real hit sees a non-empty table and never initiates combat.
		/// </summary>
		[Test]
		public void Taunt_GuaranteeIsPermanent_AndTheEdgeIsNotConsumed()
		{
			string taunt = ReadSource(
				"Assets/Scripts/Shared/Implementation/Entity/ECA/Actions/Character/ApplyTauntAction.cs");

			LogAssert.IsFalse(taunt.Contains("taunterMultiplier"),
				"The required points must not be discounted by the taunter's transient vulnerability multiplier.");
			LogAssert.IsTrue(taunt.Contains("float requiredPoints = ceilingScore + LeadOverHighest;"),
				"The guarantee clears the score ceiling at the taunter's multiplier floor of 1, so it survives any later heal.");

			LogAssert.IsTrue(taunt.Contains("controller.AggressionState?.OnCombatInitiated?.Invoke(initiator);"),
				"A taunt that seeds the table must fire the empty-to-non-empty edge itself.");
			LogAssert.IsTrue(taunt.Contains("bool wasEmpty = !controller.Aggression.HasAggression;"),
				"Detected before the points are added, like HandleDamaged does.");
		}

		// ── Helpers ──────────────────────────────────────────────────────────────────

		private static string ReadSource(string relativePath)
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), relativePath);
			LogAssert.IsTrue(File.Exists(path), $"{relativePath} not found at {path}.");
			return File.ReadAllText(path);
		}

		private T NewTemplate<T>(string name) where T : ScriptableObject
		{
			T template = ScriptableObject.CreateInstance<T>();
			template.name = name;
			templates.Add(template);
			return template;
		}

		private DamageAttributeTemplate MakeDamageType(string name)
		{
			DamageAttributeTemplate template = NewTemplate<DamageAttributeTemplate>(name);
			template.AddToCache(template.name);
			return template;
		}

		private AbilityObject NewAbilityObject(string name)
		{
			GameObject go = new GameObject(name);
			gameObjects.Add(go);
			return go.AddComponent<AbilityObject>();
		}

		private ICharacter MakeCharacter(string name, int objectId)
		{
			GameObject go = new GameObject(name);
			gameObjects.Add(go);

			FishNet.Object.NetworkObject nob = go.AddComponent<FishNet.Object.NetworkObject>();
			typeof(FishNet.Object.NetworkObject)
				.GetProperty("ObjectId")
				.SetValue(nob, objectId);

			return go.AddComponent<ProbeCharacter>();
		}

		/// <summary>Minimal character exposing a real NetworkObject and a settable ID.</summary>
		private sealed class ProbeCharacter : MonoBehaviour, ICharacter
		{
			public long ID { get; set; }
			public string Name => name;
			public Transform Transform => transform;
			public GameObject GameObject => gameObject;
			public Collider Collider { get; set; }
			public FishNet.Connection.NetworkConnection Owner => null;
			public FishNet.Object.NetworkObject NetworkObject => GetComponent<FishNet.Object.NetworkObject>();
			public FishNet.Managing.Predicting.PredictionManager PredictionManager => null;
			public HashSet<FishNet.Connection.NetworkConnection> Observers { get; } = new HashSet<FishNet.Connection.NetworkConnection>();
			public bool IsTeleporting => false;
			public bool IsSpawned => true;
			public int Flags { get; set; }
			public WorldLabel CharacterNameLabel { get; set; }
			public WorldLabel CharacterGuildLabel { get; set; }
			public Transform MeshRoot => null;
#if !UNITY_SERVER
			public void InstantiateRaceModelFromIndex(RaceTemplate raceTemplate, int modelIndex) { }
			public void InstantiateRaceModelFromIndex(RaceTemplate raceTemplate, int modelIndex, CharacterGender gender) { }
#endif
			public void EnableFlags(CharacterFlags flags) => Flags |= (int)flags;
			public void DisableFlags(CharacterFlags flags) => Flags &= ~(int)flags;
			public bool IsFlagged(CharacterFlags flags) => (Flags & (int)flags) != 0;
			public void RegisterCharacterBehaviour(ICharacterBehaviour characterBehaviour) { }
			public void UnregisterCharacterBehaviour(ICharacterBehaviour characterBehaviour) { }
			public bool TryGet<T>(out T control) where T : class, ICharacterBehaviour { control = null; return false; }
			public void Invoke(List<Trigger> triggers, EventData eventData) { }
		}
	}
}
