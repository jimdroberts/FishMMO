using System.Collections.Generic;
using System.Reflection;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using FishNet.Managing.Timing;
using NUnit.Framework;
using UnityEngine;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Pins the two halves of the observer-buff contract that a DELTA push makes ambiguous: what
	/// the server may re-baseline, and what a receiver may treat as server-stated.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A delta only carries the entries that structurally differ. Everything else on both sides is
	/// carried over locally — the sender's baseline entry, the receiver's own <c>Buffs</c> — and
	/// both used to be mistaken for something the message had stated. On the send side that
	/// re-baselined renewals nobody had been told about, which permanently disarmed
	/// <c>ObservedBuffWillLapse</c> for that buff and let every observer delete an aura the
	/// character was still carrying. On the receive side it confirmed the caster's own predicted
	/// cross-character buff, so a prediction the server had REFUSED outlived its sweep — for a
	/// permanent template, forever.
	/// </para>
	/// <para>
	/// The fixture drives the real methods on an unspawned controller: it reads as neither server
	/// nor owner (the observer's role), <c>GetCurrentDomainTick</c> falls back to the replicate
	/// tick field, and the broadcast declines a null <c>NetworkObject</c>. So a push runs end to
	/// end here without a NetworkManager, minus the bytes.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class ObservedBuffBaselineTests
	{
		private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
		private const float TickDelta30 = 1f / 30f;

		/// <summary>Ticks per second at the project's tick rate, for readable expectations.</summary>
		private const uint TicksPerSecond = 30;

		/// <summary>The controller's own renewal margin, read rather than restated.</summary>
		private static readonly uint MarginTicks = (uint)typeof(BuffController)
			.GetField("OBSERVED_BUFF_RENEWAL_MARGIN_TICKS", BindingFlags.NonPublic | BindingFlags.Static)
			.GetRawConstantValue();

		private readonly List<GameObject> gameObjects = new List<GameObject>();
		private readonly List<BaseBuffTemplate> templates = new List<BaseBuffTemplate>();

		[TearDown]
		public void TearDown()
		{
			for (int i = 0; i < templates.Count; ++i)
			{
				templates[i].RemoveFromCache();
				Object.DestroyImmediate(templates[i]);
			}
			templates.Clear();

			for (int i = 0; i < gameObjects.Count; ++i)
			{
				if (gameObjects[i] != null)
				{
					Object.DestroyImmediate(gameObjects[i]);
				}
			}
			gameObjects.Clear();
		}

		// ── The renewal rule ─────────────────────────────────────────────────────────

		/// <summary>
		/// The whole truth table for "must this renewal be restated before observers delete it".
		/// </summary>
		/// <remarks>
		/// Pure arithmetic over four ticks, so the rule can be read and tested without a
		/// NetworkManager — the same seam <c>ServerCancelsDirectly</c> and
		/// <c>ResolvesHitsOnThisPeer</c> provide for their decisions.
		/// </remarks>
		[Test]
		public void ObservedRenewalNeedsPush_TruthTable()
		{
			const uint now = 1000;
			uint imminent = now + MarginTicks;   // their countdown empties inside the margin
			uint distant = now + 500;            // ...and well outside it

			LogAssert.IsFalse(BuffController.ObservedRenewalNeedsPush(TimeManager.UNSET_TICK, now + 900, imminent, MarginTicks),
				"With no clock there is no comparison to make, and a push cannot be justified by a tick nobody has.");

			LogAssert.IsFalse(BuffController.ObservedRenewalNeedsPush(now, now + 900, TimeManager.UNSET_TICK, MarginTicks),
				"Observers told 'permanent' count towards nothing. Their bar never empties, so there is " +
				"nothing to get ahead of.");

			LogAssert.IsFalse(BuffController.ObservedRenewalNeedsPush(now, now + 900, distant, MarginTicks),
				"A renewal nobody is about to lose costs nothing to leave unsent — the whole point of " +
				"the gate is that ordinary countdown needs no message.");

			LogAssert.IsTrue(BuffController.ObservedRenewalNeedsPush(now, TimeManager.UNSET_TICK, imminent, MarginTicks),
				"Permanent here and finite there means observers are about to delete something this " +
				"peer holds forever. Unreachable while IsPermanent is authored per template, and still " +
				"the honest answer.");

			LogAssert.IsFalse(BuffController.ObservedRenewalNeedsPush(now, now - 1, imminent, MarginTicks),
				"Already expired here: this tick's Tick() removes it, and that removal is structural " +
				"and travels on its own. A renewal message as well would be a full set for a buff that " +
				"is leaving anyway.");

			LogAssert.IsFalse(BuffController.ObservedRenewalNeedsPush(now, imminent + MarginTicks, imminent, MarginTicks),
				"Expiring within the margin of their own zero is the SAME expiry, quantised. Firing " +
				"here would add a full set shortly before every buff in the game ran out.");

			LogAssert.IsTrue(BuffController.ObservedRenewalNeedsPush(now, imminent + MarginTicks + 1, imminent, MarginTicks),
				"One tick past the margin is a genuine renewal, and observers are about to delete it.");
		}

		/// <summary>
		/// The baseline's observer expiry must be exactly what the receiver will compute.
		/// </summary>
		/// <remarks>
		/// The server predicts the observers' own countdown rather than approximating it, so both
		/// sides of <c>ObservedRenewalNeedsPush</c> are the same arithmetic on the same numbers. If
		/// these two ever diverge the margin silently absorbs the difference until it cannot.
		/// </remarks>
		[Test]
		public void ComputeObserverExpiryTick_MatchesWhatTheReceiverMaterialises()
		{
			ProbeBuff template = MakeTemplate("Baseline_Mirror", duration: 10f);
			BuffController observer = MakeController("Mirror");
			SetDomainTick(observer, 500);

			ApplyObserved(observer, Entry(template, stacks: 0, remaining: 7.5f));

			LogAssert.AreEqual(observer.Buffs[template.ID].ExpiryTick,
				BuffController.ComputeObserverExpiryTick(500, 7.5f, TickDelta30),
				"The tick the baseline records must be the tick the receiver actually counts down to.");

			LogAssert.AreEqual(TimeManager.UNSET_TICK,
				BuffController.ComputeObserverExpiryTick(500, 0f, TickDelta30),
				"Zero remaining is PERMANENT on this wire, so it maps to 'never' — not to 'now', which " +
				"would make every permanent buff look like it lapses on the next tick.");

			LogAssert.AreEqual(TimeManager.UNSET_TICK,
				BuffController.ComputeObserverExpiryTick(TimeManager.UNSET_TICK, 7.5f, TickDelta30),
				"A number that could not be measured cannot be counted down from.");
		}

		// ── The baseline records what was SENT ───────────────────────────────────────

		/// <summary>
		/// A delta push must not re-baseline a renewal it did not carry.
		/// </summary>
		/// <remarks>
		/// The concrete failure: a 10 s aura refreshed every tick, an unrelated buff landing halfway
		/// through. The delta contains only the unrelated buff — a refresh is structurally identical,
		/// so the aura is not in it and observers are never told its new duration — but the baseline
		/// used to record the new duration anyway. The lapse check then measured the server's expiry
		/// against itself, could never fire, and every observer deleted the aura at the deadline they
		/// were actually holding while the character still carried it.
		/// </remarks>
		[Test]
		public void DeltaPush_DoesNotRebaselineAnUnpushedRenewal()
		{
			ProbeBuff aura = MakeTemplate("Baseline_Aura", duration: 10f);
			ProbeBuff other = MakeTemplate("Baseline_Other", duration: 5f);

			BuffController server = MakeController("Renewal");
			SetDomainTick(server, 100);

			// Observers are told the aura has 10 s left, so they will delete it at their tick 400.
			ApplyObserved(server, Entry(aura, stacks: 0, remaining: 10f));
			Push(server);

			uint observerExpiry = 100 + 10 * TicksPerSecond;
			LogAssert.AreEqual(observerExpiry, server.Buffs[aura.ID].ExpiryTick,
				"Sanity: the strip that went out is the one the observers now hold.");

			/* 150 ticks later the aura has been refreshed — it now runs to 550 — and an unrelated
			 * buff lands, which is what makes the push a delta rather than a full set. */
			SetDomainTick(server, 250);
			ApplyObserved(server,
				Entry(aura, stacks: 0, remaining: 10f),
				Entry(other, stacks: 0, remaining: 5f));
			Push(server);

			uint serverExpiry = 250 + 10 * TicksPerSecond;
			LogAssert.AreEqual(serverExpiry, server.Buffs[aura.ID].ExpiryTick,
				"Sanity: the character's own aura was refreshed to 550.");

			SetDomainTick(server, 300);
			LogAssert.IsFalse(WillLapse(server),
				"Nothing is about to be deleted yet, so the gate must stay quiet — it is a lapse " +
				"check, not a drift tolerance.");

			SetDomainTick(server, observerExpiry - MarginTicks);
			LogAssert.IsTrue(WillLapse(server),
				"The observers reach zero on the duration they were TOLD, which the delta never " +
				"updated. Re-baselining the aura to the server's own 550 made this unanswerable and " +
				"the aura vanished from every observer — from Buffs as well as the strip, which " +
				"Inspect, the target frame and aggro all read.");
		}

		/// <summary>
		/// A push that sends nothing must not re-baseline either.
		/// </summary>
		/// <remarks>
		/// The empty-delta path — a buff added and removed inside one tick, a stack that went up and
		/// back down — used to adopt the whole current strip for free, wiping the record of every
		/// pending un-pushed renewal without a byte leaving the server.
		/// </remarks>
		[Test]
		public void EmptyDeltaPush_KeepsThePendingRenewal()
		{
			ProbeBuff aura = MakeTemplate("Baseline_EmptyAura", duration: 10f);

			BuffController server = MakeController("EmptyDelta");
			SetDomainTick(server, 100);

			ApplyObserved(server, Entry(aura, stacks: 0, remaining: 10f));
			Push(server);

			uint observerExpiry = 100 + 10 * TicksPerSecond;

			// Refreshed, then marked dirty by a change that cancels itself out inside the tick.
			SetDomainTick(server, 250);
			ApplyObserved(server, Entry(aura, stacks: 0, remaining: 10f));
			Push(server);

			SetDomainTick(server, observerExpiry - MarginTicks);
			LogAssert.IsTrue(WillLapse(server),
				"A push that sent nothing said nothing, so the baseline must still describe what " +
				"observers hold. Adopting the strip here lost the renewal for free.");
		}

		/// <summary>
		/// The renewal full set must satisfy the gate, so it fires once rather than every tick.
		/// </summary>
		[Test]
		public void RenewalPush_ClearsTheLapse()
		{
			ProbeBuff aura = MakeTemplate("Baseline_Settle", duration: 10f);

			BuffController server = MakeController("Settle");
			SetDomainTick(server, 100);
			ApplyObserved(server, Entry(aura, stacks: 0, remaining: 10f));
			Push(server);

			uint observerExpiry = 100 + 10 * TicksPerSecond;

			SetDomainTick(server, 250);
			ApplyObserved(server, Entry(aura, stacks: 0, remaining: 10f));

			SetDomainTick(server, observerExpiry - MarginTicks);
			LogAssert.IsTrue(WillLapse(server), "Sanity: the renewal is due.");

			// A renewal alone is not describable as a delta, so this is a full set.
			InvokePush(server);

			LogAssert.IsFalse(WillLapse(server),
				"Once the renewal has gone out the observers are counting the new duration, so the " +
				"gate must fall silent. A gate that stayed true would push a full set every tick for " +
				"the rest of the buff's life.");
		}

		/// <summary>A permanent buff has nothing to lapse into and must never trigger the gate.</summary>
		[Test]
		public void PermanentBuff_NeverLapses()
		{
			ProbeBuff permanent = MakeTemplate("Baseline_Permanent", duration: 30f, permanent: true);

			BuffController server = MakeController("Permanent");
			SetDomainTick(server, 100);
			ApplyObserved(server, Entry(permanent, stacks: 0, remaining: 0f));
			Push(server);

			SetDomainTick(server, 100 + 60 * TicksPerSecond);
			LogAssert.IsFalse(WillLapse(server),
				"Observers were told zero remaining, which they read as 'never expires'. There is no " +
				"deadline to beat, and a push here would repeat forever.");
		}

		// ── Provenance: what the SERVER stated ───────────────────────────────────────

		/// <summary>What each message shape does and does not say about a template.</summary>
		[Test]
		public void ObservedBuffStatement_TruthTable()
		{
			ObservedBuffEntry a = new ObservedBuffEntry { TemplateID = 1 };
			ObservedBuffEntry b = new ObservedBuffEntry { TemplateID = 2 };

			BuffController.ObservedBuffStatement full = BuffController.ObservedBuffStatement.WholeStrip(new[] { a });
			BuffController.ObservedBuffStatement delta = BuffController.ObservedBuffStatement.Delta(new[] { a }, new[] { 3 });

			LogAssert.IsTrue(full.Names(a.TemplateID), "A full set names everything in it.");
			LogAssert.IsFalse(full.Names(b.TemplateID), "A full set does not name what it leaves out...");
			LogAssert.IsTrue(full.NamesAbsence(b.TemplateID), "...it states its ABSENCE, which is a statement too.");
			LogAssert.IsFalse(full.NamesAbsence(a.TemplateID), "Something it carries is not absent.");

			LogAssert.IsTrue(delta.Names(a.TemplateID), "A delta names its changed entries.");
			LogAssert.IsTrue(delta.Names(3), "A delta names its removed ids.");
			LogAssert.IsFalse(delta.Names(b.TemplateID),
				"A delta says NOTHING about the templates it leaves out. Reading its silence as a " +
				"statement is what let an unrelated delta confirm a refused prediction.");
			LogAssert.IsFalse(delta.NamesAbsence(b.TemplateID),
				"Unmentioned is not absent: a delta that omits a template is not claiming the strip " +
				"has lost it.");
			LogAssert.IsTrue(delta.NamesAbsence(3), "Its removed ids are exactly the absences it states.");
		}

		/// <summary>
		/// A delta for someone else's buff must not confirm this client's own prediction.
		/// </summary>
		/// <remarks>
		/// The merged array a delta produces is mostly the receiver's own container read back out, so
		/// a locally-predicted phantom rides along inside it. Treating that array as server-named
		/// dropped the confirmation deadline on the first unrelated push — which in combat arrives
		/// well inside the three-second window — and a PERMANENT phantom then never went away.
		/// </remarks>
		[Test]
		public void UnrelatedDelta_DoesNotConfirmALocalPrediction()
		{
			ProbeBuff phantom = MakeTemplate("Baseline_Phantom", duration: 30f, permanent: true);
			ProbeBuff other = MakeTemplate("Baseline_PhantomOther", duration: 5f);

			BuffController target = MakeController("Phantom");
			SetDomainTick(target, 100);

			// The caster's client predicts a cross-character buff into a controller it does not own.
			ApplyPredicted(target, phantom, 100);
			LogAssert.IsTrue(HasPendingConfirmation(target, phantom.ID),
				"Sanity: a prediction on a non-simulating peer is provisional until the server names it.");

			Merge(target, new[] { Entry(other, stacks: 0, remaining: 5f) }, System.Array.Empty<int>());

			LogAssert.IsTrue(HasPendingConfirmation(target, phantom.ID),
				"The server said nothing about the phantom — the delta was about another buff entirely. " +
				"Confirming it here is how a refused prediction survived its own sweep.");

			// Past the deadline the sweep must still be able to reclaim it.
			SetDomainTick(target, 100 + (uint)Mathf.CeilToInt(3f / TickDelta30) + 1);
			InvokeObserverTick(target);

			LogAssert.IsFalse(target.Buffs.ContainsKey(phantom.ID),
				"A refused PERMANENT prediction has no local countdown to run out. Absence past the " +
				"deadline is the only signal there is, so losing the deadline meant the phantom icon " +
				"and FX stayed forever.");
		}

		/// <summary>A delta that names the template DOES confirm it.</summary>
		[Test]
		public void DeltaNamingTheTemplate_ConfirmsThePrediction()
		{
			ProbeBuff predicted = MakeTemplate("Baseline_Confirmed", duration: 30f, permanent: true);

			BuffController target = MakeController("Confirmed");
			SetDomainTick(target, 100);
			ApplyPredicted(target, predicted, 100);

			Merge(target, new[] { Entry(predicted, stacks: 0, remaining: 0f) }, System.Array.Empty<int>());

			LogAssert.IsFalse(HasPendingConfirmation(target, predicted.ID),
				"The server stated this template, so the prediction is confirmed and must stop being " +
				"provisional.");

			SetDomainTick(target, 100 + (uint)Mathf.CeilToInt(3f / TickDelta30) + 1);
			InvokeObserverTick(target);

			LogAssert.IsTrue(target.Buffs.ContainsKey(predicted.ID),
				"A confirmed permanent buff must survive the sweep. Dropping it would flicker the icon " +
				"off and back on with the next push.");
		}

		/// <summary>A delta that REMOVES the template settles it too.</summary>
		[Test]
		public void DeltaRemovingTheTemplate_SettlesThePrediction()
		{
			ProbeBuff predicted = MakeTemplate("Baseline_Refused", duration: 30f, permanent: true);

			BuffController target = MakeController("Refused");
			SetDomainTick(target, 100);
			ApplyPredicted(target, predicted, 100);

			Merge(target, System.Array.Empty<ObservedBuffEntry>(), new[] { predicted.ID });

			LogAssert.IsFalse(target.Buffs.ContainsKey(predicted.ID),
				"A removed id leaves the strip immediately; there is nothing left to wait for.");
			LogAssert.IsFalse(HasPendingConfirmation(target, predicted.ID),
				"The server stated this template's ABSENCE, which settles the prediction as surely as " +
				"naming it would. A deadline left behind would sweep a template that is already gone.");
		}

		/// <summary>A full set confirms by naming, exactly as it always did.</summary>
		[Test]
		public void FullSet_ConfirmsAndDenies()
		{
			ProbeBuff named = MakeTemplate("Baseline_FullNamed", duration: 30f, permanent: true);
			ProbeBuff unnamed = MakeTemplate("Baseline_FullUnnamed", duration: 30f, permanent: true);

			BuffController target = MakeController("FullSet");
			SetDomainTick(target, 100);
			ApplyPredicted(target, named, 100);
			ApplyPredicted(target, unnamed, 100);

			ApplyObserved(target, Entry(named, stacks: 0, remaining: 0f));

			LogAssert.IsFalse(HasPendingConfirmation(target, named.ID),
				"A full set names its entries, so a prediction it carries is confirmed.");
			LogAssert.IsFalse(target.Buffs.ContainsKey(unnamed.ID),
				"A full set states the whole strip, so a template it omits is gone.");
			LogAssert.IsFalse(HasPendingConfirmation(target, unnamed.ID),
				"That omission is itself a statement, so the prediction is settled — refused — rather " +
				"than left pending against a template the receiver no longer holds.");
		}

		// ── Carried entries keep their meaning ───────────────────────────────────────

		/// <summary>
		/// A permanent buff carried through a delta must stay permanent.
		/// </summary>
		/// <remarks>
		/// Zero remaining means PERMANENT on this wire, but <c>Buff.RemainingSeconds</c> answers a
		/// permanent buff with its authored Duration. Describing carried entries with that number
		/// handed the receiver a finite duration, so the first delta about any OTHER buff gave a
		/// permanent one an expiry and the observer deleted it a Duration later — the same class of
		/// failure as the un-pushed renewal, from the other side of the wire.
		/// </remarks>
		[Test]
		public void MergeCarriedEntry_KeepsAPermanentBuffPermanent()
		{
			ProbeBuff permanent = MakeTemplate("Baseline_CarriedPermanent", duration: 30f, permanent: true);
			ProbeBuff other = MakeTemplate("Baseline_CarriedOther", duration: 5f);

			BuffController observer = MakeController("Carried");
			SetDomainTick(observer, 100);
			ApplyObserved(observer, Entry(permanent, stacks: 0, remaining: 0f));

			LogAssert.AreEqual(TimeManager.UNSET_TICK, observer.Buffs[permanent.ID].ExpiryTick,
				"Sanity: a permanent buff carries UNSET_TICK, which Buff.HasExpired reads as 'never'.");

			Merge(observer, new[] { Entry(other, stacks: 0, remaining: 5f) }, System.Array.Empty<int>());

			LogAssert.AreEqual(TimeManager.UNSET_TICK, observer.Buffs[permanent.ID].ExpiryTick,
				"A delta that never mentioned the permanent buff must not give it an expiry. Reading " +
				"its authored Duration as 'remaining' made every unrelated push start a countdown on " +
				"a buff the character holds forever.");
		}

		/// <summary>A finite carried entry keeps counting down against its own expiry.</summary>
		[Test]
		public void MergeCarriedEntry_KeepsAFiniteBuffOnItsOwnClock()
		{
			ProbeBuff finite = MakeTemplate("Baseline_CarriedFinite", duration: 10f);
			ProbeBuff other = MakeTemplate("Baseline_CarriedFiniteOther", duration: 5f);

			BuffController observer = MakeController("CarriedFinite");
			SetDomainTick(observer, 100);
			ApplyObserved(observer, Entry(finite, stacks: 0, remaining: 10f));

			uint expiry = observer.Buffs[finite.ID].ExpiryTick;

			SetDomainTick(observer, 250);
			Merge(observer, new[] { Entry(other, stacks: 0, remaining: 5f) }, System.Array.Empty<int>());

			LogAssert.AreEqual(expiry, observer.Buffs[finite.ID].ExpiryTick,
				"A bar the delta did not mention is already correct — its expiry is in this peer's own " +
				"tick domain and advances with it. A carried entry must not shift its own deadline.");
		}

		// ── Helpers ──────────────────────────────────────────────────────────────────

		private static ObservedBuffEntry Entry(BaseBuffTemplate template, int stacks, float remaining)
			=> new ObservedBuffEntry
			{
				TemplateID = template.ID,
				Stacks = stacks,
				RemainingSeconds = remaining,
				TotalSeconds = template.Duration,
			};

		/// <summary>Feeds a full observed set through the receive path.</summary>
		private static void ApplyObserved(BuffController controller, params ObservedBuffEntry[] entries)
		{
			typeof(BuffController)
				.GetMethod("ApplyObservedBuffs", Private)
				.Invoke(controller, new object[] { entries });
		}

		/// <summary>Feeds a delta through the receive path.</summary>
		private static void Merge(BuffController controller, ObservedBuffEntry[] changed, int[] removed)
		{
			typeof(BuffController)
				.GetMethod("MergeObservedBuffs", Private)
				.Invoke(controller, new object[] { changed, removed });
		}

		/// <summary>
		/// Applies a buff the way a CASTER's client predicts one into a controller it does not
		/// simulate: provisional, and swept if the server never names it.
		/// </summary>
		private static void ApplyPredicted(BuffController controller, BaseBuffTemplate template, uint tick)
		{
			typeof(BuffController)
				.GetMethod("ApplyResolved", Private)
				.Invoke(controller, new object[] { template, tick, null, true });
		}

		/// <summary>Marks the strip structurally dirty and runs the real push.</summary>
		private static void Push(BuffController controller)
		{
			typeof(BuffController).GetField("observedBuffsDirty", Private).SetValue(controller, true);
			InvokePush(controller);
		}

		/// <summary>Runs the real push with the dirty flag exactly as it stands.</summary>
		private static void InvokePush(BuffController controller)
		{
			typeof(BuffController).GetMethod("PushObservedBuffs", Private).Invoke(controller, null);
		}

		private static bool WillLapse(BuffController controller)
		{
			return (bool)typeof(BuffController)
				.GetMethod("ObservedBuffWillLapse", Private)
				.Invoke(controller, null);
		}

		private static bool HasPendingConfirmation(BuffController controller, int templateID)
		{
			Dictionary<int, uint> pending = (Dictionary<int, uint>)typeof(BuffController)
				.GetField("predictedUnconfirmedBuffs", Private)
				.GetValue(controller);
			return pending != null && pending.ContainsKey(templateID);
		}

		private static void InvokeObserverTick(BuffController controller)
		{
			typeof(BuffController)
				.GetMethod("ObserverTimeManager_OnTick", Private)
				.Invoke(controller, null);
		}

		private static void SetDomainTick(BuffController controller, uint tick)
		{
			typeof(BuffController).GetField("lastReplicateTick", Private).SetValue(controller, tick);
		}

		private ProbeBuff MakeTemplate(string name, float duration, bool permanent = false)
		{
			ProbeBuff template = ScriptableObject.CreateInstance<ProbeBuff>();
			template.name = name;
			template.Duration = duration;
			template.TickRate = 1f;
			template.IsPermanent = permanent;
			template.AddToCache(template.name);
			templates.Add(template);
			return template;
		}

		/// <summary>
		/// An unspawned controller, which reads as neither server nor owner — the tracking-only
		/// role, and the only one where the receive path and the sweep both run.
		/// </summary>
		private BuffController MakeController(string name)
		{
			GameObject go = new GameObject("ObsBaseline_" + name);
			gameObjects.Add(go);

			BuffController controller = go.AddComponent<BuffController>();
			typeof(BuffController).GetField("tickDelta", Private).SetValue(controller, TickDelta30);
			typeof(BuffController).GetField("hasSeenFirstReplicate", Private).SetValue(controller, true);
			controller.InitializeOnce(new BaselineProbeCharacter());
			return controller;
		}

		/// <summary>A template with no effects of its own; these tests only move durations about.</summary>
		private sealed class ProbeBuff : BaseBuffTemplate
		{
			public override void OnApply(Buff buff, ICharacter target) { }
			public override void OnRemove(Buff buff, ICharacter target) { }
			public override GameObject OnApplyFX(Buff buff, ICharacter target) => null;
			public override void OnRemoveFX(GameObject fxInstance, ICharacter target) { }
		}

		/// <summary>Minimal character; the controller only needs identity and flags here.</summary>
		private sealed class BaselineProbeCharacter : ICharacter
		{
			public long ID { get; set; } = 1;
			public string Name => "BaselineProbe";
			public Transform Transform => null;
			public GameObject GameObject => null;
			public Collider Collider { get; set; }
			public FishNet.Connection.NetworkConnection Owner => null;
			public FishNet.Object.NetworkObject NetworkObject => null;
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
