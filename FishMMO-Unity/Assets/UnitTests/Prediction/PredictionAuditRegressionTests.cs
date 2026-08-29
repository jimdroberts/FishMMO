using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using FishNet.Connection;
using FishNet.Managing.Predicting;
using FishNet.Object;
using FishNet.Object.Prediction;
using FishNet.Serializing;
using KinematicCharacterController;
using NUnit.Framework;
using UnityEngine;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Regression guards for the defects found by the 2026-08-28 prediction-pipeline audit. Each
	/// test names the defect it pins and failed against the code as it was before the fix.
	/// </summary>
	[TestFixture]
	public class PredictionAuditRegressionTests
	{
		private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

		[OneTimeSetUp]
		public void RegisterProductionSerializers()
		{
			Type[] serializerTypes =
			{
				typeof(CharacterReconcileDataDeltaSerializer),
				typeof(CharacterReplicateDataDeltaSerializer),
				typeof(CharacterTransientGroundingReportDeltaSerializer),
				typeof(KinematicCharacterMotorStateDeltaSerializer),
				typeof(CharacterAttributeResourceStateSerializer),
			};
			foreach (Type serializerType in serializerTypes)
			{
				MethodInfo register = serializerType.GetMethod("RegisterSerializers", Any);
				LogAssert.IsNotNull(register, $"{serializerType.Name} must expose a RegisterSerializers hook.");
				register.Invoke(null, null);
			}
		}

		[TearDown]
		public void ClearGuard()
		{
			ReconcileDeltaGuard.ConsumeRejection();
		}

		private static CharacterReconcileData BaseReconcile()
		{
			CharacterReconcileData data = default;
			data.MotorState.Rotation = Quaternion.identity;
			data.MotorState.Position = new Vector3(10f, 2f, 10f);
			data.MotorState.BaseVelocity = new Vector3(1f, 0f, 0f);
			data.MotorState.TimeSinceJumpRequested = float.MaxValue;
			data.ResourceState.Health = 100;
			data.Sequence = 5;
			return data;
		}

		// ── 1. FishNet WriteDeltaVector3 leaked its Skip(1) placeholder into Writer.Length ──

		/// <summary>
		/// A walking tick whose only change is the motor position: every Vector3 delta after
		/// <c>Position</c> is unchanged, and the last of them used to leave a stale byte between
		/// <c>Position</c> and <c>Length</c>. The RPC is copied up to <c>Length</c>, so the client
		/// read one byte of garbage after the reconcile and mis-parsed the next packet id.
		/// </summary>
		[Test]
		public void UnchangedVector3Delta_LeavesNoTrailingByteInTheWriter()
		{
			CharacterReconcileData prev = BaseReconcile();
			CharacterReconcileData next = prev;
			next.MotorState.Position += new Vector3(0.1f, 0f, 0f);
			next.Sequence = unchecked((byte)(prev.Sequence + 1));

			Writer writer = new Writer();
			LogAssert.IsTrue(writer.WriteDelta(prev, next, DeltaSerializerOption.RootSerialize),
				"A changed position must produce a delta.");
			LogAssert.AreEqual(writer.Position, writer.Length,
				"The Skip(1) placeholder of an unchanged Vector3 delta shipped as a trailing byte.");

			Reader reader = new Reader(writer.GetArraySegment(), null);
			CharacterReconcileData decoded = reader.ReadDelta(prev);
			LogAssert.AreEqual(0, reader.Remaining, "The reconcile payload must be consumed exactly.");
			LogAssert.IsTrue((decoded.MotorState.Position - next.MotorState.Position).magnitude < 0.01f,
				"The decoded position must match.");
		}

		/// <summary>The primitive itself, isolated from the project serializers.</summary>
		[Test]
		public void FishNet_WriteDeltaVector3_RewindsLengthWhenNothingChanged()
		{
			Writer writer = new Writer();
			writer.WriteUInt8Unpacked(7);
			int lengthBefore = writer.Length;
			Vector3 v = new Vector3(1f, 2f, 3f);
			LogAssert.IsFalse(writer.WriteDeltaVector3(v, v), "No change, no bytes.");
			LogAssert.AreEqual(lengthBefore, writer.Length, "WriteDeltaVector3 must rewind Length with Position.");
			LogAssert.IsFalse(writer.WriteDeltaVector2(Vector2.one, Vector2.one));
			LogAssert.AreEqual(lengthBefore, writer.Length, "WriteDeltaVector2 must rewind Length with Position.");
		}

		// ── 2. The chain sequence counted reconciles CREATED, not SENT ──

		/// <summary>
		/// Models the FishMMO edit in <c>Server_SendReconcileRpc</c>: the sequence is stamped on
		/// the reconcile at the moment it is written. Ticks whose send is skipped (no resends left,
		/// which happens after any &gt;RedundancyCount-tick input gap and at every spawn) must not
		/// advance it, or the reader rejects the next delta as a lost datagram and the owner goes
		/// uncorrected until the next whole-second snapshot.
		/// </summary>
		[Test]
		public void SendTimeSequence_SurvivesSkippedSends()
		{
			Func<CharacterReconcileData, byte, CharacterReconcileData> stamp =
				ReconcileSequenceStamper<CharacterReconcileData>.Stamp;
			LogAssert.IsNotNull(stamp, "RegisterSerializers must install the send-time sequence stamper.");

			CharacterReconcileData serverBaseline = default;
			CharacterReconcileData clientBaseline = default;
			CharacterReconcileData authoritative = BaseReconcile();
			byte sendSequence = 0;

			ArraySegment<byte> Send(CharacterReconcileData data, DeltaSerializerOption option)
			{
				sendSequence = unchecked((byte)(sendSequence + 1));
				data = stamp(data, sendSequence);
				Writer w = new Writer();
				w.WriteDelta(serverBaseline, data, option);
				serverBaseline = data;
				return w.GetArraySegment();
			}

			CharacterReconcileData Receive(ArraySegment<byte> payload)
			{
				Reader r = new Reader(payload, null);
				CharacterReconcileData d = r.ReadDelta(clientBaseline);
				LogAssert.IsFalse(ReconcileDeltaGuard.ConsumeRejection(), "The delta must not be rejected.");
				LogAssert.AreEqual(0, r.Remaining, "Exact consumption.");
				clientBaseline = d;
				return d;
			}

			// Spawn: five reconciles are created before the owner's first input arrives, none sent.
			for (int i = 0; i < 5; i++)
			{
				authoritative.MotorState.Position += Vector3.forward;
			}
			// First delta ever sent (RootSerialize: the spawn-tick FullSerialize was itself skipped).
			Receive(Send(authoritative, DeltaSerializerOption.RootSerialize));
			LogAssert.AreEqual(1, (int)clientBaseline.Sequence, "First sent reconcile carries sequence 1.");

			// Steady state, then a 6-tick input gap during which nothing is sent.
			for (int i = 0; i < 3; i++)
			{
				authoritative.MotorState.Position += Vector3.forward;
				Receive(Send(authoritative, DeltaSerializerOption.RootSerialize));
			}
			for (int i = 0; i < 6; i++)
			{
				authoritative.MotorState.Position += Vector3.forward;
			}
			CharacterReconcileData resumed = Receive(Send(authoritative, DeltaSerializerOption.RootSerialize));
			LogAssert.IsTrue((resumed.MotorState.Position - authoritative.MotorState.Position).magnitude < 0.05f,
				"After a send gap the next delta must decode against the baseline both sides still hold.");
		}

		/// <summary>
		/// The negative control: numbering at creation (what the producer used to do) makes the
		/// reader reject the first delta after a skipped send even though both baselines agree.
		/// </summary>
		[Test]
		public void CreationTimeSequence_IsRejectedAfterASkippedSend()
		{
			CharacterReconcileData serverBaseline = default;
			CharacterReconcileData clientBaseline = default;
			CharacterReconcileData authoritative = BaseReconcile();
			byte created = 0;

			for (int i = 0; i < 3; i++)
			{
				authoritative.MotorState.Position += Vector3.forward;
				authoritative.Sequence = unchecked((byte)(++created));
				Writer w = new Writer();
				w.WriteDelta(serverBaseline, authoritative, DeltaSerializerOption.RootSerialize);
				serverBaseline = authoritative;
				clientBaseline = new Reader(w.GetArraySegment(), null).ReadDelta(clientBaseline);
				LogAssert.IsFalse(ReconcileDeltaGuard.ConsumeRejection());
			}

			// One created-but-unsent reconcile.
			authoritative.MotorState.Position += Vector3.forward;
			authoritative.Sequence = unchecked((byte)(++created));

			authoritative.MotorState.Position += Vector3.forward;
			authoritative.Sequence = unchecked((byte)(++created));
			Writer w2 = new Writer();
			w2.WriteDelta(serverBaseline, authoritative, DeltaSerializerOption.RootSerialize);
			new Reader(w2.GetArraySegment(), null).ReadDelta(clientBaseline);
			LogAssert.IsTrue(ReconcileDeltaGuard.ConsumeRejection(),
				"Creation-time numbering reads a skipped send as a lost datagram — the defect the send-time stamp removes.");
		}

		/// <summary>The producer no longer numbers reconciles; FishNet does at send time.</summary>
		[Test]
		public void Producer_DoesNotStampTheSequence_AndTheSendPathDoes()
		{
			LogAssert.IsNull(typeof(CharacterPredictionController).GetField("reconcileSequence", Any),
				"CharacterPredictionController must not keep its own reconcile sequence counter.");

			string path = Path.Combine(Application.dataPath,
				"Plugins/FishNet/Runtime/Object/NetworkBehaviour/NetworkBehaviour.Prediction.cs");
			string source = File.ReadAllText(path);
			int stamp = source.IndexOf("ReconcileSequenceStamper<T>.Stamp(reconcileData", StringComparison.Ordinal);
			int write = source.IndexOf("methodWriter.WriteDeltaReconcile(lastReconcileData, reconcileData", StringComparison.Ordinal);
			LogAssert.IsTrue(stamp >= 0 && write > stamp,
				"Server_SendReconcileRpc must stamp the sequence immediately before writing the delta (FISHMMO EDIT).");
		}

		// ── 3. TargetOrdering used the per-process instance id as a cross-peer sort key ──

		[Test]
		public void SameNamedSceneObjects_OrderByAuthoredPosition_NotByCreationOrder()
		{
			int[] firstRun = RankTwoBraziers(createNearFirst: false);
			int[] secondRun = RankTwoBraziers(createNearFirst: true);
			LogAssert.AreEqual(1, firstRun[0], "The brazier at x=1 must sort first regardless of which was created first.");
			LogAssert.AreEqual(1, secondRun[0], "The brazier at x=1 must sort first regardless of which was created first.");
		}

		/// <summary>Returns the x positions of two same-named objects in sorted order.</summary>
		private static int[] RankTwoBraziers(bool createNearFirst)
		{
			GameObject a = null, b = null;
			try
			{
				if (createNearFirst)
				{
					a = new GameObject("Brazier"); a.transform.position = new Vector3(1f, 0f, 0f);
					b = new GameObject("Brazier"); b.transform.position = new Vector3(2f, 0f, 0f);
				}
				else
				{
					b = new GameObject("Brazier"); b.transform.position = new Vector3(2f, 0f, 0f);
					a = new GameObject("Brazier"); a.transform.position = new Vector3(1f, 0f, 0f);
				}
				List<GameObject> candidates = new List<GameObject> { b, a };
				List<TargetRank> ranks = new List<TargetRank>
				{
					TargetOrdering.Rank(0, b, 0f),
					TargetOrdering.Rank(1, a, 0f),
				};
				TargetOrdering.SortStable(ranks);
				return new[]
				{
					Mathf.RoundToInt(candidates[ranks[0].Index].transform.position.x),
					Mathf.RoundToInt(candidates[ranks[1].Index].transform.position.x),
				};
			}
			finally
			{
				if (a != null) UnityEngine.Object.DestroyImmediate(a);
				if (b != null) UnityEngine.Object.DestroyImmediate(b);
			}
		}

		// ── 4. Area/Cone/Random capped by identity, not distance; Chain truncated in the broadphase ──

		[Test]
		public void AreaSelector_MaxHits_KeepsTheNearestCandidates()
		{
			GameObject context = MakeSphere("AreaContext", Vector3.zero);
			GameObject near = MakeSphere("AreaNear", new Vector3(2f, 0f, 0f));
			GameObject mid = MakeSphere("AreaMid", new Vector3(4f, 0f, 0f));
			GameObject far = MakeSphere("AreaFar", new Vector3(6f, 0f, 0f));
			try
			{
				Physics.SyncTransforms();
				AreaTargetSelector selector = new AreaTargetSelector { Radius = 10f, TargetLayer = ~0, MaxHits = 2 };
				EventData eventData = new EventData(null);
				eventData.SetTarget(context);

				List<GameObject> picked = new List<GameObject>(selector.SelectTargets(eventData));
				// The context's own collider is a candidate too (documented behaviour); the cap of 2
				// must therefore keep the context (d=0) and the nearest other collider.
				LogAssert.AreEqual(2, picked.Count, "MaxHits caps the result.");
				LogAssert.IsTrue(picked.Contains(near), "The nearest candidate survives the cap.");
				LogAssert.IsFalse(picked.Contains(far), "The furthest candidate is the first one dropped.");
				LogAssert.IsFalse(picked.Contains(mid), "Only the two nearest survive.");
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(context);
				UnityEngine.Object.DestroyImmediate(near);
				UnityEngine.Object.DestroyImmediate(mid);
				UnityEngine.Object.DestroyImmediate(far);
			}
		}

		[Test]
		public void ChainSelector_QueriesWiderThanMaxHits_AndTargetsEverythingByDefault()
		{
			ChainTargetSelector selector = new ChainTargetSelector { MaxHits = 4 };
			LogAssert.AreEqual(~0, selector.TargetLayer.value,
				"A fresh chain selector must target every layer like the other physics selectors, not Nothing.");

			MethodInfo ensure = typeof(ChainTargetSelector).GetMethod("EnsureHitBuffer", Any);
			LogAssert.IsNotNull(ensure);
			ensure.Invoke(selector, null);
			Collider[] hits = (Collider[])typeof(ChainTargetSelector).GetField("hits", Any).GetValue(selector);
			MethodInfo size = typeof(TargetSelector).GetMethod("QueryBufferSize", Any);
			int expected = (int)size.Invoke(null, new object[] { 4 });
			LogAssert.AreEqual(expected, hits.Length,
				"The chain's query buffer must be the wide one, so the broadphase cannot truncate before ranking.");
			LogAssert.IsTrue(hits.Length > 4, "Wider than MaxHits.");
		}

		private static GameObject MakeSphere(string name, Vector3 position)
		{
			GameObject go = new GameObject(name);
			go.transform.position = position;
			SphereCollider c = go.AddComponent<SphereCollider>();
			c.radius = 0.5f;
			return go;
		}

		// ── 5. The moving platform never moved on clients ──

		/// <summary>
		/// The client tick calls <see cref="KCCPlatform.Step"/> directly because FishNet never
		/// invokes an ownerless, non-forwarded replicate body on a client. The step itself must be
		/// the deterministic movement the server runs: advance, snap on arrival, wrap the goal.
		/// </summary>
		[Test]
		public void Platform_ClientStep_MovesTowardGoals_SnapsAndWraps()
		{
			GameObject go = new GameObject("Platform");
			try
			{
				go.transform.position = new Vector3(0f, 5f, 0f);
				KCCPlatform platform = go.AddComponent<KCCPlatform>();
				typeof(KCCPlatform).GetField("moveRate", Any).SetValue(platform, 4f);
				typeof(KCCPlatform).GetMethod("Awake", Any).Invoke(platform, null);

				float delta = 1f / 30f;
				platform.Step(delta);
				LogAssert.IsTrue(Mathf.Abs(go.transform.position.z - 4f * delta) < 1e-4f, "One tick moves moveRate × delta toward the first goal (+5 z).");
				LogAssert.IsTrue((platform.LastCompletedTickVelocity - new Vector3(0f, 0f, 4f)).magnitude < 1e-3f, "Velocity is exposed for riders.");

				FieldInfo goalField = typeof(KCCPlatform).GetField("goalIndex", Any);
				int steps = 0;
				while ((byte)goalField.GetValue(platform) == 0 && steps < 100) { platform.Step(delta); steps++; }
				LogAssert.AreEqual(1, (int)(byte)goalField.GetValue(platform), "Arrival advances the goal.");
				LogAssert.IsTrue(Mathf.Abs(go.transform.position.z - 5f) < 1e-4f, "Arrival snaps exactly onto the goal.");
				LogAssert.IsTrue(steps > 30 && steps < 45, $"5 units at 4 u/s over 30 Hz takes ~38 ticks, took {steps}.");

				steps = 0;
				while ((byte)goalField.GetValue(platform) == 1 && steps < 200) { platform.Step(delta); steps++; }
				LogAssert.AreEqual(0, (int)(byte)goalField.GetValue(platform), "The last goal wraps to the first.");
				LogAssert.IsTrue(Mathf.Abs(go.transform.position.z + 5f) < 1e-4f, "Snapped onto the second goal (-5 z).");
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(go);
			}
		}

		[Test]
		public void Platform_ClientTick_DoesNotRelyOnTheReplicate()
		{
			string path = Path.Combine(Application.dataPath, "Scripts/Shared/Implementation/Entity/Prediction/KCC/KCCPlatform.cs");
			string source = File.ReadAllText(path);
			int onTick = source.IndexOf("protected override void TimeManager_OnTick()", StringComparison.Ordinal);
			int body = source.IndexOf("Step((float)TimeManager.TickDelta);", onTick, StringComparison.Ordinal);
			LogAssert.IsTrue(onTick >= 0 && body > onTick,
				"A client must step the platform directly from TimeManager_OnTick; FishNet's Replicate_NonAuthoritative returns before invoking the replicate body when forwarding is off.");
		}

		// ── 6. NPC.ReadPayload overwrote the modifiers the attribute payload just delivered ──

		[Test]
		public void NpcAttributeGeneration_OnTheClient_DoesNotReplaceTheServersModifier()
		{
			GameObject go = new GameObject("NPCProbe");
			List<UnityEngine.Object> assets = new List<UnityEngine.Object>();
			try
			{
				// NPC's RequireComponent chain adds NetworkObject twice on a bare GameObject; FishNet
				// logs an error about it that is irrelevant to the attribute path under test.
				UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
				NPC npc = go.AddComponent<NPC>();
				UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;
				CharacterAttributeController attributes = go.GetComponent<CharacterAttributeController>();
				attributes.InitializeOnce(npc);
				npc.RegisterCharacterBehaviour(attributes);

				CharacterAttributeTemplate template = ScriptableObject.CreateInstance<CharacterAttributeTemplate>();
				template.name = "Audit_NPC_Strength"; template.InitialValue = 10; template.AddToCache(template.name); assets.Add(template);
				CharacterAttribute attribute = new CharacterAttribute(attributes, template.ID, 10, 0);
				attributes.AddAttribute(attribute);
				// What the server's attribute payload delivered: bonus + instance difficulty + buffs.
				attribute.SetModifier(50);

				NPCAttributeDatabase bonuses = ScriptableObject.CreateInstance<NPCAttributeDatabase>();
				assets.Add(bonuses);
				bonuses.Attributes.Add(new NPCAttribute(false, false, 25, 25, template));
				npc.AttributeBonuses = bonuses;
				typeof(NPC).GetField("npcRNG", Any).SetValue(npc, new DeterministicRNG(1));

				typeof(NPC).GetMethod("AddNPCAttributes", Any).Invoke(npc, new object[] { false });

				LogAssert.AreEqual(50, attribute.ExternalModifier,
					"A client must not replace the payload-delivered modifier with the locally generated NPC bonus.");
			}
			finally
			{
				foreach (UnityEngine.Object asset in assets) UnityEngine.Object.DestroyImmediate(asset);
				UnityEngine.Object.DestroyImmediate(go);
			}
		}

		// ── 7. Replaying a cast did not restore its cooldown after a pre-cast reconcile wiped it ──

		private sealed class ProbeTemplate : AbilityTemplate { }

		[Test]
		public void ReplayedCast_ReappliesTheCooldown()
		{
			GameObject go = new GameObject("CastReplayProbe");
			List<UnityEngine.Object> assets = new List<UnityEngine.Object>();
			try
			{
				MockCharacter character = new MockCharacter();
				CooldownController cooldowns = go.AddComponent<CooldownController>();
				cooldowns.InitializeOnce(character);
				typeof(CooldownController).GetField("cachedTickDelta", Any).SetValue(cooldowns, 1f / 30f);

				AbilityController controller = go.AddComponent<AbilityController>();
				controller.OnAwake();
				typeof(AbilityController).GetField("abilitySeedGenerator", Any).SetValue(controller, new DeterministicRNG(1));
				controller.InitializeOnce(character);
				typeof(AbilityController).GetField("cachedCooldownController", Any).SetValue(controller, cooldowns);

				ProbeTemplate template = ScriptableObject.CreateInstance<ProbeTemplate>();
				template.name = "Audit_Replay_Ability"; template.Cooldown = 5f; template.AddToCache(template.name); assets.Add(template);
				Ability ability = new Ability(9002L, template);

				// The reconcile for a tick before the cast restored "no cooldowns".
				cooldowns.RestoreFromReconcile(null);

				MethodInfo finish = typeof(AbilityController).GetMethod("FinishAbility", Any);
				LogAssert.IsNotNull(finish);
				AbilityActivationReplicateData activation = default;
				activation.SetTick(100);
				finish.Invoke(controller, new object[] { ability, activation, ReplicateState.Replayed | ReplicateState.Ticked | ReplicateState.Created });

				object table = typeof(CooldownController).GetField("cooldowns", Any).GetValue(cooldowns);
				bool hasCooldown = (bool)table.GetType().GetMethod("ContainsKey").Invoke(table, new object[] { ability.ID });
				LogAssert.IsTrue(hasCooldown,
					"Replaying the cast tick after a pre-cast reconcile must put the predicted cooldown back.");
			}
			finally
			{
				foreach (UnityEngine.Object asset in assets) UnityEngine.Object.DestroyImmediate(asset);
				UnityEngine.Object.DestroyImmediate(go);
			}
		}

		// ── 8. Equipment payload threw on an item template that failed to resolve ──

		/// <summary>
		/// Item templates are immutable assets registered before anything spawns, so an id with no
		/// template means the payload is corrupt — but the reader used to hand it straight to
		/// <c>new Item(...)</c>, whose constructor dereferences <c>Template</c>. The throw escaped
		/// <c>ReadPayload</c>, past the frame that exists to keep the stream aligned, taking every
		/// later behaviour on the object and the rest of the spawn packet with it.
		/// </summary>
		[Test]
		public void EquipmentPayload_WithAnUnresolvableTemplate_DoesNotThrow_AndStaysFramed()
		{
			GameObject go = new GameObject("EquipmentPayloadProbe");
			try
			{
				EquipmentController equipment = go.AddComponent<EquipmentController>();
				equipment.InitializeOnce(new MockCharacter());

				// Hand-built observer-shape block carrying one item whose template cannot resolve,
				// then a sentinel that the next behaviour would read.
				Writer writer = new Writer();
				writer.Skip(4);
				int blockStart = writer.Position;
				writer.WriteUInt8Unpacked(0);          // observer shape
				writer.WriteInt32(1);                  // one item
				writer.WriteInt32(int.MaxValue);       // template id that resolves to null
				writer.WriteUInt8Unpacked(3);          // slot
				writer.WriteInt32(1234);               // seed
				writer.InsertUInt32Unpacked((uint)(writer.Position - blockStart), blockStart - 4);
				const int sentinel = 0x5A5A5A;
				writer.WriteInt32(sentinel);

				Reader reader = new Reader(writer.GetArraySegment(), null);
				UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
				try
				{
					Assert.DoesNotThrow(() => equipment.ReadPayload(null, reader),
						"An unresolvable item template must not throw out of a spawn payload read.");
				}
				finally
				{
					UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;
				}

				LogAssert.AreEqual(sentinel, reader.ReadInt32(),
					"The behaviour after equipment must still read its own bytes.");
				LogAssert.AreEqual(0, reader.Remaining, "The block must be consumed exactly.");
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(go);
			}
		}

		// ── 9. Buff and equipment restores doubled the attribute modifier the payload carried ──

		/// <summary>
		/// The payload's <c>ExternalModifier</c> is the server's total and already contains every
		/// buff and item contribution; the buff and equipment blocks are read afterwards and add
		/// theirs again. The owner's reconcile hid it within a tick — an observer, which receives no
		/// reconcile, kept the doubled maximum until the server next pushed that attribute.
		/// </summary>
		[Test]
		public void PayloadModifier_SurvivesTheBuffAndEquipmentRestores()
		{
			GameObject writerGo = new GameObject("AttrWriter");
			GameObject readerGo = new GameObject("AttrReader");
			List<UnityEngine.Object> assets = new List<UnityEngine.Object>();
			try
			{
				CharacterAttributeTemplate template = ScriptableObject.CreateInstance<CharacterAttributeTemplate>();
				template.name = "Audit_Doubling_Health"; template.InitialValue = 100;
				template.AddToCache(template.name); assets.Add(template);

				CharacterAttributeController source = writerGo.AddComponent<CharacterAttributeController>();
				source.InitializeOnce(new MockCharacter());
				CharacterAttribute sourceAttribute = new CharacterAttribute(source, template.ID, 100, 0);
				source.AddAttribute(sourceAttribute);
				// The server's total: base 100 plus 25 of equipment or buff.
				sourceAttribute.SetModifier(25);

				Writer payload = new Writer();
				source.WritePayload(null, payload);

				CharacterAttributeController target = readerGo.AddComponent<CharacterAttributeController>();
				target.InitializeOnce(new MockCharacter());
				CharacterAttribute targetAttribute = new CharacterAttribute(target, template.ID, 100, 0);
				target.AddAttribute(targetAttribute);

				target.ReadPayload(null, new Reader(payload.GetArraySegment(), null));
				LogAssert.AreEqual(25, targetAttribute.ExternalModifier, "The payload carries the server's total.");

				// What BuffController.ReadPayload and EquipmentController.ReadPayload do next.
				targetAttribute.AddModifier(25);
				LogAssert.AreEqual(50, targetAttribute.ExternalModifier, "The restores add their contribution on top.");

				MethodInfo reassert = typeof(CharacterAttributeController).GetMethod("ReassertPayloadModifiers", Any);
				LogAssert.IsNotNull(reassert, "CharacterAttributeController must re-assert the payload's modifiers.");
				reassert.Invoke(target, null);

				LogAssert.AreEqual(25, targetAttribute.ExternalModifier,
					"Once every behaviour has read, the payload's total must be the one that stands.");
			}
			finally
			{
				foreach (UnityEngine.Object asset in assets) UnityEngine.Object.DestroyImmediate(asset);
				UnityEngine.Object.DestroyImmediate(writerGo);
				UnityEngine.Object.DestroyImmediate(readerGo);
			}
		}

		[Test]
		public void PayloadModifierReassert_RunsAfterEveryBehaviourHasRead()
		{
			string path = Path.Combine(Application.dataPath,
				"Scripts/Shared/Implementation/Entity/Prediction/CharacterAttribute/CharacterAttributeController.cs");
			string source = File.ReadAllText(path);
			int start = source.IndexOf("public override void OnStartNetwork()", StringComparison.Ordinal);
			LogAssert.IsTrue(start >= 0, "CharacterAttributeController must override OnStartNetwork.");
			int call = source.IndexOf("ReassertPayloadModifiers();", start, StringComparison.Ordinal);
			LogAssert.IsTrue(call > start && call - start < 800,
				"OnStartNetwork must re-assert the payload modifiers; FishNet initialises objects in a later loop than the one that reads their payloads, so it is the first hook that runs after every behaviour on the object has read.");
		}

		// ── 10. NPC factions: derived from the race template, not sent; pet rosters were discarded ──

		private static RaceTemplate MakeRace(string name, List<UnityEngine.Object> assets, out FactionTemplate alliedFaction, out FactionTemplate hostileFaction)
		{
			alliedFaction = ScriptableObject.CreateInstance<FactionTemplate>();
			alliedFaction.name = name + "_Allied"; alliedFaction.AddToCache(alliedFaction.name); assets.Add(alliedFaction);

			hostileFaction = ScriptableObject.CreateInstance<FactionTemplate>();
			hostileFaction.name = name + "_Hostile"; hostileFaction.AddToCache(hostileFaction.name); assets.Add(hostileFaction);

			FactionTemplate initial = ScriptableObject.CreateInstance<FactionTemplate>();
			initial.name = name + "_Initial";
			initial.DefaultAllied = new FactionTemplate.FactionHashSet { alliedFaction };
			initial.DefaultNeutral = new FactionTemplate.FactionHashSet();
			initial.DefaultHostile = new FactionTemplate.FactionHashSet { hostileFaction };
			initial.AddToCache(initial.name); assets.Add(initial);

			RaceTemplate race = ScriptableObject.CreateInstance<RaceTemplate>();
			race.name = name + "_Race"; race.InitialFaction = initial;
			race.AddToCache(race.name); assets.Add(race);
			return race;
		}

		/// <summary>
		/// An NPC's standings are a pure function of immutable template data, so the payload carries
		/// the race — which a spawner may have overridden server-side — and the receiver rebuilds the
		/// roster itself instead of being told it.
		/// </summary>
		[Test]
		public void NpcFactions_AreRebuiltFromTheRace_AndTheRaceOverrideTravels()
		{
			GameObject writerGo = new GameObject("FactionWriter");
			GameObject readerGo = new GameObject("FactionReader");
			List<UnityEngine.Object> assets = new List<UnityEngine.Object>();
			try
			{
				RaceTemplate race = MakeRace("Audit_Npc", assets, out FactionTemplate allied, out FactionTemplate hostile);

				FactionController source = writerGo.AddComponent<FactionController>();
				source.InitializeOnce(new MockCharacter());
				source.SetRaceTemplateOnSpawn(race);   // what NPCSpawnableSettings.FactionOverride does
				source.InitializeTemplateFactions();

				LogAssert.IsTrue(source.FactionsAreTemplateDerived, "An NPC roster is derived.");
				LogAssert.IsTrue(source.Hostile.ContainsKey(hostile.ID), "Hostile defaults are loaded at runtime.");
				LogAssert.IsTrue(source.Allied.ContainsKey(allied.ID), "Allied defaults are loaded at runtime.");

				Writer payload = new Writer();
				source.WritePayload(null, payload);

				// The reader has never heard of this race: no serialized template, empty roster.
				FactionController target = readerGo.AddComponent<FactionController>();
				target.InitializeOnce(new MockCharacter());
				LogAssert.AreEqual(0, target.Factions.Count, "The reader starts with nothing.");

				Reader reader = new Reader(payload.GetArraySegment(), null);
				target.ReadPayload(null, reader);
				LogAssert.AreEqual(0, reader.Remaining, "The faction block must be consumed exactly.");

				LogAssert.IsTrue(target.FactionsAreTemplateDerived, "The receiver rebuilds rather than being told.");
				LogAssert.AreEqual(race.ID, target.RaceTemplate.ID,
					"The spawner's race override must reach the client, or it judges the NPC by the prefab's race.");
				LogAssert.AreEqual(source.Factions.Count, target.Factions.Count, "Both peers derive the same roster.");
				LogAssert.IsTrue(target.Hostile.ContainsKey(hostile.ID), "...including which factions are hostile.");

				/* And the roster itself is not on the wire: an owned roster of the same two entries
				 * costs more than a derived one, which carries only the race and a shape flag. */
				FactionController owned = readerGo.AddComponent<FactionController>();
				owned.InitializeOnce(new MockCharacter());
				owned.SetFaction(allied.ID, FactionTemplate.Maximum);
				owned.SetFaction(hostile.ID, FactionTemplate.Minimum);
				Writer ownedPayload = new Writer();
				owned.WritePayload(null, ownedPayload);
				LogAssert.IsTrue(ownedPayload.Position > payload.Position,
					$"A derived roster must not put its entries on the wire (derived {payload.Position} B, owned {ownedPayload.Position} B).");
			}
			finally
			{
				foreach (UnityEngine.Object asset in assets) UnityEngine.Object.DestroyImmediate(asset);
				UnityEngine.Object.DestroyImmediate(writerGo);
				UnityEngine.Object.DestroyImmediate(readerGo);
			}
		}

		/// <summary>
		/// A pet's roster is copied from its owner and cannot be derived from any template, so it
		/// travels — and must survive arrival. <c>ReadPayload</c> used to install it through
		/// <c>SetFaction</c>, which refuses to move an NPC's standing, so every entry was dropped on
		/// the floor and a summoned pet reached its owner's client with an empty roster no matter
		/// what the server sent. A pet is an NPC, so the guard always fired.
		/// </summary>
		[Test]
		public void PetFactionRoster_CopiedFromItsOwner_SurvivesArrivalOnAnNpc()
		{
			GameObject ownerGo = new GameObject("FactionOwner");
			GameObject serverPetGo = new GameObject("FactionPetServer");
			GameObject clientPetGo = new GameObject("FactionPetClient");
			List<UnityEngine.Object> assets = new List<UnityEngine.Object>();
			try
			{
				RaceTemplate race = MakeRace("Audit_Pet", assets, out FactionTemplate allied, out FactionTemplate hostile);

				FactionController ownerFactions = ownerGo.AddComponent<FactionController>();
				ownerFactions.InitializeOnce(new MockCharacter());
				ownerFactions.SetFaction(allied.ID, FactionTemplate.Maximum);
				ownerFactions.SetFaction(hostile.ID, FactionTemplate.Minimum);

				// Both ends are real pets: a Pet is an NPC, which is why the old guard always fired.
				UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
				Pet serverPet = serverPetGo.AddComponent<Pet>();
				Pet clientPet = clientPetGo.AddComponent<Pet>();
				UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;

				FactionController serverFactions = serverPetGo.GetComponent<FactionController>();
				// Not LogAssert.IsNotNull: that helper stringifies the object, and FishNet's
				// NetworkBehaviour.ToString() dereferences network state an unspawned behaviour
				// does not have.
				LogAssert.IsTrue(serverFactions != null, "Pet requires a FactionController.");
				serverFactions.SetRaceTemplateOnSpawn(race);
				serverFactions.InitializeOnce(serverPet);
				LogAssert.IsTrue(serverFactions.FactionsAreTemplateDerived,
					"An NPC seeds a derived roster from its race as soon as it is assembled.");

				serverFactions.CopyFrom(ownerFactions);          // what PetSystem does before the spawn
				LogAssert.IsFalse(serverFactions.FactionsAreTemplateDerived,
					"A copied roster is owned state and must travel.");

				Writer payload = new Writer();
				serverFactions.WritePayload(null, payload);

				FactionController clientFactions = clientPetGo.GetComponent<FactionController>();
				clientFactions.InitializeOnce(clientPet);
				LogAssert.IsTrue(clientFactions.Character as NPC != null,
					"A pet is an NPC; SetFaction would refuse every entry.");

				Reader reader = new Reader(payload.GetArraySegment(), null);
				clientFactions.ReadPayload(null, reader);
				LogAssert.AreEqual(0, reader.Remaining, "The faction block must be consumed exactly.");

				LogAssert.AreEqual(2, clientFactions.Factions.Count,
					"A pet must arrive holding the standings its owner handed it.");
				LogAssert.IsTrue(clientFactions.Hostile.ContainsKey(hostile.ID),
					"...grouped as its owner had them.");

				// And the guard the restore path bypasses is still in force for gameplay adjustments.
				clientFactions.SetFaction(hostile.ID, FactionTemplate.Maximum);
				LogAssert.AreEqual(FactionTemplate.Minimum, clientFactions.Factions[hostile.ID].Value,
					"An NPC still refuses a gameplay faction adjustment.");
			}
			finally
			{
				foreach (UnityEngine.Object asset in assets) UnityEngine.Object.DestroyImmediate(asset);
				UnityEngine.Object.DestroyImmediate(ownerGo);
				UnityEngine.Object.DestroyImmediate(serverPetGo);
				UnityEngine.Object.DestroyImmediate(clientPetGo);
			}
		}

		/// <summary>
		/// The same double-count on the observer route: watching a peer equip a generated item used
		/// to add that item's bonuses to the observer's copy of the peer's sheet, on top of the
		/// authoritative total the server already broadcasts.
		/// </summary>
		[Test]
		public void ObservedEquipSlot_DoesNotApplyAttributeModifiersLocally()
		{
			GameObject go = new GameObject("ObservedEquipProbe");
			List<UnityEngine.Object> assets = new List<UnityEngine.Object>();
			try
			{
				MockCharacter character = new MockCharacter();
				CharacterAttributeController attributes = go.AddComponent<CharacterAttributeController>();
				attributes.InitializeOnce(character);

				CharacterAttributeTemplate attributeTemplate = ScriptableObject.CreateInstance<CharacterAttributeTemplate>();
				attributeTemplate.name = "Audit_Observed_Armor"; attributeTemplate.InitialValue = 10;
				attributeTemplate.AddToCache(attributeTemplate.name); assets.Add(attributeTemplate);
				CharacterAttribute armor = new CharacterAttribute(attributes, attributeTemplate.ID, 10, 0);
				attributes.AddAttribute(armor);

				// What the server's authoritative attribute broadcast installed, gear included.
				armor.SetModifier(40);

				EquipmentController equipment = go.AddComponent<EquipmentController>();
				equipment.InitializeOnce(character);

				MethodInfo applyObserved = typeof(EquipmentController).GetMethod("ApplyObservedSlot", Any);
				LogAssert.IsTrue(applyObserved != null, "EquipmentController must expose ApplyObservedSlot.");
				// Template id 0 clears the slot; an unknown id is refused before any equip. Either
				// way the observer path must leave the attribute sheet exactly as broadcast.
				applyObserved.Invoke(equipment, new object[] { 0, 0, 0 });

				LogAssert.AreEqual(40, armor.ExternalModifier,
					"An observed equipment change must not recompute another character's attributes locally.");

				string source = File.ReadAllText(Path.Combine(Application.dataPath,
					"Scripts/Shared/Implementation/Entity/Prediction/Equipment/EquipmentController.cs"));
				int start = source.IndexOf("internal void ApplyObservedSlot", StringComparison.Ordinal);
				int end = source.IndexOf("// ── Owner acknowledgements", start, StringComparison.Ordinal);
				LogAssert.IsTrue(start >= 0 && end > start, "ApplyObservedSlot must be locatable.");
				string body = source.Substring(start, end - start);
				LogAssert.IsTrue(body.Contains("SetEquippedCharacterSilently"),
					"The observer path must equip silently — Equip() runs ItemGenerator.ApplyAttributes.");
				LogAssert.IsFalse(body.Contains("Equippable.Equip(Character)"),
					"The observer path must not call the attribute-applying Equip.");
				LogAssert.IsFalse(body.Contains("DetachFromSlot(current, (byte)slot);"),
					"The observer path's detach must be silent too, or it subtracts what it never added.");
			}
			finally
			{
				foreach (UnityEngine.Object asset in assets) UnityEngine.Object.DestroyImmediate(asset);
				UnityEngine.Object.DestroyImmediate(go);
			}
		}

		// ── 12. The ungrounded ground normal is the motor's up vector, not zero ──

		/// <summary>
		/// The grounding serializer omits the three normals while airborne and the reader
		/// reconstructs them as zero — correct only if the writer's baseline is zero too. The motor
		/// seeds an ungrounded report's <c>GroundNormal</c> with the character's up vector, so the
		/// two peers held different baselines for the whole airborne stretch, and the landing delta
		/// was decoded against the wrong one.
		/// </summary>
		[Test]
		public void UngroundedNormals_AreCanonicalisedByTheProducer_SoTheChainSurvivesAJump()
		{
			// What an airborne motor actually hands the snapshot, before canonicalisation.
			CharacterTransientGroundingReport airborneFromMotor = default;
			airborneFromMotor.FoundAnyGround = false;
			airborneFromMotor.GroundNormal = Vector3.up;

			CharacterTransientGroundingReport airborneCanonical = airborneFromMotor;
			airborneCanonical.GroundNormal = Vector3.zero;

			// Both peers start grounded and agree.
			CharacterTransientGroundingReport grounded = default;
			grounded.FoundAnyGround = true;
			grounded.IsStableOnGround = true;
			grounded.GroundNormal = Vector3.up;
			grounded.InnerGroundNormal = Vector3.up;
			grounded.OuterGroundNormal = Vector3.up;

			// Airborne tick: the writer sends no normals, the reader zeroes them.
			CharacterTransientGroundingReport clientAirborne = RoundTripGrounding(grounded, airborneCanonical);
			LogAssert.AreEqual(Vector3.zero, clientAirborne.GroundNormal,
				"The reader reconstructs an ungrounded normal as zero.");

			// Landing on a slope, delta'd against each side's own airborne baseline.
			CharacterTransientGroundingReport landed = default;
			landed.FoundAnyGround = true;
			landed.IsStableOnGround = true;
			Vector3 slope = new Vector3(0.3f, 0.95f, 0f).normalized;
			landed.GroundNormal = slope;
			landed.InnerGroundNormal = slope;
			landed.OuterGroundNormal = slope;

			CharacterTransientGroundingReport clientLanded = RoundTripGrounding(airborneCanonical, landed, clientAirborne);
			float error = Vector3.Angle(clientLanded.GroundNormal, slope);
			LogAssert.IsTrue(error < 1f,
				$"The landing normal must decode to the server's, within the packed resolution; was {error:F2} degrees out.");

			/* Negative control: the same landing delta written against the RAW motor baseline the
			 * producer used to hand over, and read against the zero the reader holds. */
			CharacterTransientGroundingReport clientLandedUncanonical =
				RoundTripGrounding(airborneFromMotor, landed, clientAirborne);
			float uncanonicalError = Vector3.Angle(clientLandedUncanonical.GroundNormal, slope);
			LogAssert.IsTrue(uncanonicalError > 10f,
				$"Without canonicalisation the two baselines disagree and the landing normal decodes wrong; got only {uncanonicalError:F2} degrees of error, so this control no longer proves anything.");
		}

		/// <summary>Writes a grounding delta against <paramref name="writerBaseline"/> and reads it against <paramref name="readerBaseline"/>.</summary>
		private static CharacterTransientGroundingReport RoundTripGrounding(
			CharacterTransientGroundingReport writerBaseline,
			CharacterTransientGroundingReport next,
			CharacterTransientGroundingReport? readerBaseline = null)
		{
			Writer writer = new Writer();
			bool wrote = CharacterTransientGroundingReportDeltaSerializer.WriteDelta(
				writer, writerBaseline, next, DeltaSerializerOption.Unset);
			Reader reader = new Reader(writer.GetArraySegment(), null);
			CharacterTransientGroundingReport result = wrote
				? CharacterTransientGroundingReportDeltaSerializer.ReadDelta(reader, readerBaseline ?? writerBaseline)
				: (readerBaseline ?? writerBaseline);
			LogAssert.AreEqual(0, reader.Remaining, "The grounding block must be consumed exactly.");
			return result;
		}

		/// <summary>
		/// The producer is what makes the reader's zero correct; if this stops zeroing, the chain
		/// above breaks silently on every jump.
		/// </summary>
		[Test]
		public void KccControllerGetState_ZeroesUngroundedNormals()
		{
			string source = File.ReadAllText(Path.Combine(Application.dataPath,
				"Scripts/Shared/Implementation/Entity/Prediction/KCC/KCCController.cs"));
			int start = source.IndexOf("public KinematicCharacterMotorState GetState()", StringComparison.Ordinal);
			int end = source.IndexOf("return baseState;", start, StringComparison.Ordinal);
			LogAssert.IsTrue(start >= 0 && end > start, "KCCController.GetState must be locatable.");
			string body = source.Substring(start, end - start);
			LogAssert.IsTrue(body.Contains("!baseState.GroundingStatus.FoundAnyGround"),
				"GetState must canonicalise the ungrounded grounding normals; the motor seeds GroundNormal with the character's up vector.");
			LogAssert.IsTrue(body.Contains("GroundNormal = Vector3.zero"),
				"...by zeroing them, which is the value the reader reconstructs.");
		}

		// ── Round 3: the remaining confirmed defects ──

		/// <summary>
		/// Lag compensation used to rewind a constant two ticks because its "latency" term was
		/// stamped with the current server tick immediately before the replicate body ran. The
		/// offset now travels in the input, so it must survive the wire exactly.
		/// </summary>
		[Test]
		public void ViewOffsetTicks_SurvivesTheReplicateWire()
		{
			CharacterReplicateData prev = default;
			prev.AimDirection = AimDirectionCompression.QuantizedFallbackDirection;
			prev.ViewOffsetTicks = 2;

			CharacterReplicateData next = prev;
			next.ViewOffsetTicks = 9;

			// Full serializer.
			Writer full = new Writer();
			full.WriteCharacterReplicateData(next);
			Reader fullReader = new Reader(full.GetArraySegment(), null);
			LogAssert.AreEqual(9, (int)fullReader.ReadCharacterReplicateData().ViewOffsetTicks,
				"The absolute form must carry the view offset.");
			LogAssert.AreEqual(0, fullReader.Remaining, "Consumed exactly.");

			// Delta serializer.
			Writer delta = new Writer();
			LogAssert.IsTrue(delta.WriteDelta(prev, next, DeltaSerializerOption.RootSerialize),
				"A changed view offset must produce a delta.");
			Reader deltaReader = new Reader(delta.GetArraySegment(), null);
			LogAssert.AreEqual(9, (int)deltaReader.ReadDelta(prev).ViewOffsetTicks,
				"The delta form must carry the view offset.");
			LogAssert.AreEqual(0, deltaReader.Remaining, "Consumed exactly.");

			// Unchanged costs nothing.
			Writer unchanged = new Writer();
			unchanged.WriteDelta(next, next, DeltaSerializerOption.RootSerialize);
			Reader unchangedReader = new Reader(unchanged.GetArraySegment(), null);
			LogAssert.AreEqual(9, (int)unchangedReader.ReadDelta(next).ViewOffsetTicks,
				"An unchanged view offset carries forward from the baseline.");
		}

		/// <summary>The client's claim is capped before it reaches the position history.</summary>
		[Test]
		public void ViewOffsetClaim_IsCapped()
		{
			LogAssert.IsTrue(LagCompensationTick.MaximumCompensationTicks > LagCompensationTick.SpectatorInterpolationTicks,
				"The cap must leave room for a real view offset.");
			LogAssert.IsTrue(LagCompensationTick.MaximumCompensationTicks <= 60,
				"The cap must still bound how far into the past a client can claim to have seen.");

			string source = File.ReadAllText(Path.Combine(Application.dataPath,
				"Scripts/Shared/Implementation/Entity/Prediction/LagCompensation/LagCompensationTick.cs"));
			// The EXECUTING code, not the remarks — which name the old API deliberately, to explain
			// why it is not used.
			LogAssert.IsFalse(source.Contains("uint lagTicks = owner.ReplicateTick.LocalTickDifference"),
				"The rewind must not compute its offset from ReplicateTick.LocalTickDifference, which reads 0 on every tick that carries real input.");
			LogAssert.IsTrue(source.Contains("MaximumCompensationTicks"),
				"The client-supplied offset must be capped.");
		}

		/// <summary>
		/// The motor state declares IReconcileData, so its delta must be able to produce a payload a
		/// peer with no baseline can decode.
		/// </summary>
		[Test]
		public void MotorStateDelta_FullSerialize_IsAbsolute()
		{
			KinematicCharacterMotorState baseline = default;
			baseline.Rotation = Quaternion.identity;
			baseline.Position = new Vector3(120f, 8f, -40f);
			baseline.BaseVelocity = new Vector3(2f, 0f, 1f);

			KinematicCharacterMotorState next = baseline;
			next.Position = new Vector3(121f, 8f, -39f);

			Writer writer = new Writer();
			LogAssert.IsTrue(writer.WriteDelta(baseline, next, DeltaSerializerOption.FullSerialize),
				"FullSerialize always writes.");

			// A peer holding NOTHING must still decode it.
			Reader reader = new Reader(writer.GetArraySegment(), null);
			KinematicCharacterMotorState decoded = reader.ReadDelta<KinematicCharacterMotorState>(default);
			LogAssert.AreEqual(0, reader.Remaining, "Consumed exactly.");
			LogAssert.IsTrue((decoded.Position - next.Position).magnitude < 0.05f,
				"A full serialize must be decodable from an empty baseline, not relative to one.");
		}

		/// <summary>
		/// A stacking buff applied for the first time used to run both the new-buff branch and the
		/// stack branch, applying its modifier twice.
		/// </summary>
		[Test]
		public void FreshStackingBuff_AppliesItsModifierOnce()
		{
			string source = File.ReadAllText(Path.Combine(Application.dataPath,
				"Scripts/Shared/Implementation/Entity/Prediction/Buff/BuffController.cs"));
			LogAssert.IsTrue(source.Contains("if (!isNew && template.MaxStacks > 0"),
				"Only an existing buff may take a stack; a new one already applied its modifier.");
		}

		/// <summary>
		/// A pooled object must not inherit the previous occupant's permanent buffs — they carry
		/// attribute modifiers.
		/// </summary>
		[Test]
		public void LifecycleRemoveAll_ClearsPermanentBuffsToo()
		{
			MethodInfo removeAll = typeof(BuffController).GetMethod("RemoveAll", Any);
			LogAssert.IsTrue(removeAll != null, "BuffController.RemoveAll must exist.");
			/* Asserted by NAME, not by arity. The property that matters is that clearing permanent
			 * buffs is an explicit decision at each call site; how many other options RemoveAll
			 * grew since is irrelevant, and pinning the count made an unrelated parameter look like
			 * a regression. */
			ParameterInfo[] parameters = removeAll.GetParameters();
			bool hasIncludePermanent = false;
			for (int i = 0; i < parameters.Length; ++i)
			{
				hasIncludePermanent |= parameters[i].Name == "includePermanent";
			}
			LogAssert.IsTrue(hasIncludePermanent,
				"RemoveAll must take an explicit includePermanent flag rather than always exempting permanent buffs.");

			string source = File.ReadAllText(Path.Combine(Application.dataPath,
				"Scripts/Shared/Implementation/Entity/Prediction/Buff/BuffController.cs"));
			int count = source.Split(new[] { "includePermanent: true" }, StringSplitOptions.None).Length - 1;
			LogAssert.AreEqual(2, count,
				"Both lifecycle callers (ResetState and ReadPayload) must clear permanent buffs.");
		}

		/// <summary>ECA triggers are server-authoritative; actions are not all self-gating.</summary>
		[Test]
		public void BuffEcaTriggers_OnlyDispatchOnTheAuthoritativePeer()
		{
			string source = File.ReadAllText(Path.Combine(Application.dataPath,
				"Scripts/Shared/Implementation/Entity/Prediction/Buff/BuffController.cs"));
			foreach (string invoke in new[] { "Character.Invoke(onBuffApplyTriggers", "Character.Invoke(onBuffRemoveTriggers" })
			{
				int index = 0;
				while ((index = source.IndexOf(invoke, index, StringComparison.Ordinal)) >= 0)
				{
					// The guard must appear close above each dispatch.
					string preceding = source.Substring(Math.Max(0, index - 400), Math.Min(400, index));
					LogAssert.IsTrue(preceding.Contains("IsAuthoritativePeer"),
						$"Every '{invoke}' must sit under an IsAuthoritativePeer guard.");
					index += invoke.Length;
				}
			}
		}

		/// <summary>A prefab authored immortal must survive pool reuse.</summary>
		[Test]
		public void ResetState_PreservesTheAuthoredImmortalFlag()
		{
			string source = File.ReadAllText(Path.Combine(Application.dataPath,
				"Scripts/Shared/Implementation/Entity/Prediction/CharacterAttribute/CharacterDamageController.cs"));
			LogAssert.IsFalse(source.Contains("\n\t\t\timmortal = false;"),
				"ResetState must not force the authored immortal flag to false.");
			LogAssert.IsTrue(source.Contains("authoredImmortal"),
				"The prefab-authored immortal value must be captured and restored.");
		}

		/// <summary>
		/// A resource's authoritative maximum must survive the next local modifier change; only
		/// stamping FinalValue left it one AddModifier away from being recomputed away.
		/// </summary>
		[Test]
		public void ReconciledResourceMaximum_SurvivesALaterModifierChange()
		{
			GameObject go = new GameObject("ResourceReconcileProbe");
			List<UnityEngine.Object> assets = new List<UnityEngine.Object>();
			try
			{
				CharacterAttributeController attributes = go.AddComponent<CharacterAttributeController>();
				attributes.InitializeOnce(new MockCharacter());

				CharacterAttributeTemplate template = ScriptableObject.CreateInstance<CharacterAttributeTemplate>();
				template.name = "Audit_Resource_Health"; template.InitialValue = 100; template.IsResourceAttribute = true;
				template.AddToCache(template.name); assets.Add(template);
				CharacterResourceAttribute health = new CharacterResourceAttribute(attributes, template.ID, 100, 100, 0);
				attributes.AddResourceAttribute(health);

				// The server's authoritative maximum, as a reconcile installs it.
				MethodInfo apply = typeof(CharacterAttributeController).GetMethod("ApplyIndividualResourceState", Any);
				LogAssert.IsTrue(apply != null, "ApplyIndividualResourceState must exist.");
				apply.Invoke(attributes, new object[] { template.ID, 150, 150f });
				LogAssert.AreEqual(150, health.FinalValue, "The reconcile installs the server's maximum.");

				/* Anything that changes a modifier afterwards recomputes FinalValue from `value` and
				 * `externalModifier` — neither of which a resource reconcile carries. With only
				 * SetFinal stamping the number, that recompute produced 100 + 10 = 110 and the
				 * server's maximum was gone. (AddModifier(0) is a no-op by design, so the trigger
				 * has to be a real change.) */
				health.AddModifier(10);
				LogAssert.AreEqual(160, health.FinalValue,
					"A later modifier change must build on the reconciled maximum, not discard it.");
			}
			finally
			{
				foreach (UnityEngine.Object asset in assets) UnityEngine.Object.DestroyImmediate(asset);
				UnityEngine.Object.DestroyImmediate(go);
			}
		}

		/// <summary>
		/// An activation that completed without producing an object must take back the object the
		/// owner predicted for that tick — the seeds agree, so nothing else can.
		/// </summary>
		[Test]
		public void ServerSpawnedNothing_RollsBackThePredictedObject()
		{
			const int agreedSeed = 4242;
			LogAssert.IsFalse(AbilityController.ShouldDestroySpawnsAtReconcileTick(
					denied: false, havePredicted: true, predictedSeed: agreedSeed,
					havePrevious: true, previousSeed: 11, serverSeed: agreedSeed),
				"Agreeing seeds alone confirm the object — this is the state the ghost survived in.");

			LogAssert.IsTrue(AbilityController.ShouldDestroySpawnsAtReconcileTick(
					denied: false, havePredicted: true, predictedSeed: agreedSeed,
					havePrevious: true, previousSeed: 11, serverSeed: agreedSeed,
					serverSpawnedNothing: true),
				"With the server reporting no spawn at this tick, the predicted object must go.");

			LogAssert.IsFalse(AbilityController.ShouldDestroySpawnsAtReconcileTick(
					denied: false, havePredicted: false, predictedSeed: 0,
					havePrevious: false, previousSeed: 0, serverSeed: agreedSeed,
					serverSpawnedNothing: true),
				"With no history for the tick there is nothing to roll back.");
		}

		/// <summary>The platform never replays, so a replaying rider must ask for its tick.</summary>
		[Test]
		public void PlatformVelocityHistory_AnswersForAPastTick()
		{
			GameObject go = new GameObject("PlatformHistory");
			try
			{
				KCCPlatform platform = go.AddComponent<KCCPlatform>();
				MethodInfo record = typeof(KCCPlatform).GetMethod("RecordTickVelocity", Any);
				LogAssert.IsTrue(record != null, "KCCPlatform must record per-tick velocity.");

				record.Invoke(platform, new object[] { 100u, new Vector3(0f, 0f, 4f) });
				record.Invoke(platform, new object[] { 101u, new Vector3(0f, 0f, -4f) });

				LogAssert.IsTrue(platform.TryGetVelocityForTick(100u, out Vector3 atHundred));
				LogAssert.IsTrue((atHundred - new Vector3(0f, 0f, 4f)).magnitude < 1e-4f,
					"A replayed tick must get that tick's velocity, not the present one.");
				LogAssert.IsTrue(platform.TryGetVelocityForTick(101u, out Vector3 atHundredOne));
				LogAssert.IsTrue((atHundredOne - new Vector3(0f, 0f, -4f)).magnitude < 1e-4f,
					"...including across a direction reversal, which is where it mattered.");
				LogAssert.IsFalse(platform.TryGetVelocityForTick(5000u, out _),
					"A tick outside the ring reports miss rather than a stale slot's value.");
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(go);
			}
		}

		/// <summary>Destroying an item must remove the modifiers it applied.</summary>
		[Test]
		public void ItemDestroy_UnequipsBeforeDetachingItsHandlers()
		{
			string source = File.ReadAllText(Path.Combine(Application.dataPath,
				"Scripts/Shared/Implementation/Entity/Item/Item.cs"));
			int destroy = source.IndexOf("public void Destroy()", StringComparison.Ordinal);
			LogAssert.IsTrue(destroy >= 0, "Item.Destroy must exist.");
			int equippableDestroy = source.IndexOf("Equippable.Destroy();", destroy, StringComparison.Ordinal);
			int detach = source.IndexOf("Equippable.OnUnequip -= ItemEquippable_OnUnequip;", destroy, StringComparison.Ordinal);
			LogAssert.IsTrue(equippableDestroy >= 0 && detach >= 0, "Both steps must be present.");
			LogAssert.IsTrue(equippableDestroy < detach,
				"Unequip must run BEFORE the handlers are detached, or OnUnequip fires into an empty list and the item's attribute modifiers are never removed.");
		}

		/// <summary>The shipped buff assets must not serialize fields the template no longer has.</summary>
		[Test]
		public void ShippedBuffAssets_MatchTheirTemplateFields()
		{
			string[] assets = Directory.GetFiles(
				Path.Combine(Application.dataPath, "Templates/Entity/Buffs"), "*.asset");
			LogAssert.IsTrue(assets.Length > 0, "There must be buff assets to check.");

			foreach (string path in assets)
			{
				string text = File.ReadAllText(path);
				string name = Path.GetFileName(path);
				LogAssert.IsFalse(text.Contains("FXPrefab: "),
					$"{name} serializes FXPrefab, which no longer exists — the field is FXPrefabReference, so the buff has no FX.");
				LogAssert.IsFalse(text.Contains("UseCount:"),
					$"{name} serializes UseCount, which no longer exists on BaseBuffTemplate.");
				LogAssert.IsTrue(text.Contains("FXPrefabReference:"),
					$"{name} must carry the FX through the addressable reference the template declares.");

				// A tick rate with nothing to tick costs a full buff-array resend per interval.
				if (!text.Contains("OnTickEvents:"))
				{
					LogAssert.IsTrue(text.Contains("TickRate: 0"),
						$"{name} has no OnTickEvents, so its TickRate must be 0 — otherwise it dirties the reconcile snapshot on every interval for nothing.");
				}
			}
		}

		/// <summary>
		/// The float delta primitive quantised the magnitude of a difference by flooring it, so the
		/// error never cancelled — it accumulated toward the previous value until the next absolute
		/// snapshot snapped the position forward.
		/// </summary>
		[Test]
		public void FloatDelta_DoesNotDriftTowardThePreviousValue()
		{
			// A steady 5 m/s at tick rate 30: 0.1667 per tick, which floors to 0.166.
			const float perTick = 5f / 30f;
			float server = 0f;
			float client = 0f;

			for (int i = 0; i < 29; i++)
			{
				float next = server + perTick;

				Writer writer = new Writer();
				LogAssert.IsTrue(writer.WriteUDeltaSingle(server, next), "A real movement must be written.");
				Reader reader = new Reader(writer.GetArraySegment(), null);
				client = reader.ReadUDeltaSingle(client);

				// The writer advances its baseline to the exact value, as Reconcile_Send does.
				server = next;
			}

			float drift = Mathf.Abs(server - client);

			/* What flooring would have produced over the same chain: the whole fractional part is
			 * lost every tick, in the same direction, so it accumulates linearly. Rounding cannot
			 * remove the error — a constant per-tick difference rounds the same way every tick, so
			 * some bias survives — but it halves the magnitude and stops the direction from being
			 * guaranteed. The once-per-second absolute snapshot remains the backstop for what is
			 * left. Comparing against the model rather than a fixed number keeps this honest if the
			 * quantisation constant ever changes. */
			float floorErrorPerTick = perTick - (float)(System.Math.Floor(perTick * 1000d) / 1000d);
			float floorDrift = 29 * floorErrorPerTick;

			TestContext.WriteLine($"MEASURE 29-tick float delta drift: {drift * 100f:F2} cm (flooring would give {floorDrift * 100f:F2} cm)");
			LogAssert.IsTrue(drift < floorDrift * 0.6f,
				$"29 ticks of walking drifted {drift * 100f:F2} cm against flooring's {floorDrift * 100f:F2} cm; the quantised difference is still biased toward the previous value.");
		}

		/// <summary>
		/// The quaternion delta writer discarded a change below its dead zone while the caller
		/// advanced its baseline anyway, so a slow turn was never transmitted at all.
		/// </summary>
		[Test]
		public void SlowRotation_IsNotDiscardedByTheQuaternionDeadZone()
		{
			// ~0.25 degrees per tick — under the old 0.0025 component threshold.
			Quaternion server = Quaternion.identity;
			Quaternion client = Quaternion.identity;

			for (int i = 0; i < 20; i++)
			{
				Quaternion next = Quaternion.Euler(0f, (i + 1) * 0.25f, 0f);

				Writer writer = new Writer();
				if (writer.WriteDeltaQuaternion(server, next))
				{
					Reader reader = new Reader(writer.GetArraySegment(), null);
					client = reader.ReadDeltaQuaternion(client);
				}

				// The caller advances regardless of whether anything was written.
				server = next;
			}

			float error = Quaternion.Angle(server, client);
			TestContext.WriteLine($"MEASURE 20-tick slow-turn error: {error:F2} degrees");
			LogAssert.IsTrue(error < 1f,
				$"A slow turn accumulated {error:F2} degrees of unsent rotation; the dead zone discards changes the caller has already committed to.");
		}

		/// <summary>
		/// Two independent rolls can select the same attribute template, and the dictionary add
		/// threw — from inside a spawn payload read and from the equipment reconcile.
		/// </summary>
		[Test]
		public void DuplicateRandomItemAttribute_DoesNotThrow()
		{
			string source = File.ReadAllText(Path.Combine(Application.dataPath,
				"Scripts/Shared/Implementation/Entity/Item/ItemGenerator.cs"));
			int start = source.IndexOf("private void AddRandomAttributes", StringComparison.Ordinal);
			LogAssert.IsTrue(start >= 0, "AddRandomAttributes must be locatable.");
			string body = source.Substring(start, 1600);

			LogAssert.IsTrue(body.Contains("ContainsKey"),
				"A repeated template roll must be skipped rather than thrown on — Dictionary.Add throws on a duplicate key, from inside a payload read.");
			int roll = body.IndexOf("int rolledValue = random.Next", StringComparison.Ordinal);
			int guard = body.IndexOf("ContainsKey", StringComparison.Ordinal);
			LogAssert.IsTrue(roll >= 0 && roll < guard,
				"The value must still be drawn before the duplicate is skipped, or the RNG stream advances differently on peers that skip.");
		}

		// ── Permanent buffs and their FX across a respawn ──

		/// <summary>A buff template that reports whether its FX was spawned and torn down.</summary>
		private sealed class PermanentFXProbeTemplate : BaseBuffTemplate
		{
			public int ApplyFXCalls;
			public int RemoveFXCalls;
			public bool ThrowOnRemove;

			public override void OnApply(Buff buff, ICharacter target) { }

			public override void OnRemove(Buff buff, ICharacter target)
			{
				if (ThrowOnRemove)
				{
					throw new InvalidOperationException("Probe: OnRemove failed.");
				}
			}

			public override GameObject OnApplyFX(Buff buff, ICharacter target)
			{
				++ApplyFXCalls;
				return new GameObject($"{name}_FX");
			}

			public override void OnRemoveFX(GameObject fxInstance, ICharacter target)
			{
				++RemoveFXCalls;
				if (fxInstance != null)
				{
					UnityEngine.Object.DestroyImmediate(fxInstance);
				}
			}
		}

		private static BuffController BuildBuffController(GameObject go, ICharacter character)
		{
			BuffController controller = go.AddComponent<BuffController>();
			controller.InitializeOnce(character);
			return controller;
		}

		/// <summary>
		/// Clearing a permanent buff on respawn must take its FX with it. A pooled object becomes a
		/// different character; an aura left running belongs to the previous occupant.
		/// </summary>
		[Test]
		public void PermanentBuff_ClearedOnRespawn_TakesItsFXWithIt()
		{
			GameObject go = new GameObject("PermanentBuffFX");
			List<UnityEngine.Object> assets = new List<UnityEngine.Object>();
			try
			{
				PermanentFXProbeTemplate template = ScriptableObject.CreateInstance<PermanentFXProbeTemplate>();
				template.name = "Audit_Permanent_Buff";
				template.IsPermanent = true;
				template.Duration = 0f;
				template.AddToCache(template.name);
				assets.Add(template);

				BuffController buffs = BuildBuffController(go, new MockCharacter());
				buffs.Apply(new Buff(template.ID, 0u, 0u, 1f / 30f, 0, 0), suppressFX: false);

				LogAssert.AreEqual(1, template.ApplyFXCalls, "The permanent buff's FX must have spawned.");

				FieldInfo instances = typeof(BuffController).GetField("buffFXInstances", Any);
				LogAssert.IsTrue(instances != null, "BuffController must track its FX instances.");
				int liveBefore = ((System.Collections.ICollection)instances.GetValue(buffs)).Count;
				LogAssert.AreEqual(1, liveBefore, "One FX instance is tracked while the buff is applied.");

				// What a pooled respawn does.
				buffs.RemoveAll(ignoreInvokeRemove: true, includePermanent: true);

				FieldInfo buffTable = typeof(BuffController).GetField("buffs", Any);
				int remaining = ((System.Collections.ICollection)buffTable.GetValue(buffs)).Count;
				LogAssert.AreEqual(0, remaining, "A lifecycle teardown clears permanent buffs.");
				LogAssert.AreEqual(1, template.RemoveFXCalls,
					"...and their FX with them — an aura must not outlive the character it belonged to.");
				LogAssert.AreEqual(0, ((System.Collections.ICollection)instances.GetValue(buffs)).Count,
					"No FX instance may remain tracked after the teardown.");
			}
			finally
			{
				foreach (UnityEngine.Object asset in assets) UnityEngine.Object.DestroyImmediate(asset);
				UnityEngine.Object.DestroyImmediate(go);
			}
		}

		/// <summary>
		/// Even when a template's OnRemove throws — which keeps the buff tracked so its attribute
		/// modifiers are not orphaned — the FX must still be torn down on a lifecycle teardown.
		/// </summary>
		[Test]
		public void PermanentBuffFX_IsTornDown_EvenWhenRemovalThrows()
		{
			GameObject go = new GameObject("PermanentBuffFXThrow");
			List<UnityEngine.Object> assets = new List<UnityEngine.Object>();
			try
			{
				PermanentFXProbeTemplate template = ScriptableObject.CreateInstance<PermanentFXProbeTemplate>();
				template.name = "Audit_Permanent_Buff_Throws";
				template.IsPermanent = true;
				template.Duration = 0f;
				template.AddToCache(template.name);
				assets.Add(template);

				BuffController buffs = BuildBuffController(go, new MockCharacter());
				buffs.Apply(new Buff(template.ID, 0u, 0u, 1f / 30f, 0, 0), suppressFX: false);
				LogAssert.AreEqual(1, template.ApplyFXCalls, "The FX must have spawned.");

				template.ThrowOnRemove = true;

				UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
				try
				{
					buffs.RemoveAll(ignoreInvokeRemove: true, includePermanent: true);
				}
				finally
				{
					UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;
				}

				LogAssert.AreEqual(1, template.RemoveFXCalls,
					"A failed effect removal keeps the buff tracked on purpose, but the FX has no modifier to orphan and must still go.");
				FieldInfo instances = typeof(BuffController).GetField("buffFXInstances", Any);
				LogAssert.AreEqual(0, ((System.Collections.ICollection)instances.GetValue(buffs)).Count,
					"No FX instance may remain tracked.");
			}
			finally
			{
				foreach (UnityEngine.Object asset in assets) UnityEngine.Object.DestroyImmediate(asset);
				UnityEngine.Object.DestroyImmediate(go);
			}
		}

		// ── Lag compensation: input-tick anchor and sub-tick resolution ──

		/// <summary>
		/// A rewind resolved on a tick boundary reproduces a pose the client never rendered: its view
		/// of a peer is an interpolation between two received snapshots and sits BETWEEN ticks.
		/// </summary>
		[Test]
		public void RewindTarget_ResolvesBetweenTicks()
		{
			GameObject go = new GameObject("SubTickHistory");
			try
			{
				CharacterPositionHistory history = go.AddComponent<CharacterPositionHistory>();
				MethodInfo allocate = typeof(CharacterPositionHistory).GetMethod("AllocateBuffer", Any);
				MethodInfo record = typeof(CharacterPositionHistory).GetMethod("Record", Any);
				LogAssert.IsTrue(allocate != null && record != null,
					"CharacterPositionHistory must allocate and record through these.");
				allocate.Invoke(history, new object[] { 32 });

				// A peer moving 1 unit per tick along +z.
				for (uint tick = 100; tick <= 110; tick++)
				{
					record.Invoke(history, new object[] { tick, new Vector3(0f, 0f, tick), Quaternion.identity });
				}

				// Exactly on a tick: unchanged behaviour.
				LogAssert.IsTrue(history.TryResolve(new RewindTarget(105u, 0f), out CharacterPositionHistory.Snapshot onTick));
				LogAssert.IsTrue(Mathf.Abs(onTick.Position.z - 105f) < 1e-3f, "A zero fraction resolves the tick itself.");

				// A quarter of a tick BEFORE tick 105 is three quarters of the way from 104 to 105.
				LogAssert.IsTrue(history.TryResolve(new RewindTarget(105u, 0.25f), out CharacterPositionHistory.Snapshot quarter));
                LogAssert.IsTrue(Mathf.Abs(quarter.Position.z - 104.75f) < 1e-3f,
					$"A 0.25-tick offset must land at 104.75, not on a boundary; got {quarter.Position.z:F3}.");

				// Three quarters before 105 is a quarter of the way from 104.
				LogAssert.IsTrue(history.TryResolve(new RewindTarget(105u, 0.75f), out CharacterPositionHistory.Snapshot threeQuarter));
				LogAssert.IsTrue(Mathf.Abs(threeQuarter.Position.z - 104.25f) < 1e-3f,
					$"A 0.75-tick offset must land at 104.25; got {threeQuarter.Position.z:F3}.");
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(go);
			}
		}

		/// <summary>The fraction arrives off the wire, so it is clamped rather than trusted.</summary>
		[Test]
		public void RewindTarget_ClampsAnUntrustedFraction()
		{
			LogAssert.AreEqual(0f, new RewindTarget(50u, -3f).SubTickFraction, "A negative fraction clamps to zero.");
			LogAssert.IsTrue(new RewindTarget(50u, 5f).SubTickFraction < 1f, "A fraction of a whole tick or more clamps below one.");

			new RewindTarget(50u, 0f).GetBounds(out uint older, out uint newer, out float alpha);
			LogAssert.AreEqual(50u, older, "A zero fraction collapses both bounds...");
			LogAssert.AreEqual(50u, newer, "...onto the tick itself.");
			LogAssert.AreEqual(1f, alpha);

			LogAssert.IsFalse(RewindTarget.None.IsValid, "The empty target names nothing.");
		}

		/// <summary>
		/// The rewind must be anchored to the tick of the INPUT being simulated, not the server's
		/// present tick — the two differ by however much of that client's input the server has
		/// buffered, which is exactly the per-client quantity that varies.
		/// </summary>
		[Test]
		public void RewindAnchor_IsTheInputTick_NotTheServersPresent()
		{
			string source = File.ReadAllText(Path.Combine(Application.dataPath,
				"Scripts/Shared/Implementation/Entity/Prediction/LagCompensation/LagCompensationTick.cs"));
			int start = source.IndexOf("public static bool TryResolve(", StringComparison.Ordinal);
			LogAssert.IsTrue(start >= 0, "TryResolve must be locatable.");
			string body = source.Substring(start);

			LogAssert.IsTrue(body.Contains("CurrentReplicateTickSnapshot"),
				"The rewind must anchor on the replicate's own tick when one is being simulated.");
			int anchorAssign = body.IndexOf("anchorTick = replicateTick", StringComparison.Ordinal);
			int rewindBuild = body.IndexOf("new RewindTarget(anchorTick", StringComparison.Ordinal);
			LogAssert.IsTrue(anchorAssign >= 0 && rewindBuild > anchorAssign,
				"The input tick must be adopted as the anchor before the target is built.");
			LogAssert.IsTrue(body.Contains("CurrentViewOffsetFraction"),
				"The sub-tick remainder must reach the rewind, or it quantises back to a tick boundary.");
		}

		/// <summary>The sub-tick remainder must survive the wire like the whole part does.</summary>
		[Test]
		public void ViewOffsetFraction_SurvivesTheReplicateWire()
		{
			CharacterReplicateData prev = default;
			prev.AimDirection = AimDirectionCompression.QuantizedFallbackDirection;
			prev.ViewOffsetTicks = 3;
			prev.ViewOffsetFraction = 0;

			CharacterReplicateData next = prev;
			next.ViewOffsetFraction = 192;   // 0.75 of a tick

			Writer full = new Writer();
			full.WriteCharacterReplicateData(next);
			Reader fullReader = new Reader(full.GetArraySegment(), null);
			CharacterReplicateData decodedFull = fullReader.ReadCharacterReplicateData();
			LogAssert.AreEqual(192, (int)decodedFull.ViewOffsetFraction, "The absolute form carries the fraction.");
			LogAssert.AreEqual(0, fullReader.Remaining, "Consumed exactly.");

			Writer delta = new Writer();
			LogAssert.IsTrue(delta.WriteDelta(prev, next, DeltaSerializerOption.RootSerialize),
				"A changed fraction alone must produce a delta — the whole part did not move.");
			Reader deltaReader = new Reader(delta.GetArraySegment(), null);
			CharacterReplicateData decodedDelta = deltaReader.ReadDelta(prev);
			LogAssert.AreEqual(192, (int)decodedDelta.ViewOffsetFraction, "The delta form carries the fraction.");
			LogAssert.AreEqual(3, (int)decodedDelta.ViewOffsetTicks, "...alongside the unchanged whole part.");
			LogAssert.AreEqual(0, deltaReader.Remaining, "Consumed exactly.");
		}

		// ── Engagement-range exemption: tick-exact where it can be hit, throttled elsewhere ──

		[Test]
		public void EngagementRange_WidensWithReach_AndIsCappedAtTheAbilityCeiling()
		{
			float floor = ObserverStreamingPolicy.EngagementRange;
			float ceiling = ObserverStreamingPolicy.EngagementRangeCeiling;
			float margin = ObserverStreamingPolicy.EngagementRangeMargin;

			LogAssert.AreEqual(floor, ObserverStreamingPolicy.ResolveEngagementRange(0f),
				"A character with no reach still gets the floor — every ability authored today has a range of 0.");
			LogAssert.AreEqual(floor, ObserverStreamingPolicy.ResolveEngagementRange(5f),
				"A melee character keeps the LOD saving beyond the floor.");
			LogAssert.AreEqual(60f + margin, ObserverStreamingPolicy.ResolveEngagementRange(60f),
				"A long-range caster's radius covers its reach plus the closing margin.");
			LogAssert.AreEqual(ceiling, ObserverStreamingPolicy.ResolveEngagementRange(500f),
				"Nothing reaches past the ceiling, so nothing is exempt past it.");
		}

		/// <summary>
		/// Inside the radius the transform must be sent every tick — a throttled one is interpolated
		/// across the gap, and the pose the client renders then exists on no server tick at all.
		/// </summary>
		[Test]
		public void WithinEngagementRange_NothingThrottles()
		{
			GameObject go = new GameObject("EngagedLod");
			try
			{
				NetworkTransformDistanceLod lod = go.AddComponent<NetworkTransformDistanceLod>();
				MethodInfo band = typeof(NetworkTransformDistanceLod).GetMethod("BandObserver", Any);
				LogAssert.IsTrue(band != null, "BandObserver must exist.");

				const float engagement = 40f;
				float engagementSqr = engagement * engagement;

				NetworkConnection engagedObserver = new NetworkConnection { ClientId = 7 };
				NetworkConnection distantObserver = new NetworkConnection { ClientId = 8 };

				// 30 m: inside the radius, and inside a band that would otherwise throttle.
				band.Invoke(lod, new object[] { engagedObserver.ClientId, 30f * 30f, engagementSqr });
				LogAssert.AreEqual(1, (int)lod.GetInterval(engagedObserver),
					"A target the observer can reach must be sent every tick.");
				LogAssert.IsTrue(lod.IsEngaged(engagedObserver), "...and be marked engaged for the cap to honour.");

				// 70 m: beyond the radius, so the saving still applies.
				band.Invoke(lod, new object[] { distantObserver.ClientId, 70f * 70f, engagementSqr });
				LogAssert.IsTrue(lod.GetInterval(distantObserver) > 1,
					"Beyond the engagement radius the distance bands must still throttle — that is where the bandwidth saving lives.");
				LogAssert.IsFalse(lod.IsEngaged(distantObserver), "...and it must not be marked engaged.");
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(go);
			}
		}

		/// <summary>
		/// The relevance cap throttles independently of distance, so it has to honour the exemption
		/// too — otherwise a character standing next to its attacker is throttled by the cap alone.
		/// </summary>
		[Test]
		public void EngagementExemption_OverridesTheRelevanceCapToo()
		{
			string source = File.ReadAllText(Path.Combine(Application.dataPath,
				"Scripts/Shared/Implementation/Entity/Prediction/ObserverStreaming/ObserverStreamingEntry.cs"));
			int start = source.IndexOf("public byte GetEffectiveInterval", StringComparison.Ordinal);
			LogAssert.IsTrue(start >= 0, "GetEffectiveInterval must be locatable.");
			string body = source.Substring(start, 900);

			int exemption = body.IndexOf("IsEngaged", StringComparison.Ordinal);
			int capRead = body.IndexOf("GetInterval(connection)", StringComparison.Ordinal);
			LogAssert.IsTrue(exemption >= 0, "The cap path must consult the engagement exemption.");
			LogAssert.IsTrue(exemption < capRead,
				"The exemption must be checked BEFORE the cap is composed, or the cap can still throttle an engaged target.");
		}

		/// <summary>
		/// What the exemption costs. A 40 m disc out of the 100 m player observer range is 16% of the
		/// observed area, so the LOD keeps its saving over the other 84%.
		/// </summary>
		[Test]
		public void Measure_EngagementExemption_KeepsMostOfTheSaving()
		{
			float engagement = ObserverStreamingPolicy.EngagementRange;
			const float playerObserverRange = 100f;   // PlayerDistanceCondition._maximumDistance

			float exemptArea = Mathf.PI * engagement * engagement;
			float observedArea = Mathf.PI * playerObserverRange * playerObserverRange;
			float exemptFraction = exemptArea / observedArea;

			TestContext.WriteLine(
				$"MEASURE engagement exemption: {engagement:F0} m of {playerObserverRange:F0} m observer range " +
				$"= {exemptFraction * 100f:F0}% of observed area sent at full rate, {(1f - exemptFraction) * 100f:F0}% still throttled");

			LogAssert.IsTrue(exemptFraction < 0.35f,
				$"The exemption must leave most of the observed area throttled; it currently covers {exemptFraction * 100f:F0}%.");
			LogAssert.IsTrue(engagement <= ObserverStreamingPolicy.EngagementRangeCeiling,
				"The floor must not exceed the ability ceiling.");
		}

		// ── Visibility budget: the hard observer cap ──

		[Test]
		public void VisibilityBudget_AdmitsTheTopRanksAndExcludesTheRest()
		{
			int budget = ObserverStreamingPolicy.VisibilityBudget;
			LogAssert.IsTrue(budget > 0, "A budget of 0 disables the cap; the test needs one.");

			SeedRanks(clientId: 11, count: budget + 10);
			try
			{
				LogAssert.IsTrue(Budget(11, RankObjectId(0), currentlyVisible: false),
					"The most relevant character must be admitted.");
				LogAssert.IsTrue(Budget(11, RankObjectId(budget - 1), currentlyVisible: false),
					"The last character inside the budget must be admitted.");
				LogAssert.IsFalse(Budget(11, RankObjectId(budget), currentlyVisible: false),
					"The first character past the budget must be excluded — this is the cap.");
			}
			finally
			{
				ClearRegistryRanks();
			}
		}

		/// <summary>
		/// Rank hysteresis, not distance hysteresis: two near-identical scores at the boundary would
		/// otherwise spawn and despawn each other every pass, and a despawn is far more expensive
		/// than any rate change.
		/// </summary>
		[Test]
		public void VisibilityBudget_HoldsAnAlreadyVisibleCharacterPastTheBoundary()
		{
			int budget = ObserverStreamingPolicy.VisibilityBudget;
			int widened = Mathf.CeilToInt(budget * (1f + ObserverStreamingPolicy.VisibilityBudgetHysteresis));
			LogAssert.IsTrue(widened > budget, "The hysteresis must actually widen the budget.");

			SeedRanks(clientId: 12, count: widened + 5);
			try
			{
				LogAssert.IsTrue(Budget(12, RankObjectId(budget), currentlyVisible: true),
					"A character already visible keeps its slot just past the budget.");
				LogAssert.IsFalse(Budget(12, RankObjectId(budget), currentlyVisible: false),
					"...but the same rank is not admitted from outside.");
				LogAssert.IsFalse(Budget(12, RankObjectId(widened), currentlyVisible: true),
					"Past the widened boundary even a visible character is dropped.");
			}
			finally
			{
				ClearRegistryRanks();
			}
		}

		/// <summary>
		/// The chicken-and-egg case. Ranking only what a viewer already observes would mean a
		/// character that is not yet an observer is never ranked, never admitted, and can never
		/// become one.
		/// </summary>
		[Test]
		public void VisibilityBudget_DoesNotDeadlockOnUnrankedOrUnrankedViewers()
		{
			SeedRanks(clientId: 13, count: 3);
			try
			{
				// A ranked viewer, an object outside its range this pass: the distance condition has
				// already decided that; the budget must not vote a second time.
				LogAssert.IsTrue(Budget(13, 999999, currentlyVisible: false),
					"An unranked object under a ranked viewer must not be rejected by the budget.");

				// A viewer with no pass at all must be deferred, not rejected.
				bool admitted = ObserverStreamingRegistry.IsWithinVisibilityBudget(
					9999, RankObjectId(0), currentlyVisible: false, out bool hasRanking);
				LogAssert.IsFalse(hasRanking,
					"A viewer with no pass must report that it has no ranking...");
				LogAssert.IsFalse(admitted, "...so the condition defers rather than admitting blind.");
			}
			finally
			{
				ClearRegistryRanks();
			}
		}

		/// <summary>Party members and the current target are pinned and cannot be evicted.</summary>
		[Test]
		public void VisibilityBudget_PinsAreNeverEvicted()
		{
			int budget = ObserverStreamingPolicy.VisibilityBudget;
			SeedRanks(clientId: 14, count: budget + 5);
			const int pinnedId = 4242;
			try
			{
				// Ranked far outside the budget, but pinned.
				RegistryRanks(14)[pinnedId] = budget + 4;
				LogAssert.IsFalse(Budget(14, pinnedId, currentlyVisible: false),
					"Sanity: without the pin this rank is excluded.");

				RegistryPins(14).Add(pinnedId);
				LogAssert.IsTrue(Budget(14, pinnedId, currentlyVisible: false),
					"A pinned character is admitted whatever its rank — a party you cannot see is a party you cannot play with.");
			}
			finally
			{
				ClearRegistryRanks();
			}
		}

		/// <summary>The engaged full-rate budget bounds what the lag-compensation exemption can cost.</summary>
		[Test]
		public void EngagedFullRateBudget_BoundsTheExemption()
		{
			int k = ObserverStreamingPolicy.EngagedFullRateBudget;
			LogAssert.IsTrue(k > 0, "Some engaged targets must keep full rate, or compensation is never exact.");
			LogAssert.IsTrue(k <= ObserverStreamingPolicy.VisibilityBudget,
				"More full-rate slots than visible characters would be unreachable.");
			LogAssert.IsTrue(ObserverStreamingPolicy.EngagedOverflowInterval >= 2,
				"Overflow must actually throttle, or the budget saves nothing.");
			LogAssert.IsTrue(ObserverStreamingPolicy.EngagedOverflowInterval <= 3,
				"Overflow beyond every 3rd tick gives back more compensation accuracy than the exemption was worth.");

			// The cost the budget bounds, at the 17 B/update figure for most of an 8 km world.
			const float bytesPerUpdate = 17f;
			const float tickRate = 30f;
			float unbounded = 30 * bytesPerUpdate * tickRate;
			float bounded = k * bytesPerUpdate * tickRate
				+ (30 - k) * bytesPerUpdate * (tickRate / ObserverStreamingPolicy.EngagedOverflowInterval);
			TestContext.WriteLine(
				$"MEASURE 30 engaged characters: {unbounded / 1024f:F1} KB/s unbounded, {bounded / 1024f:F1} KB/s with K={k}");
			LogAssert.IsTrue(bounded < unbounded * 0.75f,
				"The engaged budget must meaningfully bound the crowd case it exists for.");
		}

		private static readonly BindingFlags Priv = BindingFlags.Static | BindingFlags.NonPublic;

		private static int RankObjectId(int rank) => 10000 + rank;

		private static Dictionary<int, int> RegistryRanks(int clientId)
		{
			var map = (Dictionary<int, Dictionary<int, int>>)typeof(ObserverStreamingRegistry)
				.GetField("ranksByClientId", Priv).GetValue(null);
			if (!map.TryGetValue(clientId, out Dictionary<int, int> ranks))
			{
				ranks = new Dictionary<int, int>();
				map[clientId] = ranks;
			}
			return ranks;
		}

		private static HashSet<int> RegistryPins(int clientId)
		{
			var map = (Dictionary<int, HashSet<int>>)typeof(ObserverStreamingRegistry)
				.GetField("pinnedByClientId", Priv).GetValue(null);
			if (!map.TryGetValue(clientId, out HashSet<int> pins))
			{
				pins = new HashSet<int>();
				map[clientId] = pins;
			}
			return pins;
		}

		private static void SeedRanks(int clientId, int count)
		{
			Dictionary<int, int> ranks = RegistryRanks(clientId);
			ranks.Clear();
			RegistryPins(clientId).Clear();
			for (int i = 0; i < count; i++)
			{
				ranks[RankObjectId(i)] = i;
			}
			((HashSet<int>)typeof(ObserverStreamingRegistry).GetField("rankedClientIds", Priv)
				.GetValue(null)).Add(clientId);
		}

		private static void ClearRegistryRanks()
		{
			((Dictionary<int, Dictionary<int, int>>)typeof(ObserverStreamingRegistry)
				.GetField("ranksByClientId", Priv).GetValue(null)).Clear();
			((HashSet<int>)typeof(ObserverStreamingRegistry).GetField("rankedClientIds", Priv)
				.GetValue(null)).Clear();
			((Dictionary<int, HashSet<int>>)typeof(ObserverStreamingRegistry)
				.GetField("pinnedByClientId", Priv).GetValue(null)).Clear();
		}

		private static bool Budget(int clientId, int objectId, bool currentlyVisible)
			=> ObserverStreamingRegistry.IsWithinVisibilityBudget(clientId, objectId, currentlyVisible, out _);

		// ── Observer stack wiring ──

		/// <summary>
		/// The condition asset must actually resolve to the script. It was authored by hand, and a
		/// GUID mismatch does not fail loudly — the ScriptableObject deserialises to null, the
		/// entry in <c>_defaultConditions</c> becomes a null reference, and the visibility budget
		/// silently stops existing.
		/// </summary>
		[Test]
		public void ObserverBudgetCondition_AssetResolvesToItsScript()
		{
			string assetPath = Path.Combine(Application.dataPath, "Settings/ObserverConditions/ObserverBudgetCondition.asset");
			LogAssert.IsTrue(File.Exists(assetPath), "The budget condition asset must exist.");

			string scriptMeta = Path.Combine(Application.dataPath,
				"Scripts/Shared/Implementation/Entity/Prediction/ObserverStreaming/ObserverBudgetCondition.cs.meta");
			LogAssert.IsTrue(File.Exists(scriptMeta), "The condition script must exist.");

			string scriptGuid = GuidFromMeta(scriptMeta);
			string asset = File.ReadAllText(assetPath);
			LogAssert.IsTrue(asset.Contains($"guid: {scriptGuid}"),
				$"The asset's m_Script must point at the condition script ({scriptGuid}); a mismatch loads as null and disables the cap.");
		}

		/// <summary>The condition has to be installed, and installed LAST.</summary>
		[Test]
		public void ObserverBudgetCondition_IsInstalledLastOnTheSceneServer()
		{
			string scenePath = Path.Combine(Application.dataPath, "Scenes/Server/SceneServer.unity");
			LogAssert.IsTrue(File.Exists(scenePath), "The SceneServer scene must exist.");
			string scene = File.ReadAllText(scenePath);

			string budgetGuid = GuidFromMeta(Path.Combine(Application.dataPath,
				"Settings/ObserverConditions/ObserverBudgetCondition.asset.meta"));

			int defaults = scene.IndexOf("_defaultConditions:", StringComparison.Ordinal);
			LogAssert.IsTrue(defaults >= 0, "The ObserverManager must declare default conditions.");
			int blockEnd = scene.IndexOf("--- !u!", defaults, StringComparison.Ordinal);
			string block = scene.Substring(defaults, blockEnd - defaults);

			LogAssert.IsTrue(block.Contains(budgetGuid),
				"The budget condition must be in the SceneServer's default conditions, or nothing is capped.");

			int gridIndex = block.IndexOf("cc503f7541ebd424c94541e6a767efee", StringComparison.Ordinal);
			int budgetIndex = block.IndexOf(budgetGuid, StringComparison.Ordinal);
			LogAssert.IsTrue(gridIndex >= 0 && budgetIndex > gridIndex,
				"The budget must be evaluated after the cheap conditions — it is the only one needing a global ranking.");
		}

		/// <summary>
		/// The grid is ANDed with the distance conditions, so an accuracy below twice the largest
		/// distance silently clips it. This is what made 100 m player visibility actually 35–70 m.
		/// </summary>
		[Test]
		public void HashGridAccuracy_CannotClipTheDistanceConditions()
		{
            string scene = File.ReadAllText(Path.Combine(Application.dataPath, "Scenes/Server/SceneServer.unity"));
			Match accuracy = Regex.Match(scene, @"_accuracy: (\d+)");
			LogAssert.IsTrue(accuracy.Success, "The SceneServer must configure a HashGrid accuracy.");
			int accuracyValue = int.Parse(accuracy.Groups[1].Value);

			float largestDistance = 0f;
			foreach (string path in Directory.GetFiles(
				Path.Combine(Application.dataPath, "Settings/ObserverConditions"), "*DistanceCondition.asset"))
			{
				Match m = Regex.Match(File.ReadAllText(path), @"_maximumDistance: ([0-9.]+)");
				if (m.Success)
				{
					largestDistance = Mathf.Max(largestDistance, float.Parse(m.Groups[1].Value,
						System.Globalization.CultureInfo.InvariantCulture));
				}
			}
			LogAssert.IsTrue(largestDistance > 0f, "At least one distance condition must be authored.");

			/* Cells are ceil(accuracy/2) and "nearby" is the 3x3 block, so anything beyond
			 * accuracy on an axis is always rejected. The grid must therefore reach at least as
			 * far as the furthest distance condition, or it is the real visibility limit. */
			LogAssert.IsTrue(accuracyValue >= largestDistance * 2f,
				$"HashGrid accuracy {accuracyValue} clips a {largestDistance} m distance condition — cells are accuracy/2, so visibility would really be {accuracyValue / 2}–{accuracyValue} m depending on cell alignment.");
		}

		/// <summary>
		/// Pop-in margin must cover the ground a player crosses between observer sweeps, and the
		/// sweep slows down as population rises.
		/// </summary>
		[Test]
		public void DistanceHysteresis_CoversTravelBetweenObserverSweeps()
		{
			foreach (string path in Directory.GetFiles(
				Path.Combine(Application.dataPath, "Settings/ObserverConditions"), "*DistanceCondition.asset"))
			{
				string text = File.ReadAllText(path);
				string name = Path.GetFileName(path);
				Match hide = Regex.Match(text, @"_hideDistancePercent: ([0-9.]+)");
				LogAssert.IsTrue(hide.Success, $"{name} must author a hide percent.");
				float percent = float.Parse(hide.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);

				Match max = Regex.Match(text, @"_maximumDistance: ([0-9.]+)");
				float distance = float.Parse(max.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
				float margin = distance * percent;

				/* The sweep is min(0.5 x (1 + clients x 0.005 + objects x 0.0005), ceiling). A scene
				 * channel caps at 200 players; with ~3000 timed objects that is 1.75 s, and a 6 m/s
				 * player covers 10.5 m. The margin has to exceed that or an object can cross the
				 * whole hysteresis band between two sweeps and flicker.
				 *
				 * World items are exempt deliberately. They are static pickups whose worst failure
				 * is a briefly blinking icon, and the alternative — a 15 m item that stays observed
				 * out to 26 m — nearly doubles the number observed per player for no gameplay
				 * benefit. Everything that spawns a CHARACTER pays the margin, because there a
                 * flicker despawns audio, nameplates and a prediction object. */
				const float sweepTravel = 10.5f;
				if (name.StartsWith("WorldItem", StringComparison.Ordinal))
				{
					LogAssert.IsTrue(margin > 0f, $"{name} must still have some hysteresis.");
					continue;
				}

				LogAssert.IsTrue(margin >= sweepTravel,
					$"{name}: a {margin:F1} m hide margin is under the {sweepTravel:F1} m a player crosses between observer sweeps on a full 200-player channel — characters will flicker under exactly the load the caps exist for.");
			}
		}

		private static string GuidFromMeta(string metaPath)
		{
			foreach (string line in File.ReadAllLines(metaPath))
			{
				if (line.StartsWith("guid:", StringComparison.Ordinal))
				{
					return line.Substring(5).Trim();
				}
			}
			LogAssert.Fail($"No guid in {metaPath}");
			return null;
		}

		private sealed class MockCharacter : ICharacter
		{
			public long ID { get; set; } = 77;
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
