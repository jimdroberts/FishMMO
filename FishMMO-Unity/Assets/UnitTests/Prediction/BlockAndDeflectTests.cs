using System.Collections.Generic;
using System.IO;
using System.Reflection;
using FishNet.Connection;
using FishNet.Managing.Predicting;
using FishNet.Object;
using FishNet.Serializing;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using NUnit.Framework;
using UnityEngine;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Covers block and deflect: the mitigation arithmetic, the charge pool that makes a shield
	/// consumable, the reconcile field that keeps the owner's copy honest, and the peer
	/// rules that decide who may spend it.
	/// </summary>
	/// <remarks>
	/// Everything here runs against real <see cref="Buff"/> instances and real templates registered
	/// in the template cache, because the whole point of the design is that a shield is a buff and
	/// nothing in the ability system knows otherwise. What cannot be built in EditMode — a spawned
	/// ability object mid-flight, a rewind scope, two connected peers — is asserted on the source
	/// instead, and each of those tests says so.
	/// </remarks>
	[TestFixture]
	public class BlockAndDeflectTests
	{
		private DamageNegationBuffTemplate absorb;
		private DamageNegationBuffTemplate reduce;
		private DamageNegationBuffTemplate immune;
		private DeflectBuffTemplate deflect;
		private readonly List<GameObject> gameObjects = new List<GameObject>();

		[SetUp]
		public void SetUp()
		{
			absorb = ScriptableObject.CreateInstance<DamageNegationBuffTemplate>();
			absorb.name = "BlockDeflect_Absorb";
			absorb.Mode = DamageNegationMode.Absorb;
			absorb.Amount = 100;
			absorb.RequiresFacing = false;
			absorb.Duration = 10f;
			absorb.AddToCache(absorb.name);

			reduce = ScriptableObject.CreateInstance<DamageNegationBuffTemplate>();
			reduce.name = "BlockDeflect_Reduce";
			reduce.Mode = DamageNegationMode.Reduce;
			reduce.Amount = 50;
			reduce.RequiresFacing = false;
			reduce.Duration = 10f;
			reduce.AddToCache(reduce.name);

			immune = ScriptableObject.CreateInstance<DamageNegationBuffTemplate>();
			immune.name = "BlockDeflect_Immune";
			immune.Mode = DamageNegationMode.Immune;
			immune.RequiresFacing = false;
			immune.Duration = 10f;
			immune.AddToCache(immune.name);

			deflect = ScriptableObject.CreateInstance<DeflectBuffTemplate>();
			deflect.name = "BlockDeflect_Deflect";
			deflect.DeflectAngleDegrees = 120f;
			deflect.MaxDeflections = 0;
			deflect.Duration = 10f;
			deflect.AddToCache(deflect.name);
		}

		[TearDown]
		public void TearDown()
		{
			foreach (DamageNegationBuffTemplate t in new[] { absorb, reduce, immune })
			{
				t.RemoveFromCache();
				Object.DestroyImmediate(t);
			}
			deflect.RemoveFromCache();
			Object.DestroyImmediate(deflect);

			for (int i = 0; i < gameObjects.Count; ++i)
			{
				if (gameObjects[i] != null)
				{
					Object.DestroyImmediate(gameObjects[i]);
				}
			}
			gameObjects.Clear();
		}

		// ── The mitigation arithmetic ────────────────────────────────────────────────

		/// <summary>
		/// An absorb shield takes what it holds and no more, and reports the shortfall honestly.
		/// </summary>
		[Test]
		public void Absorb_TakesAtMostThePool()
		{
			LogAssert.AreEqual(40, absorb.ResolveNegation(40, 100),
				"A hit smaller than the pool is absorbed whole.");
			LogAssert.AreEqual(100, absorb.ResolveNegation(250, 100),
				"A hit larger than the pool is absorbed only as far as the pool goes — the rest must land.");
			LogAssert.AreEqual(0, absorb.ResolveNegation(40, 0),
				"An empty pool absorbs nothing rather than going negative.");
		}

		/// <summary>
		/// A percentage reduction rounds DOWN, so chip damage still gets through.
		/// </summary>
		/// <remarks>
		/// Rounding up would let a 50% block reduce a 1-damage hit to zero, which turns a partial
		/// block into an immunity against exactly the hits a player is least willing to see ignored.
		/// </remarks>
		[Test]
		public void Reduce_RoundsDownAndClampsThePercentage()
		{
			LogAssert.AreEqual(50, reduce.ResolveNegation(100, 0), "50% of 100 is 50.");
			LogAssert.AreEqual(0, reduce.ResolveNegation(1, 0),
				"50% of 1 rounds down to 0 negated, so the hit still lands for 1.");

			reduce.Amount = 500;
			LogAssert.AreEqual(80, reduce.ResolveNegation(80, 0),
				"A percentage above 100 is clamped, so negation can never exceed the incoming damage.");

			reduce.Amount = -20;
			LogAssert.AreEqual(0, reduce.ResolveNegation(80, 0),
				"A negative percentage is clamped to zero rather than ADDING damage.");
		}

		/// <summary>Immunity takes the whole hit whatever its size, and spends nothing.</summary>
		[Test]
		public void Immune_TakesTheWholeHit()
		{
			LogAssert.AreEqual(999, immune.ResolveNegation(999, 0),
				"Immunity negates the hit outright and does not need a pool to do it.");
			LogAssert.AreEqual(0, immune.InitialCharges,
				"Immunity must report no charges, or Buff.IsSpent would end it the first time anything asked.");
		}

		// ── The charge pool ──────────────────────────────────────────────────────────

		/// <summary>
		/// A fresh absorb buff arrives with a full pool; a reduce buff arrives with none and is
		/// never considered spent.
		/// </summary>
		/// <remarks>
		/// The second half is the one that bites: <see cref="Buff.IsSpent"/> asks the TEMPLATE
		/// whether charges were ever the point, so a duration-only shield sitting at zero must not
		/// be mistaken for an exhausted one and removed on its first hit.
		/// </remarks>
		[Test]
		public void FreshBuff_SeedsChargesFromTheTemplate()
		{
			Buff absorbBuff = new Buff(absorb.ID, 100u, 1f / 30f);
			LogAssert.AreEqual(100, absorbBuff.RemainingCharges, "An absorb shield starts full.");
			LogAssert.IsFalse(absorbBuff.IsSpent, "A full shield is not spent.");

			Buff reduceBuff = new Buff(reduce.ID, 100u, 1f / 30f);
			LogAssert.AreEqual(0, reduceBuff.RemainingCharges, "A percentage block holds no pool.");
			LogAssert.IsFalse(reduceBuff.IsSpent,
				"A template that counts nothing must never report itself spent, or it would be removed " +
				"the first time damage was resolved against it.");
		}

		/// <summary>Spending is bounded by what is held and ends the buff at exactly zero.</summary>
		[Test]
		public void SpendCharges_IsBoundedAndEndsAtZero()
		{
			Buff buff = new Buff(absorb.ID, 100u, 1f / 30f);

			LogAssert.AreEqual(30, buff.SpendCharges(30), "Spends what was asked while the pool covers it.");
			LogAssert.AreEqual(70, buff.RemainingCharges, "The pool drops by what was spent.");
			LogAssert.IsFalse(buff.IsSpent, "Still holding charge.");

			LogAssert.AreEqual(70, buff.SpendCharges(500),
				"A request beyond the pool spends the remainder and reports that, so the caller knows " +
				"how much damage was NOT absorbed.");
			LogAssert.AreEqual(0, buff.RemainingCharges, "The pool never goes negative.");
			LogAssert.IsTrue(buff.IsSpent, "A pool at zero has done its job and the buff must disappear.");

			LogAssert.AreEqual(0, buff.SpendCharges(10), "An empty pool spends nothing.");
		}

		/// <summary>
		/// The restore constructor must NOT refill the pool — it is handed the server's remainder.
		/// </summary>
		/// <remarks>
		/// This is the difference between a reconcile that corrects a shield and one that hands the
		/// owner a full shield every tick, which would make an absorb barrier unkillable.
		/// </remarks>
		[Test]
		public void RestoreConstructor_DoesNotRefillThePool()
		{
			Buff restored = new Buff(absorb.ID, 200u, 210u, 1f / 30f, 0, 0);
			LogAssert.AreEqual(0, restored.RemainingCharges,
				"The restore constructor leaves the pool alone so the caller can assign the server's value; " +
				"seeding a full one here would make a spent shield come back on every reconcile.");

			restored.RemainingCharges = 25;
			LogAssert.AreEqual(25, restored.RemainingCharges, "The caller's value survives.");
		}

		// ── Reconcile coverage ───────────────────────────────────────────────────────

		/// <summary>
		/// <see cref="BuffReconcileEntry"/> carries the pool through its wire format and its
		/// equality, or the delta serialiser would never notice a shield being spent.
		/// </summary>
		/// <remarks>
		/// <see cref="BuffReconcileEntry.WriteArrayDelta"/> decides what to send by comparing entries
		/// with <c>Equals</c>. A field left out of that comparison is a field that never reaches the
		/// owner, however faithfully it is written.
		/// </remarks>
		[Test]
		public void ReconcileEntry_CarriesTheChargePool()
		{
			BuffReconcileEntry entry = new BuffReconcileEntry
			{
				TemplateID = 7,
				ExpiryTick = 400u,
				NextTickTick = 410u,
				Stacks = 1,
				TickCount = 2,
				CumulativeTickMultiplier = 3,
				RemainingCharges = 64,
			};

			BuffReconcileEntry spent = entry;
			spent.RemainingCharges = 12;
			LogAssert.IsFalse(entry.Equals(spent),
				"Two entries differing only in RemainingCharges must compare unequal, or WriteArrayDelta " +
				"would decide nothing changed and the owner would never learn its shield was spent.");

			Writer writer = new Writer();
			entry.WriteTo(writer);
			Reader reader = new Reader(writer.GetArraySegment(), null);
			BuffReconcileEntry round = BuffReconcileEntry.ReadFrom(reader);

			LogAssert.AreEqual(0, reader.Remaining,
				"WriteTo and ReadFrom must agree on the entry's width; a mismatch misaligns every entry after it.");
			LogAssert.IsTrue(entry.Equals(round), "The entry must survive a wire round trip intact.");
			LogAssert.AreEqual(64, round.RemainingCharges, "Including the pool.");
		}

		/// <summary>
		/// The absolute reconcile snapshot writes the pool too, not just the index-delta path.
		/// </summary>
		/// <remarks>
		/// <c>CharacterReconcileDataDeltaSerializer</c> writes buff fields INLINE in its full
		/// snapshot rather than calling <see cref="BuffReconcileEntry.WriteTo"/>, so a field added to
		/// the entry is covered in one path and silently missing from the other. FishNet emits that
		/// absolute form once a second, and it is what a fresh observer decodes from — a field
		/// missing there misaligns everything after the buff array.
		/// </remarks>
		[Test]
		public void AbsoluteReconcileSnapshot_RoundTripsTheChargePool()
		{
			CharacterReconcileData data = default;
			data.Buffs = new[]
			{
				new BuffReconcileEntry
				{
					TemplateID = 11,
					ExpiryTick = 900u,
					NextTickTick = 905u,
					Stacks = 0,
					TickCount = 0,
					CumulativeTickMultiplier = 0,
					RemainingCharges = 250,
				},
			};

			Writer writer = new Writer();
			writer.WriteCharacterReconcileData(data);
			Reader reader = new Reader(writer.GetArraySegment(), null);
			CharacterReconcileData round = reader.ReadCharacterReconcileData();

			LogAssert.IsNotNull(round.Buffs, "The buff array must survive the absolute snapshot.");
			LogAssert.AreEqual(1, round.Buffs.Length, "One entry in, one entry out.");
			LogAssert.AreEqual(250, round.Buffs[0].RemainingCharges,
				"The absolute snapshot must carry RemainingCharges. It writes buff fields inline rather " +
				"than through BuffReconcileEntry.WriteTo, so this path has to be pinned separately.");
			LogAssert.AreEqual(11, round.Buffs[0].TemplateID,
				"And every field after the pool must still line up.");
		}

		// ── DamageMitigation.Negate ──────────────────────────────────────────────────

		/// <summary>An absorb shield eats the hit, drains, and is removed at zero.</summary>
		[Test]
		public void Negate_AbsorbDrainsAndRemovesTheBuffAtZero()
		{
			MitigationCharacter defender = NewCharacter("defender");
			Buff shield = AddBuff(defender, absorb);

			LogAssert.AreEqual(0, DamageMitigation.Negate(defender, null, 60, mutate: true),
				"A 60 hit against a 100 shield lands for nothing.");
			LogAssert.AreEqual(40, shield.RemainingCharges, "And drains the shield by 60.");
			LogAssert.IsTrue(defender.Buffs.Buffs.ContainsKey(absorb.ID), "40 left, so the shield stays up.");

			LogAssert.AreEqual(20, DamageMitigation.Negate(defender, null, 60, mutate: true),
				"The next 60 is absorbed only as far as the remaining 40 goes; 20 lands.");
			LogAssert.AreEqual(0, shield.RemainingCharges, "The pool is empty.");
			LogAssert.IsFalse(defender.Buffs.Buffs.ContainsKey(absorb.ID),
				"An emptied shield disappears — the behaviour the whole charge pool exists for.");
			LogAssert.IsTrue(defender.Buffs.RemoveCalls > 0,
				"And it goes through IBuffController.Remove, so the strip, the FX and the observer push " +
				"all hear about it exactly as they would for an expiry.");
		}

		/// <summary>Immunity wins outright and never lets a shield spend on damage that was not landing.</summary>
		/// <remarks>
		/// The ordering claim is the point. If absorb ran first the shield would drain against a hit
		/// the immunity was going to negate anyway, and a player who blocked and then went immune
		/// would come out of it with an empty barrier.
		/// </remarks>
		[Test]
		public void Negate_ImmunityRunsBeforeAbsorbAndSpendsNothing()
		{
			MitigationCharacter defender = NewCharacter("defender");
			Buff shield = AddBuff(defender, absorb);
			AddBuff(defender, immune);

			LogAssert.AreEqual(0, DamageMitigation.Negate(defender, null, 500, mutate: true),
				"Immunity negates the whole hit.");
			LogAssert.AreEqual(100, shield.RemainingCharges,
				"And the absorb pool is untouched: it must not spend on damage that was never going to land.");
		}

		/// <summary>A percentage comes off the full hit, before any pool sees it.</summary>
		[Test]
		public void Negate_ReduceAppliesToTheFullHitThenAbsorbTakesTheRest()
		{
			MitigationCharacter defender = NewCharacter("defender");
			Buff shield = AddBuff(defender, absorb);
			AddBuff(defender, reduce);

			LogAssert.AreEqual(0, DamageMitigation.Negate(defender, null, 120, mutate: true),
				"50% of 120 is 60 reduced; the 100-point shield absorbs the remaining 60, so nothing lands.");
			LogAssert.AreEqual(40, shield.RemainingCharges,
				"The shield spent 60 — what was left after the percentage, not the full 120.");
		}

		/// <summary>
		/// A peer that does not simulate this defender may read the number but must not spend the pool.
		/// </summary>
		/// <remarks>
		/// This is what lets the caster's own client predict a blocked hit honestly without draining
		/// somebody else's shield on its own copy of them.
		/// </remarks>
		[Test]
		public void Negate_WithoutMutateReadsTheNumberButSpendsNothing()
		{
			MitigationCharacter defender = NewCharacter("defender");
			Buff shield = AddBuff(defender, absorb);

			LogAssert.AreEqual(0, DamageMitigation.Negate(defender, null, 60, mutate: false),
				"The reduction is computed the same way whoever is asking.");
			LogAssert.AreEqual(100, shield.RemainingCharges,
				"But a peer that does not own the simulation must not drain the pool.");
			LogAssert.AreEqual(0, defender.Buffs.MarkDirtyCalls,
				"And must not dirty a reconcile snapshot it has no business changing.");
		}

		/// <summary>A spend has to dirty the snapshot or the server never reports it.</summary>
		/// <remarks>
		/// <c>CreateReconcileSnapshot</c> serves a cached array while nothing is dirty, and mitigation
		/// mutates the <see cref="Buff"/> instance directly — outside every path the controller marks
		/// for itself.
		/// </remarks>
		[Test]
		public void Negate_MarksTheSnapshotDirtyWhenItSpends()
		{
			MitigationCharacter defender = NewCharacter("defender");
			AddBuff(defender, absorb);

			DamageMitigation.Negate(defender, null, 10, mutate: true);
			LogAssert.IsTrue(defender.Buffs.MarkDirtyCalls > 0,
				"Spending a pool must invalidate the reconcile snapshot, or the owner never learns its " +
				"shield moved and re-spends the same damage on every replayed tick.");
		}

		/// <summary>A facing-gated shield stops the front and not the back.</summary>
		[Test]
		public void Negate_FacingGateStopsOnlyWhatArrivesInFront()
		{
			absorb.RequiresFacing = true;
			absorb.FacingAngleDegrees = 120f;

			MitigationCharacter defender = NewCharacter("defender");
			defender.Transform.rotation = Quaternion.identity; // facing +Z
			AddBuff(defender, absorb);

			MitigationCharacter front = NewCharacter("front");
			front.Transform.position = new Vector3(0f, 0f, 5f);
			LogAssert.AreEqual(0, DamageMitigation.Negate(defender, front, 40, mutate: false),
				"An attacker dead ahead is inside the 120 degree guard.");

			MitigationCharacter behind = NewCharacter("behind");
			behind.Transform.position = new Vector3(0f, 0f, -5f);
			LogAssert.AreEqual(40, DamageMitigation.Negate(defender, behind, 40, mutate: false),
				"An attacker behind is outside it, and the hit lands in full — a shield held forward " +
				"does not stop the arrow in the back.");
		}

		/// <summary>
		/// A facing-gated shield does not stop damage with no attacker behind it.
		/// </summary>
		/// <remarks>
		/// Environmental damage, damage-over-time and anything else with no position is exactly what
		/// a directional guard should not block, and answering "no direction, so no negation" is the
		/// conservative reading.
		/// </remarks>
		[Test]
		public void Negate_FacingGatedBuffIgnoresDamageWithNoAttacker()
		{
			absorb.RequiresFacing = true;
			MitigationCharacter defender = NewCharacter("defender");
			AddBuff(defender, absorb);

			LogAssert.AreEqual(40, DamageMitigation.Negate(defender, null, 40, mutate: true),
				"A shield held in one direction cannot block a hit that came from no direction.");
		}

		/// <summary>Two shields spend in template order, which every peer walks identically.</summary>
		/// <remarks>
		/// <c>IBuffController.Buffs</c> is a SortedDictionary keyed by template id, so the sequence is
		/// agreed without anything sorting it — and which shield empties first is observable.
		/// </remarks>
		[Test]
		public void Negate_TwoShieldsSpendInTemplateOrder()
		{
			DamageNegationBuffTemplate second = ScriptableObject.CreateInstance<DamageNegationBuffTemplate>();
			try
			{
				second.name = "BlockDeflect_Absorb2";
				second.Mode = DamageNegationMode.Absorb;
				second.Amount = 100;
				second.RequiresFacing = false;
				second.Duration = 10f;
				second.AddToCache(second.name);

				MitigationCharacter defender = NewCharacter("defender");
				Buff first = AddBuff(defender, absorb);
				Buff other = AddBuff(defender, second);

				Buff lower = absorb.ID < second.ID ? first : other;
				Buff higher = absorb.ID < second.ID ? other : first;

				LogAssert.AreEqual(0, DamageMitigation.Negate(defender, null, 150, mutate: true),
					"200 points of shield stop a 150 hit outright.");
				LogAssert.AreEqual(0, lower.RemainingCharges,
					"The lower template id is walked first and is emptied first.");
				LogAssert.AreEqual(50, higher.RemainingCharges,
					"The second shield takes only the remainder.");
			}
			finally
			{
				second.RemoveFromCache();
				Object.DestroyImmediate(second);
			}
		}

		// ── The shield volume ────────────────────────────────────────────────────────

		/// <summary>Each shape covers what it should and nothing beyond it.</summary>
		[Test]
		public void ShieldVolume_ContainsMatchesTheAuthoredShape()
		{
			ShieldVolume sphere = new ShieldVolume
			{
				Shape = ShieldShape.Sphere,
				LocalCenter = new Vector3(0f, 1f, 1f),
				Radius = 0.5f,
			};
			LogAssert.IsTrue(sphere.Contains(new Vector3(0f, 1f, 1.4f)), "Just inside the sphere.");
			LogAssert.IsFalse(sphere.Contains(new Vector3(0f, 1f, 1.6f)), "Just outside it.");
			LogAssert.IsFalse(sphere.Contains(new Vector3(0f, 0f, 1f)),
				"A metre below the shield is outside it — which is the whole point of a volume over an arc: " +
				"a shot at ankle height is not blocked by a shield held at the chest.");

			ShieldVolume box = new ShieldVolume
			{
				Shape = ShieldShape.Box,
				LocalCenter = new Vector3(0f, 1f, 0.75f),
				Size = new Vector3(1.2f, 1.4f, 0.2f),
			};
			LogAssert.IsTrue(box.Contains(new Vector3(0.5f, 1.5f, 0.75f)), "Inside the box face.");
			LogAssert.IsFalse(box.Contains(new Vector3(0.7f, 1f, 0.75f)),
				"Past the box's half width — a wide shield and a narrow one are different objects.");
			LogAssert.IsFalse(box.Contains(new Vector3(0f, 1f, 0f)),
				"At the character's own centre, well behind the shield's depth.");

			ShieldVolume capsule = new ShieldVolume
			{
				Shape = ShieldShape.Capsule,
				LocalCenter = new Vector3(0f, 1f, 0.8f),
				Radius = 0.3f,
				Height = 1.6f,
			};
			LogAssert.IsTrue(capsule.Contains(new Vector3(0f, 1.7f, 0.8f)), "High on the capsule's segment.");
			LogAssert.IsTrue(capsule.Contains(new Vector3(0f, 0.3f, 0.8f)), "Low on it.");
			LogAssert.IsFalse(capsule.Contains(new Vector3(0f, 1f, 1.2f)), "Beyond its radius.");
		}

		/// <summary>A shape with no size blocks nothing rather than blocking a plane.</summary>
		[Test]
		public void ShieldVolume_WithNoExtentIsInactive()
		{
			LogAssert.IsFalse(new ShieldVolume().IsActive,
				"The default is None, so a negation buff is a ward until somebody gives it dimensions.");
			LogAssert.IsFalse(new ShieldVolume { Shape = ShieldShape.Sphere, Radius = 0f }.IsActive,
				"A zero radius is not a shield.");
			LogAssert.IsFalse(new ShieldVolume { Shape = ShieldShape.Box, Size = new Vector3(1f, 1f, 0f) }.IsActive,
				"A box with a zero axis is a plane, and blocking exactly the points on a plane is never what " +
				"an author meant.");
			LogAssert.IsFalse(new ShieldVolume { Shape = ShieldShape.None, Radius = 5f }.Contains(Vector3.zero),
				"Shape None blocks nothing however generous the other dimensions are.");
		}

		/// <summary>
		/// The volume test is in LOCAL space, which is what keeps it honest across a rewind.
		/// </summary>
		/// <remarks>
		/// Hits are dispatched after the rewind scope closes, so a world-space test would compare a
		/// rewound impact point against a live shield. Here the same local point gives the same
		/// answer no matter where in the world — or in the past — the character was standing, which
		/// is the property that makes the gate safe outside the scope.
		/// </remarks>
		[Test]
		public void ShieldVolume_IsIndependentOfWhereTheCharacterStands()
		{
			ShieldVolume shield = new ShieldVolume
			{
				Shape = ShieldShape.Box,
				LocalCenter = new Vector3(0f, 1f, 0.75f),
				Size = new Vector3(1.2f, 1.4f, 0.2f),
			};
			Vector3 localHit = new Vector3(0.2f, 1.3f, 0.75f);

			LogAssert.IsTrue(shield.Contains(localHit), "Covered at the origin.");

			GameObject go = new GameObject("moved");
			gameObjects.Add(go);
			go.transform.SetPositionAndRotation(new Vector3(37f, -12f, 105f), Quaternion.Euler(0f, 143f, 0f));

			LogAssert.IsTrue(shield.Contains(localHit),
				"And covered identically after the character has moved and turned. The test reads no " +
				"transform at all, so a rewound impact point and a live shield cannot end up in different " +
				"worlds — the failure a world-space volume would have.");
		}

		/// <summary>A hit inside a raised shield is blocked; the same hit elsewhere is not.</summary>
		[Test]
		public void TryBlockAtVolume_BlocksOnlyWhatMeetsTheShield()
		{
			absorb.Shield = new ShieldVolume
			{
				Shape = ShieldShape.Box,
				LocalCenter = new Vector3(0f, 1f, 0.75f),
				Size = new Vector3(1.2f, 1.4f, 0.2f),
			};

			MitigationCharacter defender = NewCharacter("defender");
			AddBuff(defender, absorb);

			LogAssert.IsTrue(
				DamageMitigation.TryBlockAtVolume(defender, new Vector3(0f, 1.2f, 0.75f), mutate: false),
				"A projectile that struck the shield face is stopped.");
			LogAssert.IsFalse(
				DamageMitigation.TryBlockAtVolume(defender, new Vector3(0f, 1.2f, -0.75f), mutate: false),
				"One that struck the back is not — the shield is an object with a position, not an aura.");
			LogAssert.IsFalse(
				DamageMitigation.TryBlockAtVolume(defender, new Vector3(0f, 0.1f, 0.75f), mutate: false),
				"Nor one that went under it.");
		}

		/// <summary>A buff with no volume blocks nothing physically, whatever its mode.</summary>
		/// <remarks>
		/// The two settings are independent: an Immune ward still negates damage that reaches the
		/// character, but it is not an object projectiles stop against.
		/// </remarks>
		[Test]
		public void TryBlockAtVolume_IgnoresBuffsWithNoVolume()
		{
			MitigationCharacter defender = NewCharacter("defender");
			AddBuff(defender, immune);

			LogAssert.IsFalse(DamageMitigation.TryBlockAtVolume(defender, Vector3.zero, mutate: false),
				"Immunity mitigates; it does not physically stop anything. Blocking on it would make every " +
				"ward a shield and take the author's choice away.");
		}

		/// <summary>A block cost wears the shield down and ends it, and is only paid when it can be.</summary>
		[Test]
		public void TryBlockAtVolume_SpendsTheBlockCostAndEndsWhenExhausted()
		{
			absorb.Amount = 100;
			absorb.VolumeBlockCost = 60;
			absorb.Shield = new ShieldVolume
			{
				Shape = ShieldShape.Sphere,
				LocalCenter = new Vector3(0f, 1f, 0.75f),
				Radius = 0.6f,
			};

			MitigationCharacter defender = NewCharacter("defender");
			Buff shield = AddBuff(defender, absorb);
			Vector3 onTheShield = new Vector3(0f, 1f, 0.75f);

			LogAssert.IsTrue(DamageMitigation.TryBlockAtVolume(defender, onTheShield, mutate: true),
				"The first projectile is stopped.");
			LogAssert.AreEqual(40, shield.RemainingCharges, "And costs 60 of the pool.");

			LogAssert.IsFalse(DamageMitigation.TryBlockAtVolume(defender, onTheShield, mutate: true),
				"The second is not: 40 left cannot pay a cost of 60, and a shield that blocked anyway would " +
				"make the cost meaningless at its last point of charge.");
			LogAssert.AreEqual(40, shield.RemainingCharges, "Nothing is spent on a block that did not happen.");
			LogAssert.IsTrue(defender.Buffs.Buffs.ContainsKey(absorb.ID),
				"And the buff stays up — it still has a pool to absorb damage with, it just cannot block.");
		}

		/// <summary>A peer that does not simulate the defender may test the shield but not wear it.</summary>
		[Test]
		public void TryBlockAtVolume_WithoutMutateSpendsNothing()
		{
			absorb.VolumeBlockCost = 25;
			absorb.Shield = new ShieldVolume
			{
				Shape = ShieldShape.Sphere,
				LocalCenter = Vector3.forward,
				Radius = 1f,
			};

			MitigationCharacter defender = NewCharacter("defender");
			Buff shield = AddBuff(defender, absorb);

			LogAssert.IsTrue(DamageMitigation.TryBlockAtVolume(defender, Vector3.forward, mutate: false),
				"The verdict is the same whoever is asking.");
			LogAssert.AreEqual(100, shield.RemainingCharges,
				"But only the peer that owns the simulation wears the shield down.");
		}

		/// <summary>An authoritative echo must not re-run the shield gate.</summary>
		/// <remarks>
		/// The peer that resolved the hit already asked whether a shield stopped it. A receiver
		/// second-guessing that against its own tracking copy of the defender's buffs is exactly the
		/// observer-resolves-its-own-hits failure the echo flag exists to remove — and it would have
		/// no local point to test with anyway.
		/// </remarks>
		[Test]
		public void ShieldGate_IsSkippedForAnAuthoritativeEcho()
		{
			string source = ReadSource(
				"Assets/Scripts/Shared/Implementation/Entity/Prediction/Ability/AbilityObject.cs");
			LogAssert.IsTrue(source.Contains("hitCharacter != null && !isAuthoritativeEcho && ResolvesHitsLocally"),
				"Both the deflect and the shield gate must sit behind the same guard: only a peer that " +
				"decided this hit may decide it was blocked.");
			LogAssert.IsTrue(source.Contains("ApplyHit(hitCharacter, key, point, normal, Vector3.zero, isAuthoritativeEcho: true)"),
				"The echo path passes no local point, which is safe only while the gate above refuses to run " +
				"for it.");
		}

		/// <summary>Both halves of the shield read one authored shape.</summary>
		/// <remarks>
		/// If the outward sweep approximated the volume with a physics box while the gate tested the
		/// authored one, a player would find hits landing inside a shield that had just stopped one.
		/// The sweep therefore queries a bounding sphere and narrows every candidate through
		/// <c>ShieldVolume.Contains</c>.
		/// </remarks>
		[Test]
		public void ShieldIntercept_NarrowsThroughTheSameContainsTest()
		{
			string source = ReadSource(
				"Assets/Scripts/Shared/Implementation/Entity/ECA/Actions/Character/Ability/ShieldInterceptAction.cs");
			LogAssert.IsTrue(source.Contains("volume.Contains(localPoint)"),
				"The sweep must narrow its broadphase results through the authored shape, or the shield has " +
				"one size when it stops a projectile and another when it sweeps for one.");
			LogAssert.IsTrue(source.Contains("GetWorldBoundingRadius"),
				"And query a bound that fully contains the volume, so nothing inside the shape is missed " +
				"before the narrow test runs.");
			LogAssert.IsTrue(source.Contains("abilityObject.Caster == initiator"),
				"A shield must never eat the shots of the character holding it.");
		}

		/// <summary>
		/// The bounding sphere really does contain the shape it is standing in for.
		/// </summary>
		[Test]
		public void ShieldVolume_BoundingRadiusContainsTheShape()
		{
			GameObject go = new GameObject("blocker");
			gameObjects.Add(go);

			ShieldVolume box = new ShieldVolume
			{
				Shape = ShieldShape.Box,
				LocalCenter = Vector3.zero,
				Size = new Vector3(1.2f, 1.4f, 0.2f),
			};
			float radius = box.GetWorldBoundingRadius(go.transform);
			Vector3 corner = box.Size * 0.5f;
			LogAssert.IsTrue(radius >= corner.magnitude - 1e-4f,
				"The broadphase bound must reach the box's furthest corner, or the sweep would miss objects " +
				"the gate would have blocked.");

			ShieldVolume capsule = new ShieldVolume
			{
				Shape = ShieldShape.Capsule,
				Radius = 0.3f,
				Height = 1.6f,
			};
			LogAssert.IsTrue(capsule.GetWorldBoundingRadius(go.transform) >= 0.8f - 1e-4f,
				"And reach a capsule's end cap.");
		}

		// ── DamageMitigation.TryDeflect ──────────────────────────────────────────────

		/// <summary>A guard turns away what arrives in its arc and lets the rest through.</summary>
		[Test]
		public void TryDeflect_HonoursTheGuardArc()
		{
			MitigationCharacter defender = NewCharacter("defender");
			defender.Transform.rotation = Quaternion.identity; // facing +Z
			AddBuff(defender, deflect);

			// Travelling along -Z, i.e. arriving from the front.
			LogAssert.IsTrue(
				DamageMitigation.TryDeflect(defender, Vector3.back, Vector3.forward, mutate: false, out Vector3 _),
				"An object arriving from dead ahead is inside the guard.");

			// Travelling along +Z, i.e. arriving from behind.
			LogAssert.IsFalse(
				DamageMitigation.TryDeflect(defender, Vector3.forward, Vector3.back, mutate: false, out Vector3 _),
				"An object arriving from behind is outside it and must strike normally.");
		}

		/// <summary>A limited guard spends a charge per deflection and ends when it runs out.</summary>
		[Test]
		public void TryDeflect_SpendsChargesAndEndsWhenExhausted()
		{
			deflect.MaxDeflections = 1;
			MitigationCharacter defender = NewCharacter("defender");
			defender.Transform.rotation = Quaternion.identity;
			Buff guard = AddBuff(defender, deflect);

			LogAssert.AreEqual(1, guard.RemainingCharges, "A single-use guard starts with one charge.");
			LogAssert.IsTrue(
				DamageMitigation.TryDeflect(defender, Vector3.back, Vector3.forward, mutate: true, out Vector3 _),
				"The first projectile is turned away.");
			LogAssert.IsFalse(defender.Buffs.Buffs.ContainsKey(deflect.ID),
				"And the guard is consumed doing it.");

			LogAssert.IsFalse(
				DamageMitigation.TryDeflect(defender, Vector3.back, Vector3.forward, mutate: true, out Vector3 _),
				"With the guard gone the next projectile hits.");
		}

		/// <summary>An unlimited window keeps deflecting for its whole duration.</summary>
		[Test]
		public void TryDeflect_UnlimitedWindowIsNotConsumed()
		{
			deflect.MaxDeflections = 0;
			MitigationCharacter defender = NewCharacter("defender");
			defender.Transform.rotation = Quaternion.identity;
			AddBuff(defender, deflect);

			for (int i = 0; i < 5; ++i)
			{
				LogAssert.IsTrue(
					DamageMitigation.TryDeflect(defender, Vector3.back, Vector3.forward, mutate: true, out Vector3 _),
					"MaxDeflections of 0 means the window's duration is the only bound.");
			}
			LogAssert.IsTrue(defender.Buffs.Buffs.ContainsKey(deflect.ID),
				"An unlimited window must never report itself spent — Buff.IsSpent asks the template " +
				"whether charges were the point, and here they were not.");
		}

		/// <summary>
		/// The deflected heading is a pure function of the incoming one and the impact normal, which
		/// is what lets an observer reproduce it from one bit on the wire.
		/// </summary>
		[Test]
		public void ResolveDeflectedHeading_IsAMirrorAndNeverContinuesForward()
		{
			Vector3 heading = Vector3.forward;
			Vector3 mirrored = DeflectBuffTemplate.ResolveDeflectedHeading(heading, Vector3.back);
			LogAssert.IsTrue(Vector3.Dot(mirrored, heading) < -0.99f,
				"A head-on impact sends the object straight back the way it came.");

			Vector3 glancing = DeflectBuffTemplate.ResolveDeflectedHeading(
				new Vector3(0f, 0f, 1f), new Vector3(-1f, 0f, -1f).normalized);
			LogAssert.IsTrue(Vector3.Dot(glancing, Vector3.forward) < 0.99f,
				"A glancing impact turns the object aside rather than leaving it on its line.");

			Vector3 degenerate = DeflectBuffTemplate.ResolveDeflectedHeading(heading, Vector3.zero);
			LogAssert.IsTrue(Vector3.Dot(degenerate, heading) < -0.99f,
				"A normal the query could not produce falls back to reversing, never to a zero vector — " +
				"the guarantee callers rely on is that the object is no longer heading at the defender.");

			Vector3 parallel = DeflectBuffTemplate.ResolveDeflectedHeading(heading, Vector3.up);
			LogAssert.IsTrue(Vector3.Dot(parallel, heading) < 0.9999f,
				"Mirroring about a surface the object travels ALONG returns the incoming heading, which " +
				"is not a deflection at all; that case must fall back rather than pass the object through.");
		}

		/// <summary>The same inputs give the same heading on every peer.</summary>
		[Test]
		public void ResolveDeflectedHeading_IsDeterministic()
		{
			Vector3 heading = new Vector3(0.3f, -0.2f, 0.9f).normalized;
			Vector3 normal = new Vector3(-0.5f, 0.1f, -0.8f).normalized;

			Vector3 first = DeflectBuffTemplate.ResolveDeflectedHeading(heading, normal);
			Vector3 second = DeflectBuffTemplate.ResolveDeflectedHeading(heading, normal);
			LogAssert.IsTrue(first == second,
				"The server and every observer compute this from the same two vectors and must land on " +
				"the same answer, or their copies of the projectile diverge.");
		}

		// ── The wire and the peer rules ──────────────────────────────────────────────

		/// <summary>
		/// A deflection is applied before the victim is resolved, so it survives an unobservable one.
		/// </summary>
		/// <remarks>
		/// Asserted on the source because reproducing it needs two connected peers, a spawned
		/// projectile and a victim outside one client's streaming budget. The ORDER is the whole
		/// finding: the victim lookup returns early for a character this client is not observing, so
		/// anything after it is lost.
		/// </remarks>
		[Test]
		public void HitBroadcast_AppliesDeflectionBeforeResolvingTheVictim()
		{
			string source = ReadSource(
				"Assets/Scripts/Shared/Implementation/Entity/Prediction/Ability/AbilityController.Activation.cs");

			int deflectIndex = source.IndexOf("msg.Deflected");
			int victimIndex = source.IndexOf("msg.VictimObjectID != 0");

			LogAssert.IsTrue(deflectIndex >= 0,
				"OnAbilityObjectHitBroadcast must act on the Deflected bit; without it an observer's copy " +
				"flies on at a target the server already turned it away from.");
			LogAssert.IsTrue(victimIndex >= 0, "The victim resolution is expected to still be there.");
			LogAssert.IsTrue(deflectIndex < victimIndex,
				"The deflection must be handled BEFORE the victim lookup. That lookup returns early for a " +
				"character this client is not observing, so a deflection handled after it is dropped for " +
				"exactly the victims most likely to be out of view.");
		}

		/// <summary>
		/// The deflected heading travels absolute, so applying it twice cannot undo it.
		/// </summary>
		/// <remarks>
		/// The hit broadcast goes to every observer of the caster INCLUDING the owner, and the owner
		/// normally predicted the deflection itself. A receiver that re-derived the mirror from the
		/// impact normal would therefore reflect an already-reflected heading — and
		/// <c>Reflect(Reflect(v, n), n) == v</c>, so the caster's own copy would turn straight back
		/// at the defender it had just been turned away from. Asserted on the source because
		/// reproducing it needs a spawned projectile and two peers.
		/// </remarks>
		[Test]
		public void DeflectionHeading_TravelsAbsoluteRatherThanAsAMirror()
		{
			Vector3 heading = new Vector3(0.2f, 0f, 1f).normalized;
			Vector3 normal = Vector3.back;
			Vector3 once = DeflectBuffTemplate.ResolveDeflectedHeading(heading, normal);
			Vector3 twice = DeflectBuffTemplate.ResolveDeflectedHeading(once, normal);
			LogAssert.IsTrue(Vector3.Dot(twice, heading) > 0.99f,
				"Mirroring twice about one normal returns the original heading. This is the property that " +
				"makes re-deriving the deflection on the receiver wrong, and it is why the wire carries " +
				"the resulting heading instead.");

			string receiver = ReadSource(
				"Assets/Scripts/Shared/Implementation/Entity/Prediction/Ability/AbilityController.Activation.cs");
			LogAssert.IsTrue(receiver.Contains("AimDirectionCompression.Decode(msg.PackedDeflectHeading)"),
				"The receiver must apply the heading the server sent, not recompute it — see above for what " +
				"recomputing does to the peer that already predicted the deflection.");
		}

		/// <summary>Charges are only ever spent where this peer simulates the defender.</summary>
		/// <remarks>
		/// Asserted on the source: the rule lives at two call sites in two subsystems, and the failure
		/// is a client draining a pool on its own copy of somebody else — silent, and corrected only
		/// by the next reconcile.
		/// </remarks>
		[Test]
		public void MitigationCallSites_GateMutationOnSimulatingThisDefender()
		{
			string damage = ReadSource(
				"Assets/Scripts/Shared/Implementation/Entity/Prediction/CharacterAttribute/CharacterDamageController.cs");
			LogAssert.IsTrue(damage.Contains("DamageMitigation.Negate(Character, attacker, amount, SimulatesOwnBuffEffects())"),
				"Damage must gate the spend on whether this peer simulates the DEFENDER's buffs, not on " +
				"whether it is the server — the owner predicts its own blocks and must spend its own pool.");

			string ability = ReadSource(
				"Assets/Scripts/Shared/Implementation/Entity/Prediction/Ability/AbilityObject.cs");
			LogAssert.IsTrue(ability.Contains("mutate: isServer"),
				"Deflection may be PREDICTED by the caster's client but only the server may spend the " +
				"defender's charge: the defender's buff container on the caster's client is a tracking " +
				"copy that has never applied anything.");
		}

		/// <summary>
		/// A hit the server told us about must not also be drawn as a prediction.
		/// </summary>
		/// <remarks>
		/// The duplicate this removes: <c>AbilityObjectHitBroadcast</c> is reliable and
		/// <c>CombatEventBroadcast</c> is unreliable, so for a hit the owner mispredicted as a miss
		/// the two race. When the unreliable report wins, <c>TryConfirm</c> finds nothing pending and
		/// the display draws the server's number — and the echo, arriving moments later, drew a
		/// second label for the same hit.
		/// </remarks>
		[Test]
		public void AuthoritativeEcho_SuppressesThePredictedNumber()
		{
			AbilityCollisionEventData predicted = new AbilityCollisionEventData(
				null, null, null, Vector3.zero, Vector3.up);
			LogAssert.IsFalse(predicted.IsAuthoritativeEcho,
				"A hit this peer resolved for itself is not an echo.");

			AbilityCollisionEventData echoed = new AbilityCollisionEventData(
				null, null, null, Vector3.zero, Vector3.up, null, isAuthoritativeEcho: true);
			LogAssert.IsTrue(echoed.IsAuthoritativeEcho, "A hit the server reported is.");

			LogAssert.IsTrue(echoed.TryGet(out AbilityCollisionEventData self) && self.IsAuthoritativeEcho,
				"The flag has to be reachable through the payload lookup, because the actions that read it " +
				"are often running against a forked per-candidate event rather than this one.");

			foreach (string path in new[]
			{
				"Assets/Scripts/Shared/Implementation/Entity/ECA/Actions/Character/ApplyDamageAction.cs",
				"Assets/Scripts/Shared/Implementation/Entity/ECA/Actions/Character/ApplyHealAction.cs",
			})
			{
				string source = ReadSource(path);
				LogAssert.IsTrue(source.Contains("MayDrawPredictedNumber(initiator, eventData)"),
					$"{Path.GetFileName(path)} must route its predicted label through MayDrawPredictedNumber, " +
					"which is the only place that knows a server-echoed hit already has a report coming.");
				LogAssert.IsTrue(source.Contains("IsAuthoritativeEcho"),
					$"{Path.GetFileName(path)} must consult the echo flag; without it the guard is only the " +
					"old server/replay pair and the duplicate comes straight back.");
			}
		}

		/// <summary>
		/// The uncapped rule the runtime implements has to be reachable from the inspector.
		/// </summary>
		/// <remarks>
		/// <c>TargetOrdering.CappedCount</c>, <c>LagCompensatedQuery</c> and
		/// <c>LineTargetSelector</c> all treat a cap of zero or less as "no cap". A <c>[Min(1)]</c> on
		/// the volume selectors made that unreachable: an author typing 0 for an uncapped area effect
		/// got 1 — the opposite meaning — with no error anywhere.
		/// </remarks>
		[Test]
		public void VolumeSelectors_AllowAnUncappedMaxHits()
		{
			LogAssert.AreEqual(9, TargetOrdering.CappedCount(9, 0), "Zero means uncapped in the runtime rule.");
			LogAssert.AreEqual(9, TargetOrdering.CappedCount(9, -1), "As does anything below zero.");

			foreach (System.Type type in new[]
			{
				typeof(AreaTargetSelector), typeof(ConeTargetSelector), typeof(RandomTargetSelector),
			})
			{
				FieldInfo field = type.GetField("MaxHits", BindingFlags.Public | BindingFlags.Instance);
				LogAssert.IsNotNull(field, $"{type.Name}.MaxHits not found.");

				MinAttribute min = (MinAttribute)field.GetCustomAttribute(typeof(MinAttribute));
				LogAssert.IsTrue(min == null || min.min <= 0f,
					$"{type.Name}.MaxHits must accept 0, or the uncapped rule every other capped path " +
					"implements cannot be authored on it.");
			}
		}

		// ── Helpers ──────────────────────────────────────────────────────────────────

		private static string ReadSource(string relativePath)
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), relativePath);
			LogAssert.IsTrue(File.Exists(path), $"{relativePath} not found at {path}.");
			return File.ReadAllText(path);
		}

		private MitigationCharacter NewCharacter(string name)
		{
			GameObject go = new GameObject(name);
			gameObjects.Add(go);
			return new MitigationCharacter(go);
		}

		private static Buff AddBuff(MitigationCharacter character, BaseBuffTemplate template)
		{
			Buff buff = new Buff(template.ID, 100u, 1f / 30f);
			character.Buffs.Buffs[template.ID] = buff;
			return buff;
		}

		/// <summary>
		/// A buff container with a real dictionary, so mitigation walks the same
		/// <see cref="SortedDictionary{TKey,TValue}"/> ordering it walks in production.
		/// </summary>
		private sealed class StubBuffController : IBuffController
		{
			public int RemoveCalls;
			public int MarkDirtyCalls;

			public ICharacter Character => null;
			public bool Initialized => true;
			public List<Trigger> OnBuffApplyTriggers { get; } = new List<Trigger>();
			public List<Trigger> OnBuffRemoveTriggers { get; } = new List<Trigger>();
			public SortedDictionary<int, Buff> Buffs { get; } = new SortedDictionary<int, Buff>();
			public bool SimulatesBuffEffects => true;
			public void MarkBuffStateDirty() => ++MarkDirtyCalls;
			public IReadOnlyList<ObservedBuffEntry> ObservedBuffs { get; } = new List<ObservedBuffEntry>();

			public void InitializeOnce(ICharacter character) { }
			public void OnStartCharacter() { }
			public void OnStopCharacter() { }
			public uint GetCurrentDomainTick() => 100u;
			public void Tick(uint currentTick) { }
			public void Apply(BaseBuffTemplate template, PredictionTick currentTick, ICharacter caster = null) { }
			public void ApplyAuthoritative(BaseBuffTemplate template, uint serverTick, ICharacter caster = null) { }
			public uint ResolveAuthoritativeTick(uint serverTick) => serverTick;
			public void Apply(Buff buff, bool suppressFX = false) { }

			public void Remove(int buffID)
			{
				++RemoveCalls;
				Buffs.Remove(buffID);
			}

			public void RemoveRandom(DeterministicRNG rng, bool includeBuffs = false, bool includeDebuffs = false) { }
			public void RemoveAll(bool ignoreInvokeRemove = false, bool includePermanent = false, bool preserveFX = false) { }
			public BuffReconcileEntry[] CreateReconcileSnapshot() => null;
			public void RestoreFromReconcile(BuffReconcileEntry[] entries, uint reconcileTick) { }
		}

		/// <summary>A character with a real transform, so the facing tests have a forward to measure.</summary>
		private sealed class MitigationCharacter : ICharacter
		{
			public readonly StubBuffController Buffs = new StubBuffController();

			public MitigationCharacter(GameObject gameObject)
			{
				GameObject = gameObject;
				Transform = gameObject.transform;
			}

			public long ID { get; set; }
			public string Name => GameObject != null ? GameObject.name : "MitigationCharacter";
			public Transform Transform { get; }
			public GameObject GameObject { get; }
			public Collider Collider { get; set; }
			public NetworkConnection Owner => null;
			public NetworkObject NetworkObject => null;
			public PredictionManager PredictionManager => null;
			public HashSet<NetworkConnection> Observers { get; } = new HashSet<NetworkConnection>();
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

			public bool TryGet<T>(out T control) where T : class, ICharacterBehaviour
			{
				control = Buffs as T;
				return control != null;
			}

			public void Invoke(List<Trigger> triggers, EventData eventData) { }
		}
	}
}
