using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using FishNet.Connection;
using FishNet.Managing.Predicting;
using FishNet.Object;
using FishNet.Serializing;
using NUnit.Framework;
using UnityEngine;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Pins what a peer that does NOT own a character is allowed to learn about its buffs, and what
	/// it does with what it learns.
	/// </summary>
	/// <remarks>
	/// <para>
	/// State forwarding is off on every prefab, so an observer never runs Replicate or OnReconcile
	/// for somebody else's character. The spawn payload used to hand it the owner's simulation
	/// anyway, which <c>BuffController.Apply</c> then wrote into the observer's dictionary — FX
	/// instantiated, attribute modifiers applied — where nothing would ever tick it. Those buffs
	/// never expired: a poison that had already killed its victim was still slowing them on every
	/// onlooker's screen. The payload now carries the display list instead, and these tests fail if
	/// the simulation shape ever reaches a non-owner again.
	/// </para>
	/// <para>
	/// Every test that reads a payload also asserts <c>reader.Remaining == 0</c>. FishNet packs all
	/// behaviours' payloads into ONE buffer with no per-behaviour framing, so a reader that stops a
	/// byte early or late does not corrupt buffs — it corrupts whichever behaviour is decoded next.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class BuffObserverStateTests
	{
		private const BindingFlags PrivateInstanceFlags = BindingFlags.Instance | BindingFlags.NonPublic;
		private const float TickDelta30 = 1f / 30f;
		private const uint ApplyTick = 100u;

		/// <summary>Payload shape flag for the owner's full simulation block.</summary>
		private const byte ShapeSimulation = 0;

		/// <summary>Payload shape flag for the observer's display block.</summary>
		private const byte ShapeObserved = 1;

		private readonly List<GameObject> gameObjects = new List<GameObject>();
		private readonly List<BaseBuffTemplate> templates = new List<BaseBuffTemplate>();

		[TearDown]
		public void TearDown()
		{
			for (int i = 0; i < templates.Count; ++i)
			{
				templates[i].RemoveFromCache();
				UnityEngine.Object.DestroyImmediate(templates[i]);
			}
			templates.Clear();

			for (int i = 0; i < gameObjects.Count; ++i)
			{
				if (gameObjects[i] != null)
				{
					UnityEngine.Object.DestroyImmediate(gameObjects[i]);
				}
			}
			gameObjects.Clear();
		}

		// ── Payload shapes ───────────────────────────────────────────────────────────

		/// <summary>
		/// A non-owner's payload block must fill the display list and leave the simulation empty.
		/// </summary>
		[Test]
		public void ReadPayload_ObserverShape_FillsObservedBuffsAndLeavesSimulationEmpty()
		{
			ProbeBuffTemplate visible = MakeTemplate("BuffObserverState_Visible", duration: 10f, hidden: false);
			ProbeBuffTemplate hidden = MakeTemplate("BuffObserverState_Hidden", duration: 10f, hidden: true);

			BuffController sender = MakeController("ObserverPayloadSender", characterID: 1);
			sender.Apply(visible, new PredictionTick(ApplyTick));
			sender.Apply(hidden, new PredictionTick(ApplyTick));

			// conn is null, which PayloadVisibility classifies as "not the owner" — the same answer
			// every observer and FishNet's EmptyConnection get.
			Writer writer = new Writer();
			sender.WritePayload(null, writer);

			BuffController receiver = MakeController("ObserverPayloadReceiver", characterID: 2);
			Reader reader = new Reader(writer.GetArraySegment(), null);
			receiver.ReadPayload(null, reader);

			TestContext.WriteLine(
				$"MEASURE observer spawn payload: {sender.Buffs.Count} buffs live → " +
				$"{receiver.ObservedBuffs.Count} observed entries in {writer.Length} B");

			LogAssert.AreEqual(0, receiver.Buffs.Count,
				"A non-owner must not receive simulation state. A buff in this dictionary would never " +
				"be ticked — nothing runs Replicate for a character this peer does not own — so it " +
				"would hold its attribute modifiers on this copy of the character forever.");
			LogAssert.AreEqual(1, receiver.ObservedBuffs.Count,
				"The observer block must carry the visible buff.");
			LogAssert.AreEqual(visible.ID, receiver.ObservedBuffs[0].TemplateID,
				"The wrong template survived the filter.");
			LogAssert.AreEqual(10f, receiver.ObservedBuffs[0].TotalSeconds,
				"TotalSeconds is not on the wire; the receiver must read it off the template it already has.");
			LogAssert.IsTrue(Mathf.Abs(receiver.ObservedBuffs[0].RemainingSeconds - 10f) < 0.05f,
				$"Remaining seconds should be the full duration on the tick of application, was " +
				$"{receiver.ObservedBuffs[0].RemainingSeconds}.");
			LogAssert.AreEqual(0, reader.Remaining,
				"The framed block must be consumed exactly, or every behaviour decoded after this one reads garbage.");
		}

		/// <summary>
		/// The owner's payload block must restore the full simulation, hidden buffs included.
		/// </summary>
		/// <remarks>
		/// Written by hand because the owner shape needs a spawned NetworkObject with a valid owning
		/// connection, which an EditMode test cannot construct — <c>PayloadVisibility.IsOwner</c>
		/// answers "not the owner" for every connection an unspawned behaviour can be handed. The
		/// bytes below are exactly what <c>WritePayload</c> emits for an owner.
		/// </remarks>
		[Test]
		public void ReadPayload_OwnerShape_RestoresSimulationIncludingHiddenBuffs()
		{
			ProbeBuffTemplate visible = MakeTemplate("BuffOwnerPayload_Visible", duration: 10f, hidden: false);
			ProbeBuffTemplate hidden = MakeTemplate("BuffOwnerPayload_Hidden", duration: 10f, hidden: true);

			Writer body = new Writer();
			body.WriteUInt8Unpacked(ShapeSimulation);
			body.WriteInt32(2);
			WriteSimulationEntry(body, visible.ID, expiryTick: ApplyTick + 300u, nextTickTick: ApplyTick + 30u, stacks: 2, tickCount: 3, cumulative: 5);
			WriteSimulationEntry(body, hidden.ID, expiryTick: ApplyTick + 150u, nextTickTick: ApplyTick + 30u, stacks: 0, tickCount: 0, cumulative: 0);

			BuffController receiver = MakeController("OwnerPayloadReceiver", characterID: 3);
			Reader reader = new Reader(FramePayload(ApplyTick, body).GetArraySegment(), null);
			receiver.ReadPayload(null, reader);

			LogAssert.AreEqual(2, receiver.Buffs.Count,
				"The owner must restore its whole simulation, including its own HiddenFromOthers buffs — " +
				"they are prediction state, not decoration.");
			LogAssert.IsTrue(receiver.Buffs.ContainsKey(hidden.ID),
				"A hidden buff is still the owner's own state and must survive the payload.");
			LogAssert.AreEqual(ApplyTick + 300u, receiver.Buffs[visible.ID].ExpiryTick,
				"Absolute ticks must survive verbatim when the writer and reader share a tick domain.");
			LogAssert.AreEqual(2, receiver.Buffs[visible.ID].Stacks, "Stacks must round trip.");
			LogAssert.AreEqual(3, receiver.Buffs[visible.ID].TickCount, "TickCount must round trip.");
			LogAssert.AreEqual(5, receiver.Buffs[visible.ID].CumulativeTickMultiplier,
				"CumulativeTickMultiplier must round trip, or OnRemove cannot reverse what the ticks applied.");
			LogAssert.AreEqual(0, reader.Remaining, "The framed block must be consumed exactly.");
		}

		/// <summary>
		/// A payload whose shape flag this build does not understand must still consume exactly its
		/// own frame.
		/// </summary>
		/// <remarks>
		/// This is the whole point of framing the block and putting the shape flag INSIDE the frame:
		/// a version mismatch costs one behaviour's state, not the rest of the packet. The sentinel
		/// written after the frame stands in for the next behaviour's payload.
		/// </remarks>
		[Test]
		public void ReadPayload_UnknownShape_SkipsExactlyItsOwnFrame()
		{
			Writer body = new Writer();
			body.WriteUInt8Unpacked(99);
			body.WriteInt32(1234);
			body.WriteInt32(5678);

			Writer full = FramePayload(ApplyTick, body);
			full.WriteInt32(0x5EED);

			BuffController receiver = MakeController("UnknownShapeReceiver", characterID: 4);
			Reader reader = new Reader(full.GetArraySegment(), null);

			UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
			try
			{
				receiver.ReadPayload(null, reader);
			}
			finally
			{
				UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;
			}

			LogAssert.AreEqual(0, receiver.Buffs.Count, "An unreadable payload must not leave partial state behind.");
			LogAssert.AreEqual(0x5EED, reader.ReadInt32(),
				"The reader must land exactly at the end of this behaviour's frame so the next " +
				"behaviour's payload decodes correctly.");
			LogAssert.AreEqual(0, reader.Remaining, "Nothing but the sentinel should remain.");
		}

		// ── Observer FX ──────────────────────────────────────────────────────────────

		/// <summary>
		/// Buff FX on an observer follows the observed-list diff: added templates spawn, removed
		/// templates are torn down, and a template present in both is left alone.
		/// </summary>
		/// <remarks>
		/// The observer runs no buff simulation, so nothing on the apply/remove path ever fires for
		/// it — the arrival of a new list IS the event. Before this, an observer's FX came from the
		/// spawn payload only, so a buff gained while you were already watching had no FX at all and
		/// one you arrived after had FX that never stopped.
		/// </remarks>
		[Test]
		public void ObservedBuffs_Diff_SpawnsAndTearsDownFXPerTemplate()
		{
			ProbeBuffTemplate a = MakeTemplate("BuffFX_A", duration: 10f, hidden: false);
			ProbeBuffTemplate b = MakeTemplate("BuffFX_B", duration: 10f, hidden: false);

			BuffController observer = MakeController("ObserverFX", characterID: 5);

			ReadObservedPayload(observer, Entry(a, 0, 10f));
			LogAssert.AreEqual(1, a.ApplyFXCalls, "The arriving buff must spawn its FX.");
			LogAssert.AreEqual(0, a.RemoveFXCalls, "Nothing left yet.");

			// Same buff, one more stack, plus a second buff.
			ReadObservedPayload(observer, Entry(a, 1, 9f), Entry(b, 0, 10f));
			LogAssert.AreEqual(1, a.ApplyFXCalls,
				"A buff that merely changed stacks must not restart its effect — one instance per template, " +
				"not per stack and not per update.");
			LogAssert.AreEqual(1, b.ApplyFXCalls, "The newly visible buff must spawn its FX.");
			LogAssert.AreEqual(0, a.RemoveFXCalls + b.RemoveFXCalls, "Neither buff has left.");

			GameObject aInstance = a.LastSpawned;
			ReadObservedPayload(observer, Entry(b, 0, 8f));
			LogAssert.AreEqual(1, a.RemoveFXCalls,
				"A buff that left the observed set must have its FX torn down; before this the instance " +
				"leaked for the lifetime of the character.");
			LogAssert.AreSame(aInstance, a.LastRemoved,
				"OnRemoveFX must be handed the instance OnApplyFX returned for that template.");
			LogAssert.AreEqual(1, b.ApplyFXCalls, "The surviving buff's effect must not be restarted.");

			ReadObservedPayload(observer);
			LogAssert.AreEqual(1, b.RemoveFXCalls, "An empty list must tear down everything that was showing.");
			LogAssert.AreEqual(1, a.ApplyFXCalls, "A removed buff must not be respawned by the empty list.");
		}

		// ── Push gating ──────────────────────────────────────────────────────────────

		/// <summary>
		/// Only a structural change — or a drift big enough to see — is worth an observer message.
		/// </summary>
		/// <remarks>
		/// A Region stay-trigger or an aura re-applies its buff on EVERY tick. Each re-application
		/// moves ExpiryTick, and marking that as a change pushed a reliable observer message thirty
		/// times a second, forever, for a bar that is simply pinned full.
		/// </remarks>
		[Test]
		public void ObservedBuffs_PureDriftRefresh_DoesNotPushUntilItIsVisible()
		{
			ProbeBuffTemplate aura = MakeTemplate("BuffDrift_Aura", duration: 10f, hidden: false);
			BuffController controller = MakeController("DriftController", characterID: 6);

			controller.Apply(aura, new PredictionTick(ApplyTick));
			LogAssert.IsTrue(ShouldPush(controller), "A brand new buff is a structural change and must be pushed.");

			// Consume the push and record the baseline the drift is measured against.
			InvokePrivate(controller, "PushObservedBuffs");
			LogAssert.IsFalse(ShouldPush(controller), "Nothing changed since the push.");

			// Ten ticks later (a third of a second) the aura re-applies: expiry moves, the set does not.
			SetPrivateField(controller, "lastReplicateTick", ApplyTick + 10u);
			controller.Apply(aura, new PredictionTick(ApplyTick + 10u));
			LogAssert.IsFalse(GetPrivateField<bool>(controller, "observedBuffsDirty"),
				"A refresh of an already-visible buff is not a structural change.");
			LogAssert.IsTrue(GetPrivateField<bool>(controller, "observedBuffsTimingDirty"),
				"The refresh did move the expiry, so it must be recorded as a timing change.");
			LogAssert.IsFalse(ShouldPush(controller),
				"A third of a second of drift is invisible on a duration bar and must not cost a reliable message.");

			// A hundred ticks later the observer's local countdown has drifted more than a second.
			SetPrivateField(controller, "lastReplicateTick", ApplyTick + 100u);
			controller.Apply(aura, new PredictionTick(ApplyTick + 100u));
			LogAssert.IsTrue(ShouldPush(controller),
				"Once the drift exceeds the tolerance the observers' bars are visibly wrong and must be corrected.");

			// A stack, on the other hand, is structural whatever the timing says.
			InvokePrivate(controller, "PushObservedBuffs");
			ProbeBuffTemplate other = MakeTemplate("BuffDrift_Other", duration: 10f, hidden: false);
			controller.Apply(other, new PredictionTick(ApplyTick + 100u));
			LogAssert.IsTrue(GetPrivateField<bool>(controller, "observedBuffsDirty"),
				"A new buff must dirty the observed set structurally.");
			LogAssert.IsTrue(ShouldPush(controller), "A structural change is always pushed.");
		}

		// ── Reconcile snapshot ───────────────────────────────────────────────────────

		/// <summary>
		/// An unchanged tick must hand back the SAME array instance.
		/// </summary>
		/// <remarks>
		/// The delta serialiser holds the previous tick's array and short-circuits on
		/// <c>ReferenceEquals</c>. A fresh array of identical contents defeats that for the whole
		/// reconcile payload, not just for buffs — which is what made continuous re-application
		/// expensive in the first place. Mirrors CooldownReconcileSnapshotTests.
		/// </remarks>
		[Test]
		public void CreateReconcileSnapshot_IsStableAcrossUnchangedTicks_AndFreshAfterAnAdd()
		{
			ProbeBuffTemplate first = MakeTemplate("BuffSnapshot_First", duration: 10f, hidden: false);
			ProbeBuffTemplate second = MakeTemplate("BuffSnapshot_Second", duration: 10f, hidden: false);
			BuffController controller = MakeController("SnapshotController", characterID: 7);

			controller.Apply(first, new PredictionTick(ApplyTick));

			BuffReconcileEntry[] firstSnapshot = controller.CreateReconcileSnapshot();
			BuffReconcileEntry[] secondSnapshot = controller.CreateReconcileSnapshot();

			LogAssert.AreSame(firstSnapshot, secondSnapshot,
				"An unchanged tick must reuse the cached array so the delta serialiser can skip it.");

			controller.Apply(second, new PredictionTick(ApplyTick));
			BuffReconcileEntry[] afterAdd = controller.CreateReconcileSnapshot();

			LogAssert.AreNotSame(secondSnapshot, afterAdd,
				"Adding a buff must allocate a fresh array. Mutating the cached one in place would " +
				"silently update the reference the serialiser diffs against, and the change would be " +
				"encoded as zero bytes.");
			LogAssert.AreEqual(2, afterAdd.Length, "The rebuilt snapshot must contain both buffs.");
			LogAssert.AreSame(afterAdd, controller.CreateReconcileSnapshot(),
				"The rebuilt array must then be stable in its turn.");
		}

		// ── Owner exclusion ──────────────────────────────────────────────────────────

		/// <summary>
		/// The observed-buff broadcast must go through the shared owner-excluding scope.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Asserted at source level: the send needs a spawned, server-started NetworkObject with
		/// observers, which an EditMode test cannot construct. What the source can prove is the two
		/// things that matter — the owner is excluded, and it is excluded through
		/// <c>ObserverBroadcastScope</c> rather than <c>ServerManager.BroadcastExcept</c>, which
		/// mutates the observer set it is handed and would permanently unsubscribe the owner from its
		/// own character. The scope's behaviour is covered by ObserverMessagingTests.
		/// </para>
		/// <para>
		/// The owner was previously a recipient of every buff push, receiving in seconds a lossy copy
		/// of the state it already holds in ticks from the reconcile.
		/// </para>
		/// </remarks>
		[Test]
		public void BroadcastObservedBuffs_ExcludesTheOwner_ThroughTheSharedScope()
		{
			string path = Path.Combine(
				Directory.GetCurrentDirectory(),
				"Assets/Scripts/Shared/Implementation/Entity/Prediction/Buff/BuffController.cs");
			LogAssert.IsTrue(File.Exists(path), $"BuffController.cs not found at {path}.");

			string source = File.ReadAllText(path);

			LogAssert.IsTrue(
				source.Contains("ObserverBroadcastScope.BroadcastToObserversExceptOwner"),
				"BuffController must send the observed buff list through ObserverBroadcastScope so the " +
				"owner is excluded. The owner already has the authoritative reconcile and builds its own " +
				"observed list locally.");
			LogAssert.IsFalse(
				source.Contains("ServerManager.Broadcast(base.NetworkObject"),
				"Broadcasting to the whole observer set includes the owner. Use ObserverBroadcastScope.");
			LogAssert.IsFalse(
				source.Contains("BroadcastExcept("),
				"ServerManager.BroadcastExcept removes the excluded connection from the set it is given; " +
				"handed NetworkObject.Observers it would unsubscribe the owner from its own character " +
				"permanently. ObserverBroadcastScope copies first.");
		}

		// ── Helpers ──────────────────────────────────────────────────────────────────

		/// <summary>Creates a cached probe template and registers it for teardown.</summary>
		private ProbeBuffTemplate MakeTemplate(string name, float duration, bool hidden)
		{
			ProbeBuffTemplate template = ScriptableObject.CreateInstance<ProbeBuffTemplate>();
			template.name = name;
			template.Duration = duration;
			template.TickRate = 1f;
			template.HiddenFromOthers = hidden;
			template.AddToCache(template.name);
			templates.Add(template);
			return template;
		}

		/// <summary>
		/// Creates an unspawned controller with a deterministic tick domain.
		/// </summary>
		/// <remarks>
		/// Unspawned means <c>NetworkObject</c> is null, which the controller reads as "not the
		/// server, not the owner" — the observer's position, and the one these tests care about.
		/// </remarks>
		private BuffController MakeController(string name, long characterID)
		{
			GameObject go = new GameObject(name);
			gameObjects.Add(go);

			BuffController controller = go.AddComponent<BuffController>();
			SetPrivateField(controller, "tickDelta", TickDelta30);
			SetPrivateField(controller, "lastReplicateTick", ApplyTick);
			SetPrivateField(controller, "hasSeenFirstReplicate", true);
			controller.InitializeOnce(new MockCharacter(characterID));
			return controller;
		}

		/// <summary>Builds one observer-shaped payload and feeds it to <paramref name="controller"/>.</summary>
		private static void ReadObservedPayload(BuffController controller, params ObservedBuffEntry[] entries)
		{
			Writer body = new Writer();
			body.WriteUInt8Unpacked(ShapeObserved);
			body.WriteInt32(entries.Length);
			for (int i = 0; i < entries.Length; ++i)
			{
				// The entry's own wire form, shared with CharacterBuffsBroadcast. Hand-writing the
				// fields here is what let this helper drift from the writer it is imitating.
				entries[i].WriteTo(body);
			}

			Reader reader = new Reader(FramePayload(ApplyTick, body).GetArraySegment(), null);
			controller.ReadPayload(null, reader);
			LogAssert.AreEqual(0, reader.Remaining,
				"Every observer payload read must consume its frame exactly.");
		}

		private static ObservedBuffEntry Entry(BaseBuffTemplate template, int stacks, float remaining)
		{
			return new ObservedBuffEntry()
			{
				TemplateID = template.ID,
				Stacks = stacks,
				RemainingSeconds = remaining,
				TotalSeconds = template.Duration,
			};
		}

		private static void WriteSimulationEntry(Writer writer, int templateID, uint expiryTick, uint nextTickTick, int stacks, int tickCount, int cumulative)
		{
			writer.WriteInt32(templateID);
			writer.WriteUInt32(expiryTick);
			writer.WriteUInt32(nextTickTick);
			writer.WriteInt32(stacks);
			writer.WriteInt32(tickCount);
			writer.WriteInt32(cumulative);
		}

		/// <summary>
		/// Wraps <paramref name="body"/> in the reference tick and the unpacked length frame
		/// <c>WritePayload</c> emits.
		/// </summary>
		private static Writer FramePayload(uint referenceTick, Writer body)
		{
			ArraySegment<byte> bodySegment = body.GetArraySegment();

			Writer full = new Writer();
			full.WriteUInt32(referenceTick);
			full.WriteUInt32Unpacked((uint)bodySegment.Count);
			for (int i = 0; i < bodySegment.Count; ++i)
			{
				full.WriteUInt8Unpacked(bodySegment.Array[bodySegment.Offset + i]);
			}
			return full;
		}

		private static bool ShouldPush(BuffController controller)
		{
			return (bool)InvokePrivate(controller, "ShouldPushObservedBuffs");
		}

		private static object InvokePrivate(BuffController controller, string methodName)
		{
			MethodInfo method = typeof(BuffController).GetMethod(methodName, PrivateInstanceFlags);
			LogAssert.IsNotNull(method, $"BuffController.{methodName} not found.");
			return method.Invoke(controller, Array.Empty<object>());
		}

		private static void SetPrivateField(object target, string fieldName, object value)
		{
			FieldInfo field = target.GetType().GetField(fieldName, PrivateInstanceFlags);
			LogAssert.IsNotNull(field, $"Field {fieldName} not found on {target.GetType().Name}.");
			field.SetValue(target, value);
		}

		private static T GetPrivateField<T>(object target, string fieldName)
		{
			FieldInfo field = target.GetType().GetField(fieldName, PrivateInstanceFlags);
			LogAssert.IsNotNull(field, $"Field {fieldName} not found on {target.GetType().Name}.");
			return (T)field.GetValue(target);
		}

		/// <summary>
		/// A buff template that records the FX calls made against it and hands back a real instance
		/// so the controller has something to track and destroy.
		/// </summary>
		private sealed class ProbeBuffTemplate : BaseBuffTemplate
		{
			public int ApplyFXCalls;
			public int RemoveFXCalls;
			public GameObject LastSpawned;
			public GameObject LastRemoved;

			public override void OnApply(Buff buff, ICharacter target) { }
			public override void OnRemove(Buff buff, ICharacter target) { }

			public override GameObject OnApplyFX(Buff buff, ICharacter target)
			{
				++ApplyFXCalls;
				LastSpawned = new GameObject($"{name}_FX");
				return LastSpawned;
			}

			public override void OnRemoveFX(GameObject fxInstance, ICharacter target)
			{
				++RemoveFXCalls;
				LastRemoved = fxInstance;
				if (fxInstance != null)
				{
					UnityEngine.Object.DestroyImmediate(fxInstance);
				}
			}
		}

		/// <summary>Minimal character stand-in; the controller only needs identity and flags.</summary>
		private sealed class MockCharacter : ICharacter
		{
			public MockCharacter(long id) => ID = id;
			public long ID { get; set; }
			public string Name => "MockCharacter";
			public Transform Transform => null;
			public GameObject GameObject => null;
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
			public bool TryGet<T>(out T control) where T : class, ICharacterBehaviour { control = null; return false; }
			public void Invoke(List<Trigger> triggers, EventData eventData) { }
		}
	}
}
