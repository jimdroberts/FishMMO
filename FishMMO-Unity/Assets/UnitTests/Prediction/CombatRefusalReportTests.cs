using System;
using System.Collections.Generic;
using System.IO;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using FishNet.Serializing;
using NUnit.Framework;
using UnityEngine;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Pins the "Evade" and "Immune" combat text (optional change O1) and the shared evade gate on
	/// debuffs and knockback (O2).
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>The defect O1 closes.</b> A hit on a leashing NPC or an immortal one was refused on the
	/// server without a word. The evade lives only in the server's brain, so the caster's client
	/// predicted the hit, drew its number, and a second later greyed it out as if the packet had
	/// been lost. The server now reports the refusal — to the attacker alone — and the client turns
	/// the predicted number into the reason.
	/// </para>
	/// <para>
	/// <b>The rule O2 adds.</b> An evading NPC refuses every debuff another character tries to put
	/// on it, and every knockback, through one question (<see cref="CharacterEvade"/>) shared with
	/// the damage path. The live-brain half of that is in <c>AILeashPhaseSweepTests</c>.
	/// </para>
	/// <para>
	/// What needs a spawned NetworkObject or a connected client — the flush's recipient sets, the
	/// label re-text — is asserted on the SOURCE, as the neighbouring audit fixtures do.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class CombatRefusalReportTests
	{
		private readonly List<GameObject> gameObjects = new List<GameObject>();
		private readonly List<ScriptableObject> templates = new List<ScriptableObject>();
		private readonly List<(long Id, CombatEventKind Reason)> refused = new List<(long, CombatEventKind)>();
		private readonly List<long> confirmed = new List<long>();
		private readonly List<long> rejected = new List<long>();

		private const string DAMAGE = "Assets/Scripts/Shared/Implementation/Entity/Prediction/CharacterAttribute/CharacterDamageController.cs";
		private const string ACTIONS = "Assets/Scripts/Shared/Implementation/Entity/ECA/Actions/Character/";
		private const string DISPLAY = "Assets/Scripts/Client/World/ClientCombatDisplay.cs";

		[SetUp]
		public void SetUp()
		{
			PredictedCombatEvents.Clear();
			PredictedCombatEvents.ConfirmationWindowSeconds = 1.0f;
			refused.Clear();
			confirmed.Clear();
			rejected.Clear();
			PredictedCombatEvents.OnPredictionRefused += RecordRefused;
			PredictedCombatEvents.OnPredictionConfirmed += RecordConfirmed;
			PredictedCombatEvents.OnPredictionRejected += RecordRejected;
		}

		[TearDown]
		public void TearDown()
		{
			PredictedCombatEvents.OnPredictionRefused -= RecordRefused;
			PredictedCombatEvents.OnPredictionConfirmed -= RecordConfirmed;
			PredictedCombatEvents.OnPredictionRejected -= RecordRejected;
			PredictedCombatEvents.Clear();

			foreach (GameObject go in gameObjects)
			{
				if (go != null)
				{
					UnityEngine.Object.DestroyImmediate(go);
				}
			}
			gameObjects.Clear();

			foreach (ScriptableObject template in templates)
			{
				if (template != null)
				{
					UnityEngine.Object.DestroyImmediate(template);
				}
			}
			templates.Clear();
		}

		private void RecordRefused(long id, CombatEventKind reason) => refused.Add((id, reason));
		private void RecordConfirmed(long id) => confirmed.Add(id);
		private void RecordRejected(long id) => rejected.Add(id);

		// --- The kinds, as truth tables ------------------------------------------------------------

		[Test]
		public void OnlyEvadeAndImmune_AreRefusals()
		{
			foreach (CombatEventKind kind in (CombatEventKind[])Enum.GetValues(typeof(CombatEventKind)))
			{
				bool expected = kind == CombatEventKind.Evade || kind == CombatEventKind.Immune;
				Assert.AreEqual(expected, kind.IsRefusal(), $"{kind}");
				Assert.AreEqual(!expected, kind.MovesHealth(), $"{kind}: a refusal moves no bar; everything else does");
				Assert.AreEqual(!expected, kind.ReachesBystanders(), $"{kind}: a refusal is the attacker's feedback alone");
			}
		}

		[Test]
		public void TheNewKinds_AppendToTheByte_WithoutMovingAnExistingValue()
		{
			Assert.AreEqual(0, (byte)CombatEventKind.Damage);
			Assert.AreEqual(1, (byte)CombatEventKind.Heal);
			Assert.AreEqual(2, (byte)CombatEventKind.PeriodicDamage);
			Assert.AreEqual(3, (byte)CombatEventKind.PeriodicHeal);
			Assert.AreEqual(4, (byte)CombatEventKind.Evade);
			Assert.AreEqual(5, (byte)CombatEventKind.Immune);
		}

		[Test]
		public void ARefusal_CarriesTheRefusedHitsDamageType_AndAHealCarriesNone()
		{
			Assert.IsTrue(CombatEventKind.Damage.CarriesDamageType());
			Assert.IsTrue(CombatEventKind.PeriodicDamage.CarriesDamageType());
			Assert.IsTrue(CombatEventKind.Evade.CarriesDamageType(),
				"the caster's pending prediction is keyed on the type; a typeless refusal could settle only a typeless prediction");
			Assert.IsTrue(CombatEventKind.Immune.CarriesDamageType());
			Assert.IsFalse(CombatEventKind.Heal.CarriesDamageType());
			Assert.IsFalse(CombatEventKind.PeriodicHeal.CarriesDamageType());
		}

		[Test]
		public void Delivery_IsReliableToTheSourcesOwner_AndNothingToBystandersForARefusal()
		{
			Assert.AreEqual(CombatEventRules.Delivery.Reliable, CombatEventRules.ResolveDelivery(CombatEventKind.Damage, true));
			Assert.AreEqual(CombatEventRules.Delivery.Unreliable, CombatEventRules.ResolveDelivery(CombatEventKind.Damage, false));
			Assert.AreEqual(CombatEventRules.Delivery.Unreliable, CombatEventRules.ResolveDelivery(CombatEventKind.PeriodicHeal, false));
			Assert.AreEqual(CombatEventRules.Delivery.Reliable, CombatEventRules.ResolveDelivery(CombatEventKind.Evade, true));
			Assert.AreEqual(CombatEventRules.Delivery.None, CombatEventRules.ResolveDelivery(CombatEventKind.Evade, false),
				"twenty players on a training dummy must not each see everyone else's Immune");
			Assert.AreEqual(CombatEventRules.Delivery.None, CombatEventRules.ResolveDelivery(CombatEventKind.Immune, false));
		}

		[Test]
		public void WhatARefusedHitReports()
		{
			// immortal, evading, alive -> reported?, kind
			AssertRefusal(false, false, true, false, default);
			AssertRefusal(false, true, true, true, CombatEventKind.Evade);
			AssertRefusal(true, false, true, true, CombatEventKind.Immune);
			AssertRefusal(true, true, true, true, CombatEventKind.Evade);
			// A corpse is immortal only so it cannot die twice; a hit on it is not an immunity.
			AssertRefusal(true, false, false, false, default);
			AssertRefusal(false, true, false, false, default);
		}

		private static void AssertRefusal(bool immortal, bool evading, bool alive, bool reported, CombatEventKind kind)
		{
			bool result = CharacterDamageController.TryResolveRefusalReport(immortal, evading, alive, out CombatEventKind resolved);
			Assert.AreEqual(reported, result, $"immortal={immortal} evading={evading} alive={alive}");
			if (reported)
			{
				Assert.AreEqual(kind, resolved, $"immortal={immortal} evading={evading} alive={alive}");
			}
		}

		// --- The coalescer -------------------------------------------------------------------------

		[Test]
		public void TheCoalescer_KeepsARefusal_WithAZeroAmount_AndCountsIt()
		{
			CombatEventCoalescer coalescer = new CombatEventCoalescer();
			coalescer.Add(7, CombatEventKind.Evade, 3, 0);
			coalescer.Add(7, CombatEventKind.Evade, 3, 250);
			coalescer.Add(7, CombatEventKind.Damage, 3, 0);

			List<CombatEventCoalescer.Entry> flushed = new List<CombatEventCoalescer.Entry>();
			coalescer.Flush(flushed);

			Assert.AreEqual(1, flushed.Count, "a zero-amount DAMAGE entry is still dropped; the refusal is kept");
			Assert.AreEqual(CombatEventKind.Evade, flushed[0].Kind);
			Assert.AreEqual(0, flushed[0].Amount, "a refusal cannot be made to carry an amount");
			Assert.AreEqual(3, flushed[0].DamageTemplateID, "the refused hit's type survives the merge");
			Assert.AreEqual(2, flushed[0].Occurrences, "both refused hits settle a prediction each");
		}

		[Test]
		public void TheCoalescer_NeverMergesARefusalIntoLandedDamage()
		{
			CombatEventCoalescer coalescer = new CombatEventCoalescer();
			coalescer.Add(7, CombatEventKind.Damage, 3, 40);
			coalescer.Add(7, CombatEventKind.Immune, 3, 0);

			List<CombatEventCoalescer.Entry> flushed = new List<CombatEventCoalescer.Entry>();
			coalescer.Flush(flushed);

			Assert.AreEqual(2, flushed.Count);
			foreach (CombatEventCoalescer.Entry entry in flushed)
			{
				if (entry.Kind == CombatEventKind.Damage)
				{
					Assert.AreEqual(40, entry.Amount);
					Assert.AreEqual(1, entry.Occurrences);
				}
				else
				{
					Assert.AreEqual(CombatEventKind.Immune, entry.Kind);
					Assert.AreEqual(0, entry.Amount);
				}
			}
		}

		[Test]
		public void ARefusal_RoundTripsTheWire()
		{
			CombatEventBroadcast sent = new CombatEventBroadcast()
			{
				TargetObjectID = 42,
				SourceObjectID = 7,
				Amount = 0,
				Kind = (byte)CombatEventKind.Immune,
				DamageTemplateID = 99,
				Occurrences = 3,
			};

			Writer writer = new Writer();
			writer.WriteCombatEventBroadcast(sent);
			Reader reader = new Reader(writer.GetArraySegment(), null);
			CombatEventBroadcast read = reader.ReadCombatEventBroadcast();

			Assert.AreEqual((byte)CombatEventKind.Immune, read.Kind);
			Assert.AreEqual(0, read.Amount);
			Assert.AreEqual(99, read.DamageTemplateID);
			Assert.AreEqual(3, read.Occurrences);
			Assert.AreEqual(0, reader.Remaining, "the layout is unchanged: the kind is the same byte it always was");
		}

		// --- The prediction pairing ----------------------------------------------------------------

		[Test]
		public void ARefusal_SettlesTheCastersPredictions_AsRefused()
		{
			ICharacter attacker = MakeCharacter("RefusalAttacker", 1);
			ICharacter target = MakeCharacter("RefusalTarget", 2);
			DamageAttributeTemplate fire = MakeDamageType("RefusalFire");

			PredictedCombatEvents.Predict(attacker, target, 30, PredictedCombatEvents.Kind.Damage, fire, 0f);
			PredictedCombatEvents.Predict(attacker, target, 31, PredictedCombatEvents.Kind.Damage, fire, 0f);

			Assert.IsTrue(PredictedCombatEvents.TryRefuse(attacker, target, CombatEventKind.Evade, 2, fire));
			Assert.AreEqual(0, PredictedCombatEvents.PendingCount, "both refused hits are settled at once, not left to time out");
			Assert.AreEqual(2, refused.Count);
			Assert.AreEqual(CombatEventKind.Evade, refused[0].Reason, "the display is told WHY, so it can say it");
			Assert.AreEqual(0, confirmed.Count, "a refused hit is not a confirmed one");

			PredictedCombatEvents.Sweep(100f);
			Assert.AreEqual(0, rejected.Count, "and it is never greyed out later as if lost");
		}

		[Test]
		public void ARefusal_PairsOnSourceAndType_AndNeverOnAHeal()
		{
			ICharacter attacker = MakeCharacter("PairAttacker", 11);
			ICharacter other = MakeCharacter("PairOther", 12);
			ICharacter target = MakeCharacter("PairTarget", 13);
			DamageAttributeTemplate fire = MakeDamageType("PairFire");
			DamageAttributeTemplate frost = MakeDamageType("PairFrost");

			PredictedCombatEvents.Predict(attacker, target, 30, PredictedCombatEvents.Kind.Damage, fire, 0f);
			PredictedCombatEvents.Predict(attacker, target, 30, PredictedCombatEvents.Kind.Heal, null, 0f);

			Assert.IsFalse(PredictedCombatEvents.TryRefuse(other, target, CombatEventKind.Immune, 1, fire),
				"another player's refusal must not settle this client's prediction");
			Assert.IsFalse(PredictedCombatEvents.TryRefuse(attacker, target, CombatEventKind.Immune, 1, frost),
				"a refusal of a different stream must not settle this one");
			Assert.IsFalse(PredictedCombatEvents.TryRefuse(attacker, target, CombatEventKind.Damage, 1, fire),
				"only a refusal kind refuses");
			Assert.AreEqual(2, PredictedCombatEvents.PendingCount);

			Assert.IsTrue(PredictedCombatEvents.TryRefuse(attacker, target, CombatEventKind.Immune, 5, fire));
			Assert.AreEqual(1, PredictedCombatEvents.PendingCount, "the heal prediction is untouched — heals are never refused");
			Assert.AreEqual(1, refused.Count, "five claimed, one pending: settles what there is and no more");
		}

		[Test]
		public void ARefusalWithNothingPredicted_IsLeftForTheDisplayToDraw()
		{
			ICharacter attacker = MakeCharacter("NothingAttacker", 21);
			ICharacter target = MakeCharacter("NothingTarget", 22);

			Assert.IsFalse(PredictedCombatEvents.TryRefuse(attacker, target, CombatEventKind.Immune),
				"an immortal target refused the hit on the caster's own client too, so no number was drawn; the word is new");
		}

		// --- The shared evade gate -----------------------------------------------------------------

		[Test]
		public void AHostileBuff_IsADebuffFromSomebodyElse()
		{
			Assert.IsTrue(CharacterEvade.IsHostileBuff(true, true));
			Assert.IsFalse(CharacterEvade.IsHostileBuff(true, false), "an NPC's own debuff on itself is its own business");
			Assert.IsFalse(CharacterEvade.IsHostileBuff(false, true), "a buff from an ally is not hostile");
			Assert.IsFalse(CharacterEvade.IsHostileBuff(false, false));
		}

		[Test]
		public void NothingButAnEvadingNpc_RefusesAnything()
		{
			Harness.StubCharacter player = new Harness.StubCharacter { ID = 5 };
			Harness.StubCharacter attacker = new Harness.StubCharacter { ID = 6 };
			AttributeBuffTemplate slow = NewTemplate<AttributeBuffTemplate>("EvadeSlow");
			slow.IsDebuff = true;

			Assert.IsFalse(CharacterEvade.RefusesHostileEffects(null));
			Assert.IsFalse(CharacterEvade.RefusesHostileEffects(player), "a player never evades");
			Assert.AreEqual(0, player.TryGetCalls, "and a hit on a player never pays the brain lookup");
			Assert.IsFalse(CharacterEvade.RefusesBuff(player, slow, attacker));
			Assert.IsFalse(CharacterEvade.RefusesBuff(null, slow, attacker));
			Assert.IsFalse(CharacterEvade.RefusesBuff(player, null, attacker));
		}

		// --- The wiring, by source -----------------------------------------------------------------

		[Test]
		public void ARefusedHit_ReportsItsRefusal_AndNothingElse()
		{
			string damage = CodeOnly(Read(DAMAGE));
			string body = MethodBody(damage, "public int Damage(ICharacter attacker, int amount, DamageAttributeTemplate damageAttribute");

			int refuse = body.IndexOf("if (Immortal || IsEvading())", StringComparison.Ordinal);
			int report = body.IndexOf("ReportRefusal(attacker, damageAttribute);", StringComparison.Ordinal);
			int giveUp = body.IndexOf("return 0;", refuse, StringComparison.Ordinal);
			int combat = body.IndexOf("EnterCombat();", StringComparison.Ordinal);

			LogAssert.IsTrue(refuse >= 0 && report > refuse && giveUp > report && combat > giveUp,
				"the refusal is reported inside the refusal branch, which still returns before combat entry");

			string reportBody = MethodBody(damage, "private void ReportRefusal(ICharacter attacker, DamageAttributeTemplate damageAttribute)");
			LogAssert.IsTrue(reportBody.Contains("TryResolveRefusalReport(Immortal, IsEvading(), IsAlive, out CombatEventKind kind)"),
				"what a refusal reports is decided by the pinned rule");

			string queue = MethodBody(damage, "private void QueueCombatEvent(");
			LogAssert.IsTrue(queue.Contains("(amount <= 0 && !kind.IsRefusal())"),
				"the zero-amount guard lets refusals through and nothing else");

			string evading = MethodBody(damage, "private bool IsEvading()");
			LogAssert.IsTrue(evading.Contains("CharacterEvade.RefusesHostileEffects(Character)"),
				"damage asks the same evade question the debuff and knockback paths ask");
		}

		[Test]
		public void TheFlush_RoutesEachObserverThroughTheRule_AndARefusalOnlyToItsAttacker()
		{
			string damage = CodeOnly(Read(DAMAGE));
			string flush = MethodBody(damage, "private void FlushCombatEvents()");

			LogAssert.IsTrue(flush.Contains("CombatEventRules.ResolveDelivery(entry.Kind,"),
				"every observer's channel comes from the pinned delivery rule");
			LogAssert.IsTrue(flush.Contains("!entry.Kind.ReachesBystanders() && (sourceOwner == null"),
				"a refusal with no connection behind its source is sent to nobody");

			string receive = MethodBody(damage, "private static void OnCombatEventBroadcast(CombatEventBroadcast msg, Channel channel)");
			LogAssert.IsTrue(receive.Contains("eventKind.MovesHealth()"), "a refusal never moves an observer's health bar");
			LogAssert.IsTrue(receive.Contains("kind.CarriesDamageType()"), "a refusal's damage type reaches the pairing");
		}

		[Test]
		public void DebuffsAndKnockback_AskTheSharedEvadeQuestion()
		{
			string buff = CodeOnly(Read(ACTIONS + "ApplyBuffAction.cs"));
			int draw = buff.IndexOf("StacksValue.GetValue(", StringComparison.Ordinal);
			int gate = buff.IndexOf("CharacterEvade.RefusesBuff(target, BuffTemplate, initiator)", StringComparison.Ordinal);
			int apply = buff.IndexOf("buffController.ApplyAuthoritative(", StringComparison.Ordinal);
			int credit = buff.IndexOf("RecordCombatContribution(", StringComparison.Ordinal);
			LogAssert.IsTrue(draw >= 0 && gate > draw,
				"the stacks are drawn before the evade gate, whose answer differs between peers (see AbilityObject.RNG)");
			LogAssert.IsTrue(apply > gate && credit > gate,
				"a refused debuff is neither applied nor credited toward loot rights");

			string knockback = CodeOnly(Read(ACTIONS + "KnockbackHitAction.cs"));
			int evade = knockback.IndexOf("CharacterEvade.RefusesHostileEffects(target)", StringComparison.Ordinal);
			int impulse = knockback.IndexOf("motor.BaseVelocity = currentVelocity", StringComparison.Ordinal);
			LogAssert.IsTrue(evade >= 0 && impulse > evade, "an evading NPC is never displaced");
			LogAssert.IsTrue(knockback.Contains("!defenderDamageController.Immortal"),
				"and Immortal keeps exactly the meaning it had");
		}

		[Test]
		public void TheDisplay_TurnsARefusedPredictionIntoTheWord()
		{
			string display = CodeOnly(Read(DISPLAY));
			string onEvent = MethodBody(display, "private void OnCombatEvent(");
			int refusal = onEvent.IndexOf("if (kind.IsRefusal())", StringComparison.Ordinal);
			int pairing = onEvent.IndexOf("PredictedCombatEvents.TryRefuse(source, target, kind, occurrences, dmg)", StringComparison.Ordinal);
			int confirm = onEvent.IndexOf("PredictedCombatEvents.TryConfirm(", StringComparison.Ordinal);
			LogAssert.IsTrue(refusal >= 0 && pairing > refusal && confirm > pairing,
				"a refusal is settled as refused and never reaches the confirmation pairing");

			string onRefused = MethodBody(display, "private void OnPredictionRefused(long id, CombatEventKind reason)");
			LogAssert.IsTrue(onRefused.Contains("predicted.Label.Lease == predicted.Lease"),
				"the re-text is lease-guarded like the rejection recolor");
			LogAssert.IsTrue(onRefused.Contains("SetText(RefusalText(reason))"), "the number becomes the reason");

			LogAssert.IsTrue(display.Contains("PredictedCombatEvents.OnPredictionRefused += OnPredictionRefused;") &&
				display.Contains("PredictedCombatEvents.OnPredictionRefused -= OnPredictionRefused;"),
				"subscribed and released with the display");
		}

		// --- Helpers -------------------------------------------------------------------------------

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

		/// <summary>
		/// A character with a real NetworkObject whose id is set: the pairing reads the id, and an
		/// unspawned object reports 0, which the tracker refuses.
		/// </summary>
		private ICharacter MakeCharacter(string name, int objectId)
		{
			GameObject go = new GameObject(name);
			gameObjects.Add(go);

			FishNet.Object.NetworkObject nob = go.AddComponent<FishNet.Object.NetworkObject>();
			typeof(FishNet.Object.NetworkObject)
				.GetProperty("ObjectId")
				.SetValue(nob, objectId);

			return go.AddComponent<ProbeRefusalCharacter>();
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

		/// <summary>Minimal character exposing a real NetworkObject, which is all the pairing reads.</summary>
		private sealed class ProbeRefusalCharacter : MonoBehaviour, ICharacter
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
			/// <inheritdoc/>
			public Nameplate CharacterNameplate { get; set; }
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
