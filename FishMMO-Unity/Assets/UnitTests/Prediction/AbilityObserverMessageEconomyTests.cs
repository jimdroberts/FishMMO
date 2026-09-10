using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using FishMMO.Client;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using FishNet.Serializing;
using NUnit.Framework;
using UnityEngine;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;
using StubCharacter = FishMMO.UnitTests.Harness.StubCharacter;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// What a cast costs an observer, and what it actually tells them.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Every message here is reliable and per-observer, so a message that says nothing the receiver
	/// could not already work out is not merely wasteful — it is spent against the ~409&#160;B/s
	/// per-observed-peer budget <c>ObserverSyncMode</c> cites for the whole interpolated mode. The
	/// fixture covers the other direction too: a fact the receiver needs and was never sent, which
	/// is the harder failure because nothing on screen says it is missing.
	/// </para>
	/// <para>
	/// The rules under test are deliberately pure functions rather than inline conditions, so the
	/// table each one implements can be asserted without a NetworkManager — the same shape
	/// <c>AbilityObject.ResolvesHitsOnThisPeer</c> and <c>TargetOrdering.CappedCount</c> take.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class AbilityObserverMessageEconomyTests
	{
		private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;
		private const string ActivationPath =
			"Assets/Scripts/Shared/Implementation/Entity/Prediction/Ability/AbilityController.Activation.cs";

		private const string NetworkingPath =
			"Assets/Scripts/Shared/Implementation/Entity/Prediction/Ability/AbilityController.Networking.cs";

		private const string ObjectPath =
			"Assets/Scripts/Shared/Implementation/Entity/Prediction/Ability/AbilityObject.cs";

		private const string DisplayPath =
			"Assets/Scripts/Client/World/ClientCastNameplateDisplay.cs";

		private readonly List<GameObject> gameObjects = new List<GameObject>();
		private readonly List<UnityEngine.Object> assets = new List<UnityEngine.Object>();

		[TearDown]
		public void TearDown()
		{
			for (int i = 0; i < gameObjects.Count; ++i)
			{
				if (gameObjects[i] != null)
				{
					UnityEngine.Object.DestroyImmediate(gameObjects[i]);
				}
			}
			gameObjects.Clear();

			for (int i = 0; i < assets.Count; ++i)
			{
				if (assets[i] != null)
				{
					UnityEngine.Object.DestroyImmediate(assets[i]);
				}
			}
			assets.Clear();
		}

		private static string ReadSource(string relativePath)
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), relativePath);
			LogAssert.IsTrue(File.Exists(path), $"{relativePath} not found at {path}.");
			return File.ReadAllText(path).Replace("\r\n", "\n");
		}

		#region Defect 1 — an instant cast pays for one message, not two or three.

		/// <summary>
		/// The stop is suppressed only for an unheld activation that began and ended on one tick.
		/// </summary>
		/// <remarks>
		/// <para>
		/// 17 of the 37 authored abilities have <c>ActivationTime 0</c>, the default attack among
		/// them, and NPCs cast through the same controller — so the two-message instant cast was the
		/// common case rather than a corner. Twenty visible NPCs auto-attacking once a second spent
		/// roughly 340&#160;B/s per observer on stop messages alone.
		/// </para>
		/// <para>
		/// The held row is the one that must not be "optimised" later: a charge runs past its
		/// activation window for as long as the player holds it, and the receiver's hold allowance
		/// can size an expiry from the cap but cannot say when the key came up.
		/// </para>
		/// </remarks>
		[Test]
		public void TheCastStopIsSuppressedOnlyForAnUnheldSameTickActivation()
		{
			const uint unset = FishNet.Managing.Timing.TimeManager.UNSET_TICK;

			LogAssert.IsTrue(AbilityController.SuppressesRedundantCastStop(100u, 100u, isHeld: false),
				"An instant cast begins and ends inside one replicate call; its stop says nothing the " +
				"receiver cannot derive from the template it already holds.");

			LogAssert.IsFalse(AbilityController.SuppressesRedundantCastStop(100u, 101u, isHeld: false),
				"A cast that spanned even one more tick really ended, and only the stop says when.");

			LogAssert.IsFalse(AbilityController.SuppressesRedundantCastStop(100u, 100u, isHeld: true),
				"A HELD activation keeps its stop even on the tick it started: HeldAllowance bounds " +
				"the expiry, it does not say when the player let go.");

			LogAssert.IsFalse(AbilityController.SuppressesRedundantCastStop(100u, 130u, isHeld: true),
				"...and certainly once it has been held for a while.");

			LogAssert.IsFalse(AbilityController.SuppressesRedundantCastStop(unset, 100u, isHeld: false),
				"With no start announced there is no pair to collapse — suppressing here would drop a " +
				"stop for a row some earlier start may have opened.");
		}

		/// <summary>
		/// A zero-duration row expires on the dwell; an unresolvable one still gets the grace.
		/// </summary>
		/// <remarks>
		/// Once the server stops sending a stop for an instant cast, the receiver's own timer is the
		/// only thing that clears the row — and sizing it from <c>ExpiryGraceSeconds</c> would leave
		/// "Casting Punch" up for 1.5&#160;s per swing, which at auto-attack cadence never comes off
		/// at all. The third row is why <c>ResolveDuration</c> reports whether it resolved anything:
		/// an ability this peer does not know also computes zero, and that one may be a five second
		/// cast whose stop is on its way.
		/// </remarks>
		[Test]
		public void AZeroDurationRowExpiresOnTheDwellButAnUnknownAbilityKeepsTheGrace()
		{
			const float now = 10f;

			LogAssert.IsTrue(Mathf.Approximately(now + ClientCastNameplateDisplay.MinimumDwellSeconds,
					ClientCastNameplateDisplay.ResolveExpiry(now, duration: 0f, durationKnown: true,
						remaining: 0f, heldAllowance: 0f)),
				"An instant cast's row lives exactly the dwell: long enough to read as an event, short " +
				"enough not to be believed as an ongoing cast.");

			LogAssert.IsTrue(Mathf.Approximately(now + ClientCastNameplateDisplay.ExpiryGraceSeconds,
					ClientCastNameplateDisplay.ResolveExpiry(now, duration: 0f, durationKnown: false,
						remaining: 0f, heldAllowance: 0f)),
				"An ability this peer cannot resolve reports zero for a different reason and must keep " +
				"the grace; its stop is still coming.");

			LogAssert.IsTrue(Mathf.Approximately(now + 2f + ClientCastNameplateDisplay.ExpiryGraceSeconds,
					ClientCastNameplateDisplay.ResolveExpiry(now, duration: 0f, durationKnown: true,
						remaining: 0f, heldAllowance: 2f)),
				"A HELD instant keeps the grace and its hold allowance: it runs past its window and the " +
				"server always sends its stop.");

			LogAssert.IsTrue(Mathf.Approximately(now + 3f + ClientCastNameplateDisplay.ExpiryGraceSeconds,
					ClientCastNameplateDisplay.ResolveExpiry(now, duration: 5f, durationKnown: true,
						remaining: 3f, heldAllowance: 0f)),
				"An ordinary cast expires on what is LEFT of it plus the grace, not on its whole duration.");

			LogAssert.IsTrue(Mathf.Approximately(now + ClientCastNameplateDisplay.ExpiryGraceSeconds,
					ClientCastNameplateDisplay.ResolveExpiry(now, duration: 5f, durationKnown: true,
						remaining: -4f, heldAllowance: 0f)),
				"A negative remainder is clamped rather than shortening the expiry into the past.");
		}

		#endregion

		#region Defect 2 — an observer arriving mid-cast learns the cast.

		/// <summary>
		/// The spawn payload carries the running activation, and both shapes read it back exactly.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <c>CharacterCastBroadcast</c> is sent once, on the tick a cast begins, to whoever was
		/// observing at that instant. An observer that walked into range — or was un-culled, which
		/// the streaming budget treats as routine — three seconds into a five second cast saw a
		/// nameplate that said nothing, and then received the STOP for a cast it had never been told
		/// about, whose handler finds no row and returns.
		/// </para>
		/// <para>
		/// The exactness assertions are the load-bearing half. The payload is ONE unframed buffer
		/// shared by every behaviour on the prefab, so a write without a matching read desynchronises
		/// everything that reads after it; the block's own length prefix absorbs a shape change only
		/// if the reader actually consumes what the writer produced.
		/// </para>
		/// </remarks>
		[Test]
		public void TheSpawnPayloadCarriesTheActivationThatIsAlreadyRunning()
		{
			AbilityTemplate template = NewAsset<AbilityTemplate>("Economy_Payload_Ability");

			AbilityController writer = NewController("PayloadWriter");
			writer.LearnAbility(new Ability(9101L, template));

			// ── Idle: one shape byte and nothing else.
			Writer idleWriter = new Writer();
			writer.WritePayload(null, idleWriter);
			int idleBytes = idleWriter.Position;

			// ── Mid-cast: the reference id, the consumable bit, and the tick it started on.
			SetPrivate(writer, "currentAbilityID", 9101L);
			SetPrivate(writer, "castStartServerTick", 4242u);

			Writer castingWriter = new Writer();
			writer.WritePayload(null, castingWriter);
			int castingBytes = castingWriter.Position;

			TestContext.WriteLine($"MEASURE activation block: idle {idleBytes} B, mid-cast {castingBytes} B");
			LogAssert.IsTrue(castingBytes > idleBytes,
				"A running activation must actually reach the wire.");

			AbilityController idleReader = NewController("PayloadIdleReader");
			Reader r1 = new Reader(idleWriter.GetArraySegment(), null);
			idleReader.ReadPayload(null, r1);
			LogAssert.AreEqual(0, r1.Remaining,
				"The idle shape must be consumed exactly; a leftover byte is read as the next behaviour's state.");
			LogAssert.AreEqual(AbilityController.NO_ABILITY,
				(long)GetPrivate(idleReader, "pendingActivationReferenceID"),
				"An idle caster must not hand its observer a cast to draw.");

			AbilityController castingReader = NewController("PayloadCastingReader");
			Reader r2 = new Reader(castingWriter.GetArraySegment(), null);
			castingReader.ReadPayload(null, r2);
			LogAssert.AreEqual(0, r2.Remaining, "The mid-cast shape must be consumed exactly too.");
			LogAssert.AreEqual(9101L, (long)GetPrivate(castingReader, "pendingActivationReferenceID"),
				"The receiver must learn WHICH ability is being cast, by instance id, so the row can name " +
				"the crafted ability rather than its base template.");
			LogAssert.AreEqual(4242u, (uint)GetPrivate(castingReader, "pendingActivationStartTick"),
				"And the tick it started on, or the row restarts a cast that is nearly over.");
			LogAssert.IsFalse((bool)GetPrivate(castingReader, "pendingActivationIsConsumable"),
				"An ability is not a consumable.");

			// ── A consumable travels through the same block under its own bit.
			SetPrivate(writer, "currentAbilityID", 55L);
			/* Through the bit helper, because AbilityActivationFlags members are bit POSITIONS
			 * rather than masks — the same EnableBit the controller uses. */
			int consumableFlags = 0;
			consumableFlags.EnableBit(AbilityActivationFlags.IsConsumable);
			SetPrivate(writer, "replicatedFlags", consumableFlags);

			Writer itemWriter = new Writer();
			writer.WritePayload(null, itemWriter);

			AbilityController itemReader = NewController("PayloadItemReader");
			Reader r3 = new Reader(itemWriter.GetArraySegment(), null);
			itemReader.ReadPayload(null, r3);
			LogAssert.AreEqual(0, r3.Remaining, "The consumable shape must be consumed exactly.");
			LogAssert.IsTrue((bool)GetPrivate(itemReader, "pendingActivationIsConsumable"),
				"A consumable's reference id is an ITEM template id, and the receiver has to know that " +
				"before it looks the id up.");
		}

		/// <summary>
		/// The catch-up is announced from <c>OnStartClient</c> and drawn down the live message's path.
		/// </summary>
		/// <remarks>
		/// Asserted on the source because reproducing it needs a spawned NetworkObject, a nameplate
		/// and a client TimeManager. What matters is that the payload activation is replayed as a
		/// <c>CharacterCastBroadcast</c> into the SAME handler the live message uses, so the
		/// <c>ComputeObserverCatchUpTicks</c> correction applies to both and the row opens partway
		/// through rather than restarting — the same approach <c>CastVisibilityTests</c> takes for
		/// the sender.
		/// </remarks>
		[Test]
		public void TheMidCastCatchUpOpensTheRowThroughTheLiveMessagePath()
		{
			string networking = ReadSource(NetworkingPath);
			int start = networking.IndexOf("public override void OnStartClient", StringComparison.Ordinal);
			LogAssert.IsTrue(start >= 0, "OnStartClient must still be the hook that drains the payload.");
			string body = networking.Substring(start, Math.Min(900, networking.Length - start));
			LogAssert.IsTrue(body.Contains("AnnouncePayloadActivation()"),
				"The drain hook is the one callback that runs for observers as well as the owner — " +
				"OnStartCharacter is owner-only, which is why the in-flight drain moved here.");

			LogAssert.IsTrue(networking.Contains("OnObservedActivationCatchUp"),
				"The announcement must reach the client assembly through a hook, since Shared cannot " +
				"reference it.");
			LogAssert.IsTrue(networking.Contains("Started = true,"),
				"It is replayed as a START, in the live message's own shape.");

			string display = ReadSource(DisplayPath);
			LogAssert.IsTrue(display.Contains("AbilityController.OnObservedActivationCatchUp += OnCastCatchUp"),
				"The display must subscribe where it registers the broadcast.");
			LogAssert.IsTrue(display.Contains("AbilityController.OnObservedActivationCatchUp -= OnCastCatchUp"),
				"...and unsubscribe where it unregisters, or a static event outlives the session.");

			int catchUp = display.IndexOf("private void OnCastCatchUp", StringComparison.Ordinal);
			LogAssert.IsTrue(catchUp >= 0, "The catch-up entry point must exist.");
			string catchUpBody = display.Substring(catchUp, Math.Min(400, display.Length - catchUp));
			LogAssert.IsTrue(catchUpBody.Contains("Start(msg)"),
				"It must go through the shared start path rather than reimplementing it, or the catch-up " +
				"arithmetic applies to one of the two sources only.");
		}

		#endregion

		#region Defect 3 — the reconciled generator reaches only peers it is the authority for.

		/// <summary>
		/// The deterministic generator is installed by the owner, and by an observer only in the
		/// forwarded mode where that observer simulates the caster itself.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <c>WritePayload</c> refuses to hand a non-owner the four xoshiro128** state words and says
		/// why: 128 bits is the whole generator, so a modified client holding a peer's state can
		/// compute every seed that peer will ever cast with. The reconcile carries the same words and
		/// its consumer installed them outside any ownership gate — while every broadcast site in the
		/// project asks <c>ObserverSyncMode</c> whose turn it is.
		/// </para>
		/// <para>
		/// The forwarded row is not a loophole, it is the mode's contract: with forwarding on an
		/// observer runs the caster's replicate from the relayed input and spawns the objects itself
		/// (<c>BroadcastAbilityActivated</c> is deliberately silent there), and the generator is what
		/// advances the per-cast seed those spawns are built from.
		/// </para>
		/// </remarks>
		[Test]
		public void TheReconciledGeneratorIsInstalledOnlyWhereItIsTheAuthority()
		{
			LogAssert.IsTrue(AbilityController.InstallsReconciledRngState(isOwner: true, observersConsumeReconcile: false),
				"The owner's replay is load-bearing for deterministic ability RNG; this is the shipped path " +
				"and must be untouched.");

			LogAssert.IsTrue(AbilityController.InstallsReconciledRngState(isOwner: true, observersConsumeReconcile: true),
				"The owner still installs it with forwarding on.");

			LogAssert.IsFalse(AbilityController.InstallsReconciledRngState(isOwner: false, observersConsumeReconcile: false),
				"A non-owner in the interpolated mode must not install a peer's generator. FishNet sends " +
				"that reconcile to the owner alone, so this row is the gate rather than the behaviour.");

			LogAssert.IsTrue(AbilityController.InstallsReconciledRngState(isOwner: false, observersConsumeReconcile: true),
				"With forwarding on the observer simulates the caster from relayed input, so the generator " +
				"is the authority it spawns from rather than a disclosure.");

			/* The gate has to sit around the RESTORE, not merely exist. */
			string controller = ReadSource(
				"Assets/Scripts/Shared/Implementation/Entity/Prediction/Ability/AbilityController.cs");
			int apply = controller.IndexOf("private void ApplyAuthoritativeReconcileState", StringComparison.Ordinal);
			LogAssert.IsTrue(apply >= 0, "The reconcile consumer must still exist.");
			string body = controller.Substring(apply, Math.Min(4200, controller.Length - apply));

			int rule = body.IndexOf("InstallsReconciledRngState(", StringComparison.Ordinal);
			int restore = body.IndexOf("abilitySeedGenerator.RestoreState(", StringComparison.Ordinal);
			int seed = body.IndexOf("currentSeed = rd.Seed;", StringComparison.Ordinal);
			LogAssert.IsTrue(rule >= 0 && restore >= 0 && seed >= 0,
				"The rule, the generator restore and the seed restore must all be locatable.");
			LogAssert.IsTrue(rule < seed && rule < restore,
				"Both the generator state and the current seed must be restored INSIDE the gate; a " +
				"non-owner that installs either is holding state the payload path refuses it.");
		}

		#endregion

		#region Defect 4 — a multi-shot spawn is caught up whole.

		/// <summary>
		/// Every live member of a reproduced container is caught up, not only the root.
		/// </summary>
		/// <remarks>
		/// <c>AbilityObject.Spawn</c> returns the root, and the OnSpawn chain inside it builds the
		/// children — each with zero elapsed ticks and the source's un-charged remaining lifetime.
		/// Charging the root alone left every pellet of a multi-shot ability at the launch point with
		/// a full life while the root was correctly placed, and on the in-flight replay path the
		/// catch-up can be seconds. Nothing repaired it: the per-object hit broadcasts name objects,
		/// not positions.
		/// </remarks>
		[Test]
		public void EveryMemberOfAReproducedSpawnIsCaughtUpNotJustTheRoot()
		{
			AbilityObject root = NewAbilityObject("EconomyRoot");
			AbilityObject childA = NewAbilityObject("EconomyChildA");
			AbilityObject childB = NewAbilityObject("EconomyChildB");

			Dictionary<int, AbilityObject> container = new Dictionary<int, AbilityObject>
			{
				[0] = root,
				[1] = childA,
				[2] = childB,
			};

			AbilityController.CatchUpSpawnedMembers(container, root, consumeTicks: 0u, fastForwardTicks: 40u);

			LogAssert.AreEqual(40u, root.ElapsedTicks, "The root was always advanced.");
			LogAssert.AreEqual(40u, childA.ElapsedTicks,
				"A child of AbilitySpawnMultiplyAction starts at zero elapsed ticks and owes the whole " +
				"catch-up; leaving it there parked the pellet at the muzzle.");
			LogAssert.AreEqual(40u, childB.ElapsedTicks, "Every member, not just the first one found.");

			/* Nothing to do is still nothing to do: a fresh message owes no catch-up at all. */
			AbilityController.CatchUpSpawnedMembers(container, root, consumeTicks: 0u, fastForwardTicks: 0u);
			LogAssert.AreEqual(40u, root.ElapsedTicks, "A zero catch-up must not touch the container.");
		}

		/// <summary>
		/// A member whose own life is shorter than the catch-up dies rather than going negative.
		/// </summary>
		/// <remarks>
		/// The pre-redirect charge runs before the trajectory advance and the destroyed check sits
		/// between them, so a member that expires to the first never runs the second — and an object
		/// that no longer exists on the server must not be left flying with a negative remainder.
		/// </remarks>
		[Test]
		public void AMemberShorterThanTheCatchUpDiesRatherThanGoingNegative()
		{
			AbilityTemplate template = NewAsset<AbilityTemplate>("Economy_CatchUp_Life");
			template.LifeTime = 5f;
			Ability ability = new Ability(3L, template);
			ability.Objects = new Dictionary<int, Dictionary<int, AbilityObject>>();

			AbilityObject root = NewAbilityObject("EconomyLongLived");
			AbilityObject shortLived = NewAbilityObject("EconomyShortLived");
			foreach (AbilityObject member in new[] { root, shortLived })
			{
				member.Ability = ability;
				SetPrivate(member, "tickDelta", 0.1f);
			}
			root.RemainingLifeTime = 5f;
			shortLived.RemainingLifeTime = 0.5f;

			Dictionary<int, AbilityObject> container = new Dictionary<int, AbilityObject>
			{
				[0] = root,
				[1] = shortLived,
			};

			/* DestroyAbilityObjectInternal calls Object.Destroy, which edit mode logs an error for
			 * and then declines; the state transition under test happens before that call. */
			UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
			try
			{
				AbilityController.CatchUpSpawnedMembers(container, root, consumeTicks: 10u, fastForwardTicks: 0u);
			}
			finally
			{
				UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;
			}

			LogAssert.IsFalse(root.IsDestroyed, "Four seconds left is four seconds left.");
			LogAssert.IsTrue(Mathf.Approximately(4f, root.RemainingLifeTime),
				"Ten ticks at 0.1 s charge one second against the root.");
			LogAssert.IsTrue(shortLived.IsDestroyed,
				"A member with half a second left does not exist on the server any more; it must die " +
				"inside the catch-up rather than fly on with a negative remainder.");
		}

		#endregion

		#region Defects 5 and 6 — one collision, one message, and a block that reads as a block.

		/// <summary>
		/// The block and the end ride the hit message, and a deflection carries neither.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Both are header bits, so neither costs the impact shape a byte. The deflect masking is
		/// what keeps the header a true statement of what follows: a deflection gives the projectile
		/// back on a new heading, so it ends nothing and strikes nobody, and its shape carries only
		/// the heading.
		/// </para>
		/// <para>
		/// Written through the hand-written pair directly, because EditMode runs neither
		/// <c>RuntimeInitializeOnLoadMethod</c> nor FishNet's IL post-processor — and that pair is
		/// what ships, the struct carrying <c>[UseGlobalCustomSerializer]</c>.
		/// </para>
		/// </remarks>
		[Test]
		public void TheHitMessageCarriesTheBlockAndTheEndWithoutGrowing()
		{
			AbilityObjectHitBroadcast plain = new AbilityObjectHitBroadcast
			{
				CasterObjectID = 7,
				AbilityID = 31L,
				ContainerID = unchecked((int)0xDEADBEEF),
				ObjectID = 0,
				VictimObjectID = 12,
				Point = new Vector3(1f, 2f, 3f),
				Normal = Vector3.up,
			};

			Writer plainWriter = new Writer();
			plainWriter.WriteAbilityObjectHitBroadcast(plain);

			AbilityObjectHitBroadcast blocked = plain;
			blocked.Blocked = true;
			blocked.Ended = true;

			Writer blockedWriter = new Writer();
			blockedWriter.WriteAbilityObjectHitBroadcast(blocked);

			LogAssert.AreEqual(plainWriter.Position, blockedWriter.Position,
				"Both flags share the header byte the shape already paid for, so stating a block and an " +
				"end costs nothing over an ordinary impact — while the destroy it replaces cost another " +
				"reliable message of fifteen to twenty bytes.");

			AbilityObjectHitBroadcast readBlocked =
				new Reader(blockedWriter.GetArraySegment(), null).ReadAbilityObjectHitBroadcast();
			LogAssert.IsTrue(readBlocked.Blocked,
				"Without this bit an observer cannot tell a blocked shot from a landed one: the echo " +
				"path deliberately skips mitigation, so it ran the whole OnHit chain for a hit the " +
				"server had rejected.");
			LogAssert.IsTrue(readBlocked.Ended, "And the end must travel with it, or the copy is a ghost.");
			LogAssert.AreEqual(12, readBlocked.VictimObjectID, "The victim still travels.");
			LogAssert.IsFalse(readBlocked.DirectImpact, "A swept block is not an action impact.");

			AbilityObjectHitBroadcast readPlain =
				new Reader(plainWriter.GetArraySegment(), null).ReadAbilityObjectHitBroadcast();
			LogAssert.IsFalse(readPlain.Blocked, "An ordinary impact is not a block.");
			LogAssert.IsFalse(readPlain.Ended, "...and does not claim to end the object.");

			// ── A deflection masks both off: it ends nothing and strikes nobody.
			AbilityObjectHitBroadcast deflected = plain;
			deflected.Deflected = true;
			deflected.Blocked = true;
			deflected.Ended = true;
			deflected.PackedDeflectHeading = 0x1234ABCDu;

			Writer deflectWriter = new Writer();
			deflectWriter.WriteAbilityObjectHitBroadcast(deflected);
			AbilityObjectHitBroadcast readDeflected =
				new Reader(deflectWriter.GetArraySegment(), null).ReadAbilityObjectHitBroadcast();

			LogAssert.IsTrue(readDeflected.Deflected, "The deflect shape survives.");
			LogAssert.IsFalse(readDeflected.Blocked,
				"A deflect and a block are exclusive outcomes; the writer masks rather than trusting " +
				"its producers, so the header stays a true statement of what follows.");
			LogAssert.IsFalse(readDeflected.Ended, "A deflected object is still flying.");
			LogAssert.AreEqual(0x1234ABCDu, readDeflected.PackedDeflectHeading,
				"And the absolute heading is all the deflect shape carries.");
		}

		/// <summary>
		/// A hit claims to end the object only when that is certain before the OnHit chain runs.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The message is written BEFORE the chain and has to be: a chain may end the object, and
		/// <c>DestroyAbilityObjectInternal</c> nulls the ability and the caster, so a destroyed object
		/// cannot publish anything. That makes the claim a pre-chain one, which is why it has to be
		/// certain rather than likely — <c>AbilityHitCountAction</c> with an amount of +1 exactly
		/// cancels the decrement the impact applies, so a pierce chain's hit cannot promise an end and
		/// keeps the standalone destroy.
		/// </para>
		/// <para>
		/// Every shipped ability carries <c>HitCount 1</c> and no pierce, so the first row is the
		/// common case and the saving is real rather than theoretical.
		/// </para>
		/// </remarks>
		[Test]
		public void AHitClaimsTheEndOnlyWhenTheEndIsCertain()
		{
			LogAssert.IsTrue(AbilityObject.HitEndsObject(resolvesHitsLocally: true, hitCountBeforeThisHit: 1,
					chainCanExtendHitCount: false),
				"The shipped shape: one hit budgeted, nothing in the chain that can add to it.");

			LogAssert.IsFalse(AbilityObject.HitEndsObject(resolvesHitsLocally: true, hitCountBeforeThisHit: 2,
					chainCanExtendHitCount: false),
				"A pierce with budget left does not end here.");

			LogAssert.IsFalse(AbilityObject.HitEndsObject(resolvesHitsLocally: true, hitCountBeforeThisHit: 1,
					chainCanExtendHitCount: true),
				"A chain carrying AbilityHitCountAction can cancel this hit's decrement, so the end is " +
				"not knowable when the message is written and the standalone destroy must still travel.");

			LogAssert.IsFalse(AbilityObject.HitEndsObject(resolvesHitsLocally: false, hitCountBeforeThisHit: 1,
					chainCanExtendHitCount: false),
				"A peer that does not resolve hits spends no count and publishes nothing; it must never " +
				"claim to have decided an end.");

			LogAssert.IsTrue(AbilityObject.HitEndsObject(resolvesHitsLocally: true, hitCountBeforeThisHit: 0,
					chainCanExtendHitCount: false),
				"An already-exhausted object — an orphan draining through collisions — ends on this hit too.");
		}

		/// <summary>
		/// The block no longer sends its own destroy, and the receiver neither runs the chain nor
		/// keeps a ghost.
		/// </summary>
		/// <remarks>
		/// Asserted on the source: the sending half needs a live shield buff on a spawned defender
		/// inside a rewind scope, and the receiving half needs a client NetworkManager with the
		/// caster in its spawned map. What the assertions pin is the pairing — one message states
		/// both facts, the destroy that used to accompany it is gone, and the receiver acts on the
		/// end even when it could not resolve the victim.
		/// </remarks>
		[Test]
		public void OneCollisionProducesOneMessageAndTheReceiverActsOnBothFacts()
		{
			string objectSource = ReadSource(ObjectPath);

			int block = objectSource.IndexOf("DamageMitigation.TryBlockAtVolume", StringComparison.Ordinal);
			LogAssert.IsTrue(block >= 0, "The shield gate must still exist.");
			string blockBody = objectSource.Substring(block, Math.Min(1600, objectSource.Length - block));
			LogAssert.IsTrue(blockBody.Contains("blocked: true, ended: true"),
				"A block states both facts on the hit message.");
			LogAssert.IsTrue(blockBody.Contains("notifyObservers: false"),
				"...so the paired destroy broadcast is gone. Sending both was two reliable messages " +
				"naming the same caster, ability, container and object.");

			LogAssert.IsTrue(objectSource.Contains("notifyObservers: !endsOnThisHit"),
				"The hit-count end is stated inline when it could be known, and only falls back to the " +
				"standalone destroy for a pierce chain.");

			string activation = ReadSource(ActivationPath);
			int handler = activation.IndexOf("private static void OnAbilityObjectHitBroadcast", StringComparison.Ordinal);
			LogAssert.IsTrue(handler >= 0, "The hit handler must still exist.");
			int handlerEnd = activation.IndexOf("\n\t\t/// <summary>\n\t\t/// Applies a server-resolved fork redirect",
				handler, StringComparison.Ordinal);
			LogAssert.IsTrue(handlerEnd > handler, "The handler's extent must be locatable.");
			string handlerBody = activation.Substring(handler, handlerEnd - handler);

			LogAssert.IsTrue(handlerBody.Contains("ApplyObservedBlock("),
				"A block takes the path that records the dedupe entry and runs no events — the whole " +
				"point of the bit.");
			LogAssert.IsTrue(handlerBody.Contains("if (msg.Ended)"),
				"And the receiver must destroy its copy from the hit message rather than waiting for a " +
				"destroy that is no longer coming.");
			int unresolved = handlerBody.IndexOf("victimResolved = false;", StringComparison.Ordinal);
			int ended = handlerBody.IndexOf("if (msg.Ended)", StringComparison.Ordinal);
			LogAssert.IsTrue(unresolved >= 0 && unresolved < ended,
				"An unresolvable victim must drop the HIT and still act on the END: dropping both leaves " +
				"a copy flying that the server no longer has, which is the ghost the destroy message " +
				"existed to prevent.");
		}

		/// <summary>
		/// An echoed block records the victim without running the chain or ending the copy itself.
		/// </summary>
		/// <remarks>
		/// The dedupe entry is what absorbs a second report of the same body — the sweep re-runs every
		/// tick — and on the caster's own client it absorbs the echo of a block that client already
		/// resolved. The end is the server's to state, so this path touches neither the hit count nor
		/// the destroyed flag.
		/// </remarks>
		[Test]
		public void AnEchoedBlockRecordsTheVictimAndRunsNothing()
		{
			AbilityObject abilityObject = NewAbilityObject("EconomyBlockEcho");
			abilityObject.HitCount = 1;

			GameObject victimGo = new GameObject("EconomyBlockVictim");
			gameObjects.Add(victimGo);
			ProbeCharacter victim = victimGo.AddComponent<ProbeCharacter>();

			abilityObject.ApplyObservedBlock(victim);

			LogAssert.AreEqual(1, abilityObject.HitTargetCount,
				"The blocker goes into the hit set, so a stationary object overlapping it does not ask " +
				"again on every tick.");
			LogAssert.AreEqual(1, abilityObject.HitCount,
				"A block spends no hit count on a peer that was merely told about it.");
			LogAssert.IsFalse(abilityObject.IsDestroyed,
				"And it does not end the copy on its own; the Ended bit is what does that.");

			abilityObject.ApplyObservedBlock(victim);
			LogAssert.AreEqual(1, abilityObject.HitTargetCount, "One body, one entry, however many reports.");
		}

		#endregion

		#region Defect 7 — a stop carries only what a stop is read for.

		/// <summary>
		/// A cast stop round-trips and costs fewer bytes than a start.
		/// </summary>
		/// <remarks>
		/// The handler branches on <c>Started</c> and returns having read only the caster id and the
		/// reference id, so the tick and the consumable flag are dead on a stop — and the generated
		/// serializer wrote all five fields unconditionally on both. <c>Started</c> therefore leads
		/// the format as a header bit, because it is the field that says which shape follows.
		/// </remarks>
		[Test]
		public void ACastStopCarriesNeitherTheTickNorTheConsumableFlag()
		{
			CharacterCastBroadcast start = new CharacterCastBroadcast
			{
				CasterObjectID = 9,
				ReferenceID = 4242L,
				IsConsumable = false,
				ServerTick = 123456u,
				Started = true,
			};

			Writer startWriter = new Writer();
			startWriter.WriteCharacterCastBroadcast(start);

			CharacterCastBroadcast stop = start;
			stop.Started = false;

			Writer stopWriter = new Writer();
			stopWriter.WriteCharacterCastBroadcast(stop);

			TestContext.WriteLine($"MEASURE cast message: start {startWriter.Position} B, stop {stopWriter.Position} B");
			LogAssert.IsTrue(stopWriter.Position < startWriter.Position,
				"A stop must be smaller than a start: it is read for two fields and was written for five.");

			CharacterCastBroadcast readStart =
				new Reader(startWriter.GetArraySegment(), null).ReadCharacterCastBroadcast();
			LogAssert.IsTrue(readStart.Started, "A start reads back as a start.");
			LogAssert.AreEqual(9, readStart.CasterObjectID, "The caster is what the row is keyed on.");
			LogAssert.AreEqual(4242L, readStart.ReferenceID, "The reference id names what is being cast.");
			LogAssert.AreEqual(123456u, readStart.ServerTick,
				"And the start tick must survive, or a late message cannot be corrected.");

			CharacterCastBroadcast readStop =
				new Reader(stopWriter.GetArraySegment(), null).ReadCharacterCastBroadcast();
			LogAssert.IsFalse(readStop.Started, "A stop reads back as a stop.");
			LogAssert.AreEqual(9, readStop.CasterObjectID, "The caster still travels.");
			LogAssert.AreEqual(4242L, readStop.ReferenceID,
				"And so does the reference, in the SAME encoding the start used — the receiver compares " +
				"a stop's reference against the row's.");
			LogAssert.AreEqual(0u, readStop.ServerTick, "The tick does not, and the handler never reads it.");

			// A starting consumable keeps its flag; the bit shares the header byte.
			CharacterCastBroadcast item = start;
			item.IsConsumable = true;
			Writer itemWriter = new Writer();
			itemWriter.WriteCharacterCastBroadcast(item);
			LogAssert.AreEqual(startWriter.Position, itemWriter.Position,
				"The consumable bit rides the header rather than costing a byte of its own.");

			CharacterCastBroadcast readItem =
				new Reader(itemWriter.GetArraySegment(), null).ReadCharacterCastBroadcast();
			LogAssert.IsTrue(readItem.IsConsumable,
				"A start must say whether the reference is an item template id or an ability instance id.");
		}

		#endregion

		#region Defects 8 and 9 — the display's own two.

		/// <summary>
		/// A superseding start clears the previous row immediately, and the grace's reason is honest.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The supersede branch's comment asserted that the previous cast's text must not be left
		/// standing, and then called a <c>Stop</c> that honoured the dwell — so a row younger than
		/// 0.6&#160;s was merely marked <c>StopPending</c> and the stale text stayed up for the rest
		/// of it, which is the opposite of what the comment claimed.
		/// </para>
		/// <para>
		/// The second half is a comment correction with a test because the comment was an argument
		/// about the wire: it said the stop was unreliable and could be lost, while
		/// <c>BroadcastCastState</c> sends <c>Channel.Reliable</c> and its own remarks call that a
		/// correctness requirement. The grace is still worth keeping — a caster that disconnects
		/// mid-cast sends no stop at all — but not for the reason stated.
		/// </para>
		/// </remarks>
		[Test]
		public void ASupersedingStartDoesNotLeaveTheOldTextStandingAndTheGraceIsHonest()
		{
			string display = ReadSource(DisplayPath);

			LogAssert.IsTrue(display.Contains("bool respectDwell = true"),
				"Stop must distinguish an activation that ENDED from a row being reassigned.");
			LogAssert.IsTrue(display.Contains("respectDwell: false"),
				"And the supersede path must pass false, or the stale text outlives the start that " +
				"replaced it.");
			LogAssert.IsTrue(display.Contains("if (!respectDwell || Time.unscaledTime >= cast.EarliestClear)"),
				"The flag has to short-circuit the dwell test rather than sit beside it.");

			LogAssert.IsFalse(display.Contains("The stop is unreliable"),
				"The stop rides Channel.Reliable, and reliability there is a correctness requirement: an " +
				"unreliable stop reordering behind the next start clears the wrong row.");
			int grace = display.IndexOf("ExpiryGraceSeconds = 1.5f", StringComparison.Ordinal);
			LogAssert.IsTrue(grace >= 0, "The grace must still exist.");
			string graceRemarks = display.Substring(Math.Max(0, grace - 1400), Math.Min(1400, grace));
			LogAssert.IsTrue(graceRemarks.Contains("Channel.Reliable"),
				"...and the remarks must now say which channel it actually is.");
			LogAssert.IsTrue(graceRemarks.Contains("disconnects"),
				"The grace survives for the case that really has no stop: a caster that goes away " +
				"mid-cast.");
		}

		#endregion

		#region Fixture helpers.

		private T NewAsset<T>(string name) where T : ScriptableObject
		{
			T asset = ScriptableObject.CreateInstance<T>();
			asset.name = name;
			assets.Add(asset);
			return asset;
		}

		private AbilityObject NewAbilityObject(string name)
		{
			GameObject go = new GameObject(name);
			gameObjects.Add(go);
			return go.AddComponent<AbilityObject>();
		}

		/// <summary>
		/// Builds an <see cref="AbilityController"/> usable outside a live network session.
		/// </summary>
		/// <remarks>
		/// <c>AddComponent</c> never runs <c>Awake</c> in EditMode, so the collections are built by
		/// hand, and the seed generator is pre-seeded so <c>WritePayload</c> never consults
		/// <c>IsServerStarted</c> — every NetworkBehaviour convenience property throws on an unspawned
		/// object. The same harness <c>AbilityObserverReproductionTests</c> uses.
		/// </remarks>
		private AbilityController NewController(string name)
		{
			GameObject go = new GameObject(name);
			gameObjects.Add(go);

			AbilityController controller = go.AddComponent<AbilityController>();
			controller.OnAwake();
			SetPrivate(controller, "abilitySeedGenerator", new DeterministicRNG(1));
			SetPrivate(controller, "abilitySeed", 424242);
			SetPrivate(controller, "currentSeed", 777);
			controller.InitializeOnce(new StubCharacter());
			return controller;
		}

		private static FieldInfo Field(object target, string name)
		{
			Type type = target.GetType();
			FieldInfo field = null;
			while (type != null && field == null)
			{
				field = type.GetField(name, Instance);
				type = type.BaseType;
			}
			LogAssert.IsNotNull(field, $"{target.GetType().Name}.{name} must exist.");
			return field;
		}

		private static void SetPrivate(object target, string name, object value)
			=> Field(target, name).SetValue(target, value);

		private static object GetPrivate(object target, string name)
			=> Field(target, name).GetValue(target);

		/// <summary>Minimal <see cref="ICharacter"/> with a real GameObject, for the hit set's key.</summary>
		private sealed class ProbeCharacter : MonoBehaviour, ICharacter
		{
			public long ID { get; set; }
			public string Name => "EconomyProbe";
			public Transform Transform => transform;
			public GameObject GameObject => gameObject;
			public Collider Collider { get; set; }
			public FishNet.Connection.NetworkConnection Owner => null;
			public FishNet.Object.NetworkObject NetworkObject => null;
			public FishNet.Managing.Predicting.PredictionManager PredictionManager => null;
			public HashSet<FishNet.Connection.NetworkConnection> Observers { get; } =
				new HashSet<FishNet.Connection.NetworkConnection>();
			public bool IsTeleporting => false;
			public bool IsSpawned => true;
			public int Flags { get; set; }
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

		#endregion
	}
}
