using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using FishNet.Connection;
using FishNet.Managing.Predicting;
using FishNet.Object;
using FishNet.Serializing;
using UnityEngine;
using UnityEngine.SceneManagement;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Guards for the remediations applied to the prediction audit's risk list.
	/// </summary>
	/// <remarks>
	/// Each test pins one behaviour that was previously correct only by convention, by absence
	/// of a caller, or not at all: the buff spawn payload no longer leaks hidden buffs to
	/// non-owners; a charged ability with zero activation time can actually be held; a chain
	/// walks its links from one consistent world; and a position history follows its
	/// character across Unity scenes.
	/// </remarks>
	[TestFixture]
	public class RiskRemediationTests
	{
		private const float TickDelta30 = 1f / 30f;

		// ── Buff spawn payload filtering ─────────────────────────────────────────────

		/// <summary>
		/// A non-owner connection must not receive HiddenFromOthers buffs in the spawn payload.
		/// </summary>
		/// <remarks>
		/// The receiver is simulated with a null connection, which the filter classifies as
		/// "not the owner" — the same answer FishNet's EmptyConnection and any observer get.
		/// The owner path needs a spawned object with a valid owning connection, which an
		/// EditMode test cannot construct; the filtered direction is the security-relevant one.
		/// </remarks>
		[Test]
		public void BuffPayload_OmitsHiddenBuffs_ForNonOwnerConnections()
		{
			GameObject senderObject = new GameObject("BuffPayloadSender");
			GameObject receiverObject = new GameObject("BuffPayloadReceiver");
			PayloadBuffTemplate visible = ScriptableObject.CreateInstance<PayloadBuffTemplate>();
			PayloadBuffTemplate hidden = ScriptableObject.CreateInstance<PayloadBuffTemplate>();

			try
			{
				visible.name = "RiskRemediation_VisibleBuff";
				visible.Duration = 10f;
				visible.HiddenFromOthers = false;
				visible.AddToCache(visible.name);

				hidden.name = "RiskRemediation_HiddenBuff";
				hidden.Duration = 10f;
				hidden.HiddenFromOthers = true;
				hidden.AddToCache(hidden.name);

				BuffController sender = senderObject.AddComponent<BuffController>();
				SetPrivateField(sender, "tickDelta", TickDelta30);
				SetPrivateField(sender, "lastReplicateTick", 100u);
				SetPrivateField(sender, "hasSeenFirstReplicate", true);
				sender.InitializeOnce(new MockCharacter(1));

				sender.Apply(visible, new PredictionTick(100u));
				sender.Apply(hidden, new PredictionTick(100u));
				LogAssert.AreEqual(2, sender.Buffs.Count, "Both buffs must be live on the sender before the payload is written.");

				Writer writer = new Writer();
				sender.WritePayload(null, writer);

				BuffController receiver = receiverObject.AddComponent<BuffController>();
				SetPrivateField(receiver, "tickDelta", TickDelta30);
				SetPrivateField(receiver, "lastReplicateTick", 100u);
				SetPrivateField(receiver, "hasSeenFirstReplicate", true);
				receiver.InitializeOnce(new MockCharacter(2));

				Reader reader = new Reader(writer.GetArraySegment(), null);
				receiver.ReadPayload(null, reader);

				TestContext.WriteLine(
					$"MEASURE buff payload to non-owner: {sender.Buffs.Count} live on server → " +
					$"{receiver.Buffs.Count} on the wire ({writer.Length} B)");

				LogAssert.IsTrue(receiver.Buffs.ContainsKey(visible.ID),
					"The visible buff must survive the payload round trip.");
				LogAssert.IsFalse(receiver.Buffs.ContainsKey(hidden.ID),
					"A HiddenFromOthers buff must never be written into a non-owner's spawn payload.");
				LogAssert.AreEqual(0, reader.Remaining,
					"The framed block must be consumed exactly; a count/entry mismatch would desync every behaviour after this one.");
			}
			finally
			{
				visible.RemoveFromCache();
				hidden.RemoveFromCache();
				Object.DestroyImmediate(visible);
				Object.DestroyImmediate(hidden);
				Object.DestroyImmediate(senderObject);
				Object.DestroyImmediate(receiverObject);
			}
		}

		// ── Charged hold cap ─────────────────────────────────────────────────────────

		/// <summary>
		/// A zero-activation ability must still be holdable for the minimum window.
		/// </summary>
		[Test]
		public void ComputeMaxHoldTicks_ZeroActivation_UsesMinimumWindow()
		{
			uint ticks = AbilityController.ComputeMaxHoldTicks(0f, TickDelta30);
			uint expected = (uint)System.Math.Ceiling(AbilityController.MinimumChargedHoldSeconds / (double)TickDelta30);

			TestContext.WriteLine($"MEASURE hold cap @ ActivationTime 0: {ticks} ticks ({ticks * TickDelta30:F2}s)");

			LogAssert.AreEqual(expected, ticks,
				"ActivationTime 0 used to yield a 0-tick cap, cancelling the charge on its first held tick.");
			LogAssert.IsTrue(ticks > 1u, "The cap must be more than a single tick to be holdable at all.");
		}

		/// <summary>
		/// Above the floor, the cap is exactly twice the activation time.
		/// </summary>
		[Test]
		public void ComputeMaxHoldTicks_LongActivation_IsTwiceActivation()
		{
			uint ticks = AbilityController.ComputeMaxHoldTicks(2f, TickDelta30);
			LogAssert.AreEqual(120u, ticks, "A 2s charge must be holdable for 4s (120 ticks at 30 TPS).");
		}

		/// <summary>
		/// The function is deterministic across repeated calls and never returns zero.
		/// </summary>
		[Test]
		public void ComputeMaxHoldTicks_NeverZero_AndStable()
		{
			LogAssert.AreEqual(1u, AbilityController.ComputeMaxHoldTicks(1f, 0f),
				"A non-positive tick delta must degrade to one tick, not divide by zero or wrap.");
			LogAssert.AreEqual(
				AbilityController.ComputeMaxHoldTicks(0.37f, TickDelta30),
				AbilityController.ComputeMaxHoldTicks(0.37f, TickDelta30),
				"Both peers evaluate this inside the predicted replicate; it must be a pure function.");
		}

		// ── Chain selection ──────────────────────────────────────────────────────────

		/// <summary>
		/// The chain still walks nearest-unselected links after being restructured to
		/// materialise under a single rewind scope.
		/// </summary>
		/// <remarks>
		/// No caster is supplied, so no rewind resolves and the walk runs uncompensated —
		/// which is the base behaviour every compensated walk reduces to.
		/// </remarks>
		[Test]
		public void ChainTargetSelector_WalksNearestLinks_WithinRadius()
		{
			GameObject context = MakeSphere("ChainContext", new Vector3(0f, 0f, 0f));
			GameObject near = MakeSphere("ChainNear", new Vector3(2f, 0f, 0f));
			GameObject next = MakeSphere("ChainNext", new Vector3(4f, 0f, 0f));
			GameObject far = MakeSphere("ChainFar", new Vector3(40f, 0f, 0f));

			try
			{
				Physics.SyncTransforms();

				ChainTargetSelector selector = new ChainTargetSelector
				{
					ChainLength = 3,
					ChainRadius = 3f,
					TargetLayer = ~0,
					MaxHits = 16,
				};

				EventData eventData = new EventData(null);
				eventData.SetTarget(context);

				List<GameObject> chain = selector.SelectTargets(eventData).ToList();

				TestContext.WriteLine("MEASURE chain: " + string.Join(" → ", chain.Select(g => g.name)));

				LogAssert.AreEqual(3, chain.Count, "ChainLength 3 with two reachable links must yield three targets.");
				LogAssert.IsTrue(ReferenceEquals(chain[0], context), "The chain starts at the context.");
				LogAssert.IsTrue(ReferenceEquals(chain[1], near), "The first link is the nearest unselected collider.");
				LogAssert.IsTrue(ReferenceEquals(chain[2], next), "The second link continues from the first, not from the context.");
				LogAssert.IsFalse(chain.Contains(far), "A collider outside ChainRadius of every link is never selected.");
			}
			finally
			{
				Object.DestroyImmediate(context);
				Object.DestroyImmediate(near);
				Object.DestroyImmediate(next);
				Object.DestroyImmediate(far);
			}
		}

		// ── History scene registration ───────────────────────────────────────────────

		/// <summary>
		/// A history follows its GameObject when that object moves to another scene.
		/// </summary>
		[Test]
		public void PositionHistory_FollowsCharacter_AcrossScenes()
		{
			typeof(LagCompensationRegistry)
				.GetMethod("Clear", BindingFlags.Static | BindingFlags.NonPublic)
				.Invoke(null, null);

			Scene original = SceneManager.GetActiveScene();
			// A preview scene: fully isolated, legal in edit mode (SceneManager.CreateScene is not),
			// and it never touches the scene the test runner is standing in.
			Scene other = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
			GameObject go = new GameObject("HistoryMover");

			try
			{
				CharacterPositionHistory history = go.AddComponent<CharacterPositionHistory>();

				history.EnsureSceneRegistration();
				LogAssert.AreEqual(1, LagCompensationRegistry.RegisteredIn(original), "First call registers under the object's current scene.");
				LogAssert.AreEqual(0, LagCompensationRegistry.RegisteredIn(other), "Nothing is registered in a scene the object has never been in.");

				history.EnsureSceneRegistration();
				LogAssert.AreEqual(1, LagCompensationRegistry.RegisteredIn(original), "Repeated calls in the same scene are idempotent.");

				SceneManager.MoveGameObjectToScene(go, other);
				history.EnsureSceneRegistration();

				LogAssert.AreEqual(0, LagCompensationRegistry.RegisteredIn(original),
					"After moving scenes the history must leave its old bucket, or rewinds there would displace a character that is no longer present.");
				LogAssert.AreEqual(1, LagCompensationRegistry.RegisteredIn(other),
					"After moving scenes the history must be rewindable in the new scene's queries.");
			}
			finally
			{
				Object.DestroyImmediate(go);
				typeof(LagCompensationRegistry)
					.GetMethod("Clear", BindingFlags.Static | BindingFlags.NonPublic)
					.Invoke(null, null);
				UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(other);
			}
		}

		// ── Helpers ──────────────────────────────────────────────────────────────────

		private static GameObject MakeSphere(string name, Vector3 position)
		{
			GameObject go = new GameObject(name);
			go.transform.position = position;
			SphereCollider collider = go.AddComponent<SphereCollider>();
			collider.radius = 0.5f;
			return go;
		}

		private static void SetPrivateField<T>(object instance, string fieldName, T value)
		{
			FieldInfo field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
			Assert.IsNotNull(field, $"Private field '{fieldName}' not found on {instance.GetType().Name}.");
			field.SetValue(instance, value);
		}

		/// <summary>Minimal buff template with the production fields the payload path reads.</summary>
		private sealed class PayloadBuffTemplate : BaseBuffTemplate
		{
			public override void OnApply(Buff buff, ICharacter target) { }
			public override void OnRemove(Buff buff, ICharacter target) { }
		}

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
