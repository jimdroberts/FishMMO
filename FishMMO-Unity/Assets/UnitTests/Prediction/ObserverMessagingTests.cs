using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Object;
using UnityEngine;
using FishNet.Serializing;
using FishMMO.Shared;
using NUnit.Framework;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Covers the server-to-observer messaging that replaced state forwarding.
	/// </summary>
	/// <remarks>
	/// With forwarding off, an observer learns about another character only through these messages,
	/// so the properties asserted here — the owner is never a recipient, a burst of hits becomes one
	/// message per source, a dropped resource packet is repaired, and the party payload survives
	/// quantisation — are the whole of what a peer can see.
	/// </remarks>
	[TestFixture]
	public class ObserverMessagingTests
	{
		// ── ObserverBroadcastScope ──────────────────────────────────────

		/// <summary>
		/// Builds a connection with a distinct identity.
		/// </summary>
		/// <remarks>
		/// <see cref="NetworkConnection"/> compares and hashes by <c>ClientId</c> alone, so two
		/// default-constructed instances are the same connection as far as a <see cref="HashSet{T}"/>
		/// is concerned. Every connection in these tests therefore needs its own id, or the set
		/// silently collapses them and the assertions stop meaning what they read as.
		/// </remarks>
		private static NetworkConnection Connection(int clientId)
		{
			return new NetworkConnection() { ClientId = clientId };
		}

		/// <summary>
		/// The recipient copy must exclude the owner and must not disturb the source set.
		/// </summary>
		/// <remarks>
		/// This is the reason the helper exists. FishNet's own
		/// <c>BroadcastExcept(HashSet, NetworkConnection, ...)</c> calls <c>Remove</c> on the set it
		/// is handed, so passing <c>NetworkObject.Observers</c> straight to it would not exclude the
		/// owner for one message — it would delete the owner from the observer set permanently and
		/// silently stop every later observer message reaching them.
		/// </remarks>
		[Test]
		public void CollectRecipients_DropsOwnerAndNullsWithoutMutatingTheSource()
		{
			NetworkConnection owner = Connection(1);
			NetworkConnection other = Connection(2);

			HashSet<NetworkConnection> observers = new HashSet<NetworkConnection>() { owner, other, null };
			HashSet<NetworkConnection> into = new HashSet<NetworkConnection>();

			int count = ObserverBroadcastScope.CollectRecipients(observers, owner, into);

			Assert.AreEqual(1, count, "Only the non-owner observer should receive the message.");
			Assert.IsTrue(into.Contains(other));
			Assert.IsFalse(into.Contains(owner), "The owner must never be a recipient of an observer message.");
			Assert.AreEqual(3, observers.Count, "The source observer set must not be modified.");
			Assert.IsTrue(observers.Contains(owner), "The owner must still be an observer of its own object.");
		}

		/// <summary>An ownerless object (an NPC) sends to every observer.</summary>
		[Test]
		public void CollectRecipients_WithNoOwner_KeepsEveryObserver()
		{
			NetworkConnection a = Connection(3);
			NetworkConnection b = Connection(4);
			HashSet<NetworkConnection> into = new HashSet<NetworkConnection>();

			int count = ObserverBroadcastScope.CollectRecipients(new HashSet<NetworkConnection>() { a, b }, null, into);

			Assert.AreEqual(2, count);
		}

		/// <summary>A null observer set is a no-op rather than a throw.</summary>
		[Test]
		public void CollectRecipients_WithNullObservers_ReturnsZero()
		{
			HashSet<NetworkConnection> into = new HashSet<NetworkConnection>() { Connection(5) };

			Assert.AreEqual(0, ObserverBroadcastScope.CollectRecipients(null, null, into));
			Assert.AreEqual(0, into.Count, "The destination must be cleared even when there is nothing to collect.");
		}

		// ── CombatEventCoalescer ────────────────────────────────────────

		/// <summary>Repeated hits from one source in a tick become a single number.</summary>
		/// <remarks>
		/// A multi-hit ability, or several damage-over-time stacks expiring on the same tick, would
		/// otherwise put one message on the wire per hit and stack that many labels on top of each
		/// other over the victim.
		/// </remarks>
		[Test]
		public void Coalescer_MergesSameSourceAndType()
		{
			CombatEventCoalescer coalescer = new CombatEventCoalescer();
			coalescer.Add(sourceObjectID: 7, CombatEventKind.Damage, damageTemplateID: 3, amount: 10);
			coalescer.Add(sourceObjectID: 7, CombatEventKind.Damage, damageTemplateID: 3, amount: 5);

			List<CombatEventCoalescer.Entry> flushed = new List<CombatEventCoalescer.Entry>();
			coalescer.Flush(flushed);

			Assert.AreEqual(1, flushed.Count);
			Assert.AreEqual(15, flushed[0].Amount);
			Assert.AreEqual(0, coalescer.Count, "Flush must empty the buffer so the next tick starts clean.");
		}

		/// <summary>Different sources, kinds and damage types stay separate numbers.</summary>
		[Test]
		public void Coalescer_KeepsDistinctSourcesKindsAndTypesApart()
		{
			CombatEventCoalescer coalescer = new CombatEventCoalescer();
			coalescer.Add(1, CombatEventKind.Damage, 3, 10);
			coalescer.Add(2, CombatEventKind.Damage, 3, 10);
			coalescer.Add(1, CombatEventKind.Damage, 4, 10);
			coalescer.Add(1, CombatEventKind.Heal, 0, 10);

			Assert.AreEqual(4, coalescer.Count);
		}

		/// <summary>
		/// Heals are normalised to no damage type, so two heals from one source always merge.
		/// </summary>
		[Test]
		public void Coalescer_NormalisesDamageTypeOnHeals()
		{
			CombatEventCoalescer coalescer = new CombatEventCoalescer();
			coalescer.Add(1, CombatEventKind.Heal, damageTemplateID: 3, amount: 4);
			coalescer.Add(1, CombatEventKind.Heal, damageTemplateID: 9, amount: 6);

			List<CombatEventCoalescer.Entry> flushed = new List<CombatEventCoalescer.Entry>();
			coalescer.Flush(flushed);

			Assert.AreEqual(1, flushed.Count);
			Assert.AreEqual(10, flushed[0].Amount);
			Assert.AreEqual(0, flushed[0].DamageTemplateID);
		}

		/// <summary>Nothing landed means nothing is queued.</summary>
		[Test]
		public void Coalescer_IgnoresNonPositiveAmounts()
		{
			CombatEventCoalescer coalescer = new CombatEventCoalescer();
			coalescer.Add(1, CombatEventKind.Damage, 0, 0);
			coalescer.Add(1, CombatEventKind.Damage, 0, -5);

			Assert.AreEqual(0, coalescer.Count);
		}

		/// <summary>
		/// Past the entry cap the total is preserved rather than the overflow being dropped.
		/// </summary>
		/// <remarks>
		/// A wide area-of-effect tick can involve more distinct sources than the cap. Losing the
		/// attribution of the tail is acceptable; losing the damage from the displayed total is not,
		/// because the number would then disagree with the health bar beside it.
		/// </remarks>
		[Test]
		public void Coalescer_BeyondCapKeepsTheTotal()
		{
			CombatEventCoalescer coalescer = new CombatEventCoalescer();
			int expectedTotal = 0;
			for (int i = 1; i <= CombatEventCoalescer.MaxEntries + 5; ++i)
			{
				coalescer.Add(sourceObjectID: i, CombatEventKind.Damage, damageTemplateID: 1, amount: i);
				expectedTotal += i;
			}

			List<CombatEventCoalescer.Entry> flushed = new List<CombatEventCoalescer.Entry>();
			coalescer.Flush(flushed);

			Assert.LessOrEqual(flushed.Count, CombatEventCoalescer.MaxEntries);
			int total = 0;
			for (int i = 0; i < flushed.Count; ++i)
			{
				total += flushed[i].Amount;
			}
			Assert.AreEqual(expectedTotal, total, "Damage folded into the overflow bucket must still be reported.");
		}

		/// <summary>The combat report survives its wire format unchanged.</summary>
		[Test]
		public void CombatEventBroadcast_RoundTrips()
		{
			CombatEventBroadcast sent = new CombatEventBroadcast()
			{
				TargetObjectID = 42,
				SourceObjectID = 7,
				Amount = 1234,
				Kind = (byte)CombatEventKind.Damage,
				DamageTemplateID = 99,
			};

			Writer writer = new Writer();
			writer.WriteCombatEventBroadcast(sent);

			Reader reader = new Reader(writer.GetArraySegment(), null);
			CombatEventBroadcast read = reader.ReadCombatEventBroadcast();

			Assert.AreEqual(sent.TargetObjectID, read.TargetObjectID);
			Assert.AreEqual(sent.SourceObjectID, read.SourceObjectID);
			Assert.AreEqual(sent.Amount, read.Amount);
			Assert.AreEqual(sent.Kind, read.Kind);
			Assert.AreEqual(sent.DamageTemplateID, read.DamageTemplateID);
			Assert.AreEqual(0, reader.Remaining, "The reader must consume exactly what was written.");
		}

		// ── ObservedResourcePushScheduler ───────────────────────────────

		private static CharacterAttributeResourceState Resources(float health, int maxHealth = 100)
		{
			return new CharacterAttributeResourceState()
			{
				Health = health,
				MaxHealth = maxHealth,
				Mana = 50.0f,
				MaxMana = 50,
				Stamina = 50.0f,
				MaxStamina = 50,
			};
		}

		/// <summary>An unchanging character costs nothing after its first push.</summary>
		[Test]
		public void ResourceScheduler_IdleCharacterStopsSending()
		{
			ObservedResourcePushScheduler scheduler = default;
			CharacterAttributeResourceState state = Resources(100.0f);

			Assert.AreEqual(ObservedResourcePushScheduler.Decision.Push, scheduler.Evaluate(0, state, 6),
				"The first evaluation always pushes: no observer has been told anything yet.");

			// Up to but not including the confirmation, an unchanged character says nothing.
			for (uint tick = 1; tick < ObservedResourcePushScheduler.ConfirmDelayTicks; ++tick)
			{
				Assert.AreEqual(ObservedResourcePushScheduler.Decision.None, scheduler.Evaluate(tick, state, 6));
			}

			Assert.AreEqual(ObservedResourcePushScheduler.Decision.Confirm,
				scheduler.Evaluate(ObservedResourcePushScheduler.ConfirmDelayTicks, state, 6),
				"One confirmation repeats the last value so a dropped unreliable packet is repaired.");

			for (uint tick = ObservedResourcePushScheduler.ConfirmDelayTicks + 1; tick < 200; ++tick)
			{
				Assert.AreEqual(ObservedResourcePushScheduler.Decision.None, scheduler.Evaluate(tick, state, 6),
					"After the single confirmation an idle character must go completely quiet.");
			}
		}

		/// <summary>
		/// The confirmation is what makes a killing blow survive packet loss.
		/// </summary>
		/// <remarks>
		/// A character at zero health does not regenerate, so its resources never change again and
		/// the change gate never reopens. Before the confirmation existed, a single dropped packet
		/// left every observer drawing a corpse as alive for as long as it stayed spawned.
		/// </remarks>
		[Test]
		public void ResourceScheduler_RepeatsTheFinalValueAfterAKillingBlow()
		{
			ObservedResourcePushScheduler scheduler = default;
			scheduler.Evaluate(0, Resources(100.0f), 6);

			CharacterAttributeResourceState dead = Resources(0.0f);
			Assert.AreEqual(ObservedResourcePushScheduler.Decision.Push, scheduler.Evaluate(6, dead, 6));

			ObservedResourcePushScheduler.Decision confirm = ObservedResourcePushScheduler.Decision.None;
			for (uint tick = 7; tick <= 6 + ObservedResourcePushScheduler.ConfirmDelayTicks; ++tick)
			{
				confirm = scheduler.Evaluate(tick, dead, 6);
				if (confirm != ObservedResourcePushScheduler.Decision.None)
				{
					break;
				}
			}

			Assert.AreEqual(ObservedResourcePushScheduler.Decision.Confirm, confirm);
			Assert.AreEqual(0.0f, scheduler.LastPushed.Health, "The repeated value must be the state that was last sent.");
		}

		/// <summary>Changes faster than the interval are rate limited, not dropped.</summary>
		[Test]
		public void ResourceScheduler_RateLimitsRapidChanges()
		{
			ObservedResourcePushScheduler scheduler = default;
			scheduler.Evaluate(0, Resources(100.0f), 6);

			Assert.AreEqual(ObservedResourcePushScheduler.Decision.None, scheduler.Evaluate(1, Resources(99.0f), 6));
			Assert.AreEqual(ObservedResourcePushScheduler.Decision.None, scheduler.Evaluate(5, Resources(98.0f), 6));
			Assert.AreEqual(ObservedResourcePushScheduler.Decision.Push, scheduler.Evaluate(6, Resources(97.0f), 6));
			Assert.AreEqual(97.0f, scheduler.LastPushed.Health, "A rate limited push must carry the newest value, not the one it skipped.");
		}

		/// <summary>Sub-unit regeneration drift is not a change worth a packet.</summary>
		[Test]
		public void ResourceScheduler_IgnoresSubUnitDrift()
		{
			ObservedResourcePushScheduler scheduler = default;
			scheduler.Evaluate(0, Resources(100.0f), 6);

			Assert.AreEqual(ObservedResourcePushScheduler.Decision.None, scheduler.Evaluate(6, Resources(100.4f), 6),
				"An observer draws whole units; a fractional change is invisible and must not be sent.");
		}

		/// <summary>
		/// Entering combat must not be made to wait out a rate limit sized for an idle character.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Out of combat the push interval is widened (12 ticks) because the only thing moving a bar
		/// is regeneration. <c>NextPushTick</c> is an absolute deadline stamped from whatever
		/// interval was in force at the last push, so an idle character carries a deadline up to
		/// twelve ticks out — and the first hit of a fight is exactly the change that arrives while
		/// it is still pending. Without the reset, observers would see an eleven-tick-stale health
		/// bar at the one moment it matters.
		/// </para>
		/// </remarks>
		[Test]
		public void ResourceScheduler_EnteringCombatClearsTheOutOfCombatRateLimit()
		{
			const uint OutOfCombat = 12;
			const uint InCombat = 6;

			ObservedResourcePushScheduler scheduler = default;

			// Idle at full health: the first push stamps a deadline twelve ticks out.
			Assert.AreEqual(ObservedResourcePushScheduler.Decision.Push,
				scheduler.Evaluate(0, Resources(100.0f), OutOfCombat));

			// Hit on tick 1. Under the idle deadline this is refused.
			Assert.AreEqual(ObservedResourcePushScheduler.Decision.None,
				scheduler.Evaluate(1, Resources(60.0f), InCombat),
				"Sanity: without clearing the deadline the hit is held. This is the behaviour being fixed.");

			scheduler.AllowImmediatePush(1);

			Assert.AreEqual(ObservedResourcePushScheduler.Decision.Push,
				scheduler.Evaluate(1, Resources(60.0f), InCombat),
				"Once combat clears the deadline the damage must go out on the tick it lands.");
			Assert.AreEqual(60.0f, scheduler.LastPushed.Health,
				"The push must carry the post-damage value.");
		}

		/// <summary>
		/// Clearing the rate limit must not itself send anything.
		/// </summary>
		/// <remarks>
		/// It lifts the deadline; the change gate still decides. Resending a value the observers
		/// already hold would make every combat entry cost a packet per character whether or not
		/// anything happened.
		/// </remarks>
		[Test]
		public void ResourceScheduler_AllowImmediatePushDoesNotResendAnUnchangedState()
		{
			ObservedResourcePushScheduler scheduler = default;
			scheduler.Evaluate(0, Resources(100.0f), 12);

			scheduler.AllowImmediatePush(1);

			Assert.AreEqual(ObservedResourcePushScheduler.Decision.None,
				scheduler.Evaluate(1, Resources(100.0f), 6),
				"Nothing changed, so nothing may be sent — the deadline was lifted, not the change gate.");
		}

		/// <summary>
		/// Clearing the rate limit must leave the pending loss-repair confirmation intact.
		/// </summary>
		/// <remarks>
		/// The confirmation is what repairs a dropped unreliable packet. Resetting the whole
		/// scheduler on combat entry would drop it, reintroducing the stale-corpse failure for any
		/// character that took a hit shortly after its last push.
		/// </remarks>
		[Test]
		public void ResourceScheduler_AllowImmediatePushKeepsThePendingConfirmation()
		{
			ObservedResourcePushScheduler scheduler = default;
			scheduler.Evaluate(0, Resources(100.0f), 12);

			scheduler.AllowImmediatePush(1);

			Assert.IsTrue(scheduler.ConfirmPending,
				"The confirmation scheduled by the last push must survive; it is the loss repair.");
			Assert.AreEqual(ObservedResourcePushScheduler.Decision.Confirm,
				scheduler.Evaluate(ObservedResourcePushScheduler.ConfirmDelayTicks, Resources(100.0f), 6),
				"The confirmation must still fire on its original schedule.");
		}

		/// <summary>The wider out-of-combat interval must actually rate limit.</summary>
		[Test]
		public void ResourceScheduler_OutOfCombatIntervalHoldsChangesLonger()
		{
			ObservedResourcePushScheduler scheduler = default;
			scheduler.Evaluate(0, Resources(50.0f), 12);

			Assert.AreEqual(ObservedResourcePushScheduler.Decision.None, scheduler.Evaluate(6, Resources(52.0f), 12),
				"Six ticks is inside the twelve-tick out-of-combat interval; regeneration must not push there.");
			Assert.AreEqual(ObservedResourcePushScheduler.Decision.Push, scheduler.Evaluate(12, Resources(54.0f), 12),
				"At twelve ticks the interval has elapsed and the accumulated change goes out.");
			Assert.AreEqual(54.0f, scheduler.LastPushed.Health,
				"The delayed push must carry the newest value, not the one it skipped.");
		}

		/// <summary>A maximum that moves is a change even when the current value has not.</summary>
		[Test]
		public void ResourceScheduler_MaxChangeCounts()
		{
			ObservedResourcePushScheduler scheduler = default;
			scheduler.Evaluate(0, Resources(100.0f, maxHealth: 100), 6);

			Assert.AreEqual(ObservedResourcePushScheduler.Decision.Push,
				scheduler.Evaluate(6, Resources(100.0f, maxHealth: 150), 6),
				"Equipping or buffing maximum health must reach observers; it is what the bar is drawn against.");
		}

		// ── Forwarding is a real switch ─────────────────────────────────

		/// <summary>
		/// Exactly one observer transport owns a character's state at a time.
		/// </summary>
		/// <remarks>
		/// The two systems overlap completely, so the predicates that select between them must be
		/// strict opposites. If both could answer true, equipment would have two writers on one
		/// container and every observed buff would get two effect instances.
		/// </remarks>
		[Test]
		public void ObserverSyncMode_TransportsAreMutuallyExclusive()
		{
			GameObject go = new GameObject("SyncModeProbe");
			try
			{
				NetworkObject nob = go.AddComponent<NetworkObject>();

				SetPrivate(nob, "_enableStateForwarding", false);
				SetPrivate(nob, "_enablePrediction", true);
				Assert.IsTrue(ObserverSyncMode.ShouldBroadcastToObservers(nob),
					"With forwarding off, the broadcasts are the only way observers learn anything.");
				Assert.IsFalse(ObserverSyncMode.ObserversConsumeReconcile(nob),
					"With forwarding off the reconcile never reaches an observer, so nothing may act on it.");

				SetPrivate(nob, "_enableStateForwarding", true);
				Assert.IsFalse(ObserverSyncMode.ShouldBroadcastToObservers(nob),
					"With forwarding on the reconcile already carries this state; broadcasting duplicates it.");
				Assert.IsTrue(ObserverSyncMode.ObserversConsumeReconcile(nob));
			}
			finally
			{
				Object.DestroyImmediate(go);
			}
		}

		/// <summary>
		/// A missing or unspawned object falls to the broadcast side rather than going silent.
		/// </summary>
		[Test]
		public void ObserverSyncMode_NullObjectPrefersTheBroadcastPath()
		{
			Assert.IsTrue(ObserverSyncMode.ShouldBroadcastToObservers(null),
				"Answering false here would silently disable observer sync for a controller whose " +
				"NetworkObject is not assigned yet, which is the harder failure to notice.");
			Assert.IsFalse(ObserverSyncMode.ObserversConsumeReconcile(null));
		}

		/// <summary>Sets a private serialized field by reflection.</summary>
		private static void SetPrivate(object target, string field, object value)
		{
			System.Reflection.FieldInfo fi = target.GetType().GetField(field,
				System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
			Assert.IsNotNull(fi, $"Field '{field}' not found on {target.GetType().Name}; FishNet may have renamed it.");
			fi.SetValue(target, value);
		}

		// ── Observed attribute delta ────────────────────────────────────

		/// <summary>The attribute update survives its wire format, entries and flag intact.</summary>
		[Test]
		public void AttributesBroadcast_RoundTrips()
		{
			CharacterAttributesBroadcast sent = new CharacterAttributesBroadcast()
			{
				CharacterObjectID = 11,
				IsFullSet = false,
				Attributes = new AttributeReconcileEntry[]
				{
					new AttributeReconcileEntry() { TemplateID = 5, Value = 30, ExternalModifier = 7 },
					new AttributeReconcileEntry() { TemplateID = 900001, Value = -4, ExternalModifier = 0 },
				},
			};

			Writer writer = new Writer();
			writer.WriteCharacterAttributesBroadcast(sent);

			Reader reader = new Reader(writer.GetArraySegment(), null);
			CharacterAttributesBroadcast read = reader.ReadCharacterAttributesBroadcast();

			Assert.AreEqual(sent.CharacterObjectID, read.CharacterObjectID);
			Assert.IsFalse(read.IsFullSet);
			Assert.AreEqual(2, read.Attributes.Length);
			Assert.AreEqual(sent.Attributes[0], read.Attributes[0]);
			Assert.AreEqual(sent.Attributes[1], read.Attributes[1]);
			Assert.AreEqual(0, reader.Remaining, "The reader must consume exactly what was written.");
		}

		/// <summary>An empty or null set is a legal message, not a malformed one.</summary>
		/// <remarks>
		/// The sender never emits one — it returns early instead — but the reader must not treat a
		/// zero count as corruption, because that is also what a full set on a character with no
		/// attributes looks like.
		/// </remarks>
		[Test]
		public void AttributesBroadcast_EmptyAndNullSetsAreLegal()
		{
			Writer writer = new Writer();
			writer.WriteCharacterAttributesBroadcast(new CharacterAttributesBroadcast()
			{
				CharacterObjectID = 3,
				IsFullSet = true,
				Attributes = null,
			});

			Reader reader = new Reader(writer.GetArraySegment(), null);
			CharacterAttributesBroadcast read = reader.ReadCharacterAttributesBroadcast();

			Assert.IsTrue(read.IsFullSet);
			Assert.IsNotNull(read.Attributes, "A null array must read back as empty rather than null.");
			Assert.AreEqual(0, read.Attributes.Length);
			Assert.AreEqual(0, reader.Remaining);
		}

		/// <summary>
		/// Only the attributes that moved are carried — one equip is one entry, not the whole sheet.
		/// </summary>
		/// <remarks>
		/// This is the property that replaces what the forwarded reconcile used to do for observers.
		/// Asserted against the same diff shape the controller pushes: equal length means compare
		/// position by position, since both sides are ordered by template id.
		/// </remarks>
		[Test]
		public void AttributeDiff_CarriesOnlyChangedEntries()
		{
			AttributeReconcileEntry[] before = new AttributeReconcileEntry[]
			{
				new AttributeReconcileEntry() { TemplateID = 1, Value = 10, ExternalModifier = 0 },
				new AttributeReconcileEntry() { TemplateID = 2, Value = 20, ExternalModifier = 0 },
				new AttributeReconcileEntry() { TemplateID = 3, Value = 30, ExternalModifier = 0 },
			};
			AttributeReconcileEntry[] after = new AttributeReconcileEntry[]
			{
				new AttributeReconcileEntry() { TemplateID = 1, Value = 10, ExternalModifier = 0 },
				new AttributeReconcileEntry() { TemplateID = 2, Value = 20, ExternalModifier = 15 },
				new AttributeReconcileEntry() { TemplateID = 3, Value = 30, ExternalModifier = 0 },
			};

			List<AttributeReconcileEntry> changed = new List<AttributeReconcileEntry>();
			for (int i = 0; i < after.Length; ++i)
			{
				if (!after[i].Equals(before[i]))
				{
					changed.Add(after[i]);
				}
			}

			Assert.AreEqual(1, changed.Count, "Only the buffed attribute should travel.");
			Assert.AreEqual(2, changed[0].TemplateID);
			Assert.AreEqual(15, changed[0].ExternalModifier,
				"The modifier is what makes the receiver's FinalValue match the server's.");

			Writer writer = new Writer();
			writer.WriteCharacterAttributesBroadcast(new CharacterAttributesBroadcast()
			{
				CharacterObjectID = 1,
				IsFullSet = false,
				Attributes = changed.ToArray(),
			});
			int deltaBytes = writer.Position;

			Writer fullWriter = new Writer();
			fullWriter.WriteCharacterAttributesBroadcast(new CharacterAttributesBroadcast()
			{
				CharacterObjectID = 1,
				IsFullSet = true,
				Attributes = after,
			});

			Assert.Less(deltaBytes, fullWriter.Position,
				"A one-attribute change must cost less than resending the sheet, or the delta is pointless.");
		}

		/// <summary>A count past the cap is discarded rather than allocated.</summary>
		[Test]
		public void AttributesBroadcast_RejectsAnImpossibleCount()
		{
			Writer writer = new Writer();
			writer.WriteInt32(1);
			writer.WriteBoolean(false);
			writer.WriteUInt16((ushort)(CharacterAttributesBroadcastSerializer.MAX_ATTRIBUTES + 1));

			Reader reader = new Reader(writer.GetArraySegment(), null);
			CharacterAttributesBroadcast read = reader.ReadCharacterAttributesBroadcast();

			Assert.IsNotNull(read.Attributes);
			Assert.AreEqual(0, read.Attributes.Length, "An over-large count must yield no entries.");
		}

		// ── Party vitals quantisation ───────────────────────────────────

		/// <summary>Fractions survive the byte round trip closely enough to draw.</summary>
		[Test]
		public void PartyVitals_FractionQuantisationIsAccurateEnoughToDraw()
		{
			Assert.AreEqual(0, PartyVitalsQuantiser.FractionToByte(0.0f));
			Assert.AreEqual(255, PartyVitalsQuantiser.FractionToByte(1.0f));
			Assert.AreEqual(0.0f, PartyVitalsQuantiser.ByteToFraction(0));
			Assert.AreEqual(1.0f, PartyVitalsQuantiser.ByteToFraction(255));

			for (float f = 0.0f; f <= 1.0f; f += 0.01f)
			{
				float round = PartyVitalsQuantiser.ByteToFraction(PartyVitalsQuantiser.FractionToByte(f));
				Assert.Less(System.Math.Abs(round - f), 0.005f, $"Fraction {f} lost too much precision.");
			}
		}

		/// <summary>Out-of-range and NaN inputs clamp instead of wrapping.</summary>
		/// <remarks>
		/// A percentage can arrive above one from an over-heal, and NaN from a maximum of zero on a
		/// character whose attributes have not finished loading. Wrapping either into a byte would
		/// draw a full bar on a dying party member.
		/// </remarks>
		[Test]
		public void PartyVitals_QuantisationClampsHostileInputs()
		{
			Assert.AreEqual(0, PartyVitalsQuantiser.FractionToByte(float.NaN));
			Assert.AreEqual(0, PartyVitalsQuantiser.FractionToByte(-5.0f));
			Assert.AreEqual(255, PartyVitalsQuantiser.FractionToByte(17.0f));

			Assert.AreEqual(0, PartyVitalsQuantiser.RateToUInt16(float.NaN));
			Assert.AreEqual(0, PartyVitalsQuantiser.RateToUInt16(-1.0f));
			Assert.AreEqual(ushort.MaxValue, PartyVitalsQuantiser.RateToUInt16(1e9f));
			Assert.AreEqual(1234, PartyVitalsQuantiser.RateToUInt16(1234.4f));
		}
	}
}
