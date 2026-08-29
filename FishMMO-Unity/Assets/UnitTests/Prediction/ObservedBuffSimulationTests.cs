using System.Collections.Generic;
using System.IO;
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
	/// Pins the contract for a character this peer OBSERVES rather than owns: it holds the real
	/// buff entries, counts their durations down itself, and applies none of their effects.
	/// </summary>
	/// <remarks>
	/// <para>
	/// This replaces the display-list model. A local client is required to know an observed
	/// character's actual state — Inspect and faction/aggro evaluation read it, not just the
	/// renderer — so there is no longer a parallel <c>ObservedBuffs</c> collection, and
	/// <c>Buffs</c> is the single container on every peer.
	/// </para>
	/// <para>
	/// The dangerous half is what an observer must NOT do. Its attribute broadcast already carries
	/// every buff's contribution inside <c>ExternalModifier</c>, and its resource broadcast already
	/// carries the result of every damage-over-time tick, so running the effects locally counts both
	/// twice — the same double-count the 2026-08-28 audit fixed on the spawn payload.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class ObservedBuffSimulationTests
	{
		private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
		private const float TickDelta30 = 1f / 30f;
		private const uint ApplyTick = 100u;

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

		// ── The container ────────────────────────────────────────────────────────────

		/// <summary>An observed character's buffs land in the real container, not a side list.</summary>
		[Test]
		public void ObservedBuffs_MaterializeIntoTheBuffContainer()
		{
			ProbeBuff template = MakeTemplate("ObsSim_Materialize", duration: 10f);
			BuffController observer = MakeObserver("Materialize");

			ApplyObserved(observer, Entry(template, stacks: 2, remaining: 8f));

			LogAssert.AreEqual(1, observer.Buffs.Count,
				"An observed character's buffs belong in Buffs. Inspect and faction/aggro read this " +
				"container, so a display-only projection is not enough.");
			LogAssert.IsTrue(observer.Buffs.ContainsKey(template.ID), "The right template must be present.");
			LogAssert.AreEqual(2, observer.Buffs[template.ID].Stacks, "Stacks must survive materialisation.");
		}

		/// <summary>
		/// A remaining duration is rebased into THIS peer's tick domain.
		/// </summary>
		/// <remarks>
		/// Tick domains are per-client, so an absolute tick from the server is meaningless here. The
		/// wire carries seconds and the receiver converts against its own clock.
		/// </remarks>
		[Test]
		public void ObservedBuffs_RebaseExpiryIntoTheLocalTickDomain()
		{
			ProbeBuff template = MakeTemplate("ObsSim_Rebase", duration: 10f);
			BuffController observer = MakeObserver("Rebase");

			ApplyObserved(observer, Entry(template, stacks: 0, remaining: 3f));

			uint expiry = observer.Buffs[template.ID].ExpiryTick;
			uint expected = ApplyTick + (uint)Mathf.CeilToInt(3f / TickDelta30);

			LogAssert.IsTrue(expiry >= expected - 2 && expiry <= expected + 2,
				$"Expiry must be rebased onto the local tick ({expected}, was {expiry}). Copying the " +
				"sender's absolute tick would expire the buff at an arbitrary moment on this peer.");
		}

		/// <summary>
		/// Zero remaining means PERMANENT, and must not be read as "expires immediately".
		/// </summary>
		[Test]
		public void ObservedBuffs_PermanentNeverExpiresLocally()
		{
			ProbeBuff template = MakeTemplate("ObsSim_Permanent", duration: 0f);
			BuffController observer = MakeObserver("Permanent");

			ApplyObserved(observer, Entry(template, stacks: 0, remaining: 0f));

			LogAssert.AreEqual(TimeManager.UNSET_TICK, observer.Buffs[template.ID].ExpiryTick,
				"A permanent buff must carry UNSET_TICK, which Buff.HasExpired reads as 'never'. " +
				"Mapping zero onto the current tick instead would expire every permanent buff on the " +
				"observer's very next tick.");
			LogAssert.IsFalse(observer.Buffs[template.ID].HasExpired(ApplyTick + 100000u),
				"However far the clock advances, a permanent buff must not expire.");
		}

		/// <summary>A full set is authoritative: anything it does not name is gone.</summary>
		[Test]
		public void ObservedBuffs_FullSetDropsWhatItDoesNotName()
		{
			ProbeBuff a = MakeTemplate("ObsSim_DropA", duration: 10f);
			ProbeBuff b = MakeTemplate("ObsSim_DropB", duration: 10f);
			BuffController observer = MakeObserver("Drop");

			ApplyObserved(observer, Entry(a, 0, 8f), Entry(b, 0, 8f));
			LogAssert.AreEqual(2, observer.Buffs.Count, "Both must arrive first.");

			ApplyObserved(observer, Entry(b, 0, 7f));

			LogAssert.AreEqual(1, observer.Buffs.Count, "The omitted buff must be dropped.");
			LogAssert.IsFalse(observer.Buffs.ContainsKey(a.ID),
				"A full set is the whole picture; a buff it omits has ended. This is also the " +
				"correction channel for a mispredicted buff, so it has to actually remove things.");
		}

		// ── What an observer must NOT do ─────────────────────────────────────────────

		/// <summary>
		/// Materialising must not run the template's apply hooks.
		/// </summary>
		/// <remarks>
		/// This is the double-count guard. The attribute broadcast already carries this buff's
		/// contribution inside <c>ExternalModifier</c>; applying it here as well counts it twice and
		/// leaves the observed character permanently wrong.
		/// </remarks>
		[Test]
		public void ObservedBuffs_DoNotApplyTheirEffects()
		{
			ProbeBuff template = MakeTemplate("ObsSim_NoEffects", duration: 10f);
			BuffController observer = MakeObserver("NoEffects");

			ApplyObserved(observer, Entry(template, stacks: 1, remaining: 8f));

			LogAssert.AreEqual(0, template.ApplyCalls,
				"An observer tracks buffs; it does not apply them. The attribute broadcast already " +
				"carries this buff's modifier, so applying it locally counts it twice.");
			LogAssert.AreEqual(0, template.ApplyStackCalls,
				"Stack contributions are part of the same ExternalModifier and must not be re-applied.");
		}

		/// <summary>
		/// Dropping an observed buff must not reverse effects that were never applied.
		/// </summary>
		[Test]
		public void ObservedBuffs_DoNotReverseEffectsOnRemoval()
		{
			ProbeBuff template = MakeTemplate("ObsSim_NoReverse", duration: 10f);
			BuffController observer = MakeObserver("NoReverse");

			ApplyObserved(observer, Entry(template, 0, 8f));
			ApplyObserved(observer);

			LogAssert.AreEqual(0, observer.Buffs.Count, "The buff must be gone.");
			LogAssert.AreEqual(0, template.RemoveCalls,
				"This peer never applied the modifier, so reversing it would subtract a contribution " +
				"it never added — leaving the character permanently below its real values, the mirror " +
				"image of the double-count.");
		}

		// ── Local ticking ────────────────────────────────────────────────────────────

		/// <summary>
		/// A finite buff expires on the observer's own clock, with no message required.
		/// </summary>
		/// <remarks>
		/// The whole point of local ticking: bars empty on time without a per-tick message, and the
		/// periodic "the numbers have drifted" resend that used to exist is gone.
		/// </remarks>
		[Test]
		public void ObserverTick_ExpiresAFiniteBuffWithoutAMessage()
		{
			ProbeBuff template = MakeTemplate("ObsSim_Expire", duration: 1f);
			BuffController observer = MakeObserver("Expire");

			ApplyObserved(observer, Entry(template, 0, 1f));
			LogAssert.AreEqual(1, observer.Buffs.Count, "Present to begin with.");

			SetDomainTick(observer, ApplyTick + 60u);
			InvokeObserverTick(observer);

			LogAssert.AreEqual(0, observer.Buffs.Count,
				"A buff whose duration ran out must expire from the observer's own tick. Waiting for " +
				"a server message would leave a dead buff on screen for a round trip or more.");
		}

		/// <summary>
		/// A stacked buff must lose one stack per expiry, not refill its own bar forever.
		/// </summary>
		/// <remarks>
		/// Resetting the duration without decrementing was a real bug: the buff would never expire
		/// on an observer and the bar would keep refilling.
		/// </remarks>
		[Test]
		public void ObserverTick_StackedBuffDecrementsRatherThanRefilling()
		{
			ProbeBuff template = MakeTemplate("ObsSim_Stacked", duration: 1f);
			BuffController observer = MakeObserver("Stacked");

			ApplyObserved(observer, Entry(template, stacks: 1, remaining: 1f));

			SetDomainTick(observer, ApplyTick + 60u);
			InvokeObserverTick(observer);

			LogAssert.AreEqual(1, observer.Buffs.Count, "One stack remains, so the buff is still present.");
			LogAssert.AreEqual(0, observer.Buffs[template.ID].Stacks, "The expired stack must be consumed.");
			LogAssert.AreEqual(0, template.RemoveStackCalls,
				"The stack is decremented directly rather than through Buff.RemoveStack, which would " +
				"fire OnRemoveStack and reverse a contribution this peer never applied.");
		}

		// ── The shape of the code ────────────────────────────────────────────────────

		/// <summary>There must be no parallel observed-buff container left.</summary>
		[Test]
		public void BuffController_HasNoParallelObservedContainer()
		{
			string source = File.ReadAllText(Path.Combine(Application.dataPath,
				"Scripts/Shared/Implementation/Entity/Prediction/Buff/BuffController.cs"));

			LogAssert.IsFalse(source.Contains("private ObservedBuffEntry[] observedBuffs"),
				"The parallel display list is gone; Buffs is the single container on every peer.");
			LogAssert.IsFalse(source.Contains("ObservedBuffsReceivedTime"),
				"Nothing re-bases entries by the age of the last message any more — durations are " +
				"simulated locally, so the receipt timestamp has no purpose.");
			LogAssert.IsFalse(source.Contains("ObservedTimingDriftExceedsTolerance"),
				"The periodic timing resend is gone: every peer counts its own bars down, so there " +
				"is no drift for the server to correct.");
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

		/// <summary>Feeds a full observed set straight into the controller's apply funnel.</summary>
		private static void ApplyObserved(BuffController controller, params ObservedBuffEntry[] entries)
		{
			typeof(BuffController)
				.GetMethod("ApplyObservedBuffs", Private)
				.Invoke(controller, new object[] { entries });
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

		private ProbeBuff MakeTemplate(string name, float duration)
		{
			ProbeBuff template = ScriptableObject.CreateInstance<ProbeBuff>();
			template.name = name;
			template.Duration = duration;
			template.TickRate = 1f;
			template.IsPermanent = duration <= 0f;
			template.AddToCache(template.name);
			templates.Add(template);
			return template;
		}

		/// <summary>
		/// An unspawned controller, which reads as neither server nor owner — the observer's role.
		/// </summary>
		private BuffController MakeObserver(string name)
		{
			GameObject go = new GameObject("ObsSim_" + name);
			gameObjects.Add(go);

			BuffController controller = go.AddComponent<BuffController>();
			typeof(BuffController).GetField("tickDelta", Private).SetValue(controller, TickDelta30);
			typeof(BuffController).GetField("lastReplicateTick", Private).SetValue(controller, ApplyTick);
			typeof(BuffController).GetField("hasSeenFirstReplicate", Private).SetValue(controller, true);
			controller.InitializeOnce(new ObserverProbeCharacter());
			return controller;
		}

		/// <summary>Counts the template hooks an observer must never fire.</summary>
		private sealed class ProbeBuff : BaseBuffTemplate
		{
			public int ApplyCalls;
			public int RemoveCalls;
			public int ApplyStackCalls;
			public int RemoveStackCalls;

			public override void OnApply(Buff buff, ICharacter target) { ++ApplyCalls; }
			public override void OnRemove(Buff buff, ICharacter target) { ++RemoveCalls; }
			public override void OnApplyStack(Buff buff, ICharacter target) { ++ApplyStackCalls; }
			public override void OnRemoveStack(Buff buff, ICharacter target) { ++RemoveStackCalls; }
			public override GameObject OnApplyFX(Buff buff, ICharacter target) => null;
			public override void OnRemoveFX(GameObject fxInstance, ICharacter target) { }
		}

		/// <summary>Minimal character; the controller only needs identity and flags here.</summary>
		private sealed class ObserverProbeCharacter : ICharacter
		{
			public long ID { get; set; } = 1;
			public string Name => "ObserverProbe";
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
