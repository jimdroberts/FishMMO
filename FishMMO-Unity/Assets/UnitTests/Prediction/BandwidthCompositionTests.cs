using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using FishNet.Connection;
using FishNet.Managing.Predicting;
using FishNet.Object;
using FishNet.Serializing;
using FishNet.Transporting;
using KinematicCharacterController;
using UnityEngine;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The bandwidth composition map: every message that originates in <c>Entity/Prediction</c>,
	/// measured with the production serializers and emitted as one machine-readable record.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Payload bytes are real writer output. Header bytes are written with the same
	/// <see cref="Writer"/> calls FishNet's own <c>CreateRpc</c>, <c>SendStateUpdate</c> and
	/// <c>WriteBroadcast</c> make, so they are measured rather than quoted. Rates are design
	/// parameters — tick rate, LOD bands, push intervals, cast cadence — and are emitted
	/// alongside the bytes so a calculator can vary them.
	/// </para>
	/// <para>
	/// The <c>COMPOSITION_JSON</c> line is the contract: the bandwidth calculator artifact is
	/// generated from it, so a serializer change that moves a figure moves the graph.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class BandwidthCompositionTests
	{
		private const int TickRate = 30;
		private const int RedundancyCount = 3; // PredictionManager: _stateInterpolation(2) + 1

		private static readonly Dictionary<string, object> Composition = new Dictionary<string, object>();

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
			foreach (Type t in serializerTypes)
			{
				MethodInfo register = t.GetMethod("RegisterSerializers", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
				LogAssert.IsNotNull(register, $"{t.Name} must expose RegisterSerializers.");
				register.Invoke(null, null);
			}
			Composition.Clear();
		}

		[OneTimeTearDown]
		public void EmitComposition()
		{
			if (Composition.Count == 0) return;
			StringBuilder sb = new StringBuilder();
			sb.Append("COMPOSITION_JSON {");
			bool first = true;
			foreach (KeyValuePair<string, object> kv in Composition)
			{
				if (!first) sb.Append(',');
				first = false;
				sb.Append('"').Append(kv.Key).Append("\":").Append(Convert.ToString(kv.Value, System.Globalization.CultureInfo.InvariantCulture));
			}
			sb.Append('}');
			TestContext.WriteLine(sb.ToString());
		}

		// ── helpers ──────────────────────────────────────────────────────────

		private static int Bytes(Action<Writer> write)
		{
			Writer w = new Writer();
			write(w);
			return w.Length;
		}

		private static void Record(string key, object value)
		{
			Composition[key] = value;
			TestContext.WriteLine($"MEASURE {key} = {value}");
		}

		// ── 1. Headers, written the way FishNet writes them ─────────────────

		/// <summary>
		/// Per-message and per-packet framing, measured with the writer calls FishNet uses.
		/// </summary>
		[Test]
		public void Headers()
		{
			// CreateRpc, unreliable: PacketId (2, unpacked) + NetworkObject id (signed packed
			// whole) + component index (1) + rpc hash (1 byte while a behaviour has < 256 rpcs).
			int rpcUnreliableSmallId = Bytes(w => { w.WriteUInt16Unpacked((ushort)PacketId.Replicate); w.WriteSignedPackedWhole(40); w.WriteUInt8Unpacked(3); w.WriteUInt8Unpacked(1); });
			int rpcUnreliableLargeId = Bytes(w => { w.WriteUInt16Unpacked((ushort)PacketId.Replicate); w.WriteSignedPackedWhole(5000); w.WriteUInt8Unpacked(3); w.WriteUInt8Unpacked(1); });
			// Reliable adds the method length (packed int).
			int rpcReliableSmallId = Bytes(w => { w.WriteUInt16Unpacked((ushort)PacketId.Reconcile); w.WriteSignedPackedWhole(40); w.WriteUInt8Unpacked(3); w.WriteInt32(26); w.WriteUInt8Unpacked(1); });
			// Replicate RPC body prefix: queued/run tick (WriteTickUnpacked = 4).
			int replicateTickPrefix = Bytes(w => w.WriteTickUnpacked(123456));
			// StateUpdate datagram header: PacketId + tick + length, all unpacked = 2 + 4 + 4.
			int stateUpdateHeader = Bytes(w => { w.WriteUInt16Unpacked((ushort)PacketId.StateUpdate); w.WriteTickUnpacked(123456); w.WriteInt32Unpacked(200); });
			// Reconcile inside the state packet is an RPC with a length (rpcChannel is forced Reliable for framing).
			int reconcileRpcHeader = rpcReliableSmallId;
			// Broadcast: PacketId (2) + type hash (2) + length (packed int).
			int broadcastHeaderSmall = Bytes(w => { w.WriteUInt16Unpacked((ushort)PacketId.Broadcast); w.WriteUInt16(0xABCD); w.WriteInt32(22); });
			int broadcastHeaderLarge = Bytes(w => { w.WriteUInt16Unpacked((ushort)PacketId.Broadcast); w.WriteUInt16(0xABCD); w.WriteInt32(300); });

			Record("hdr.rpcUnreliable", rpcUnreliableSmallId);
			Record("hdr.rpcUnreliableLargeObjectId", rpcUnreliableLargeId);
			Record("hdr.rpcReliable", rpcReliableSmallId);
			Record("hdr.replicateTickPrefix", replicateTickPrefix);
			Record("hdr.stateUpdatePerTick", stateUpdateHeader);
			Record("hdr.reconcileRpc", reconcileRpcHeader);
			Record("hdr.broadcast", broadcastHeaderSmall);
			Record("hdr.broadcastLarge", broadcastHeaderLarge);
			// Transport framing, once per datagram / packet, from the WebTransport constants.
			Record("hdr.ipUdp", 28);
			Record("hdr.quicDatagram", 50);
			Record("hdr.quicStream", 35);
			Record("hdr.datagramMtu", 1150);

			LogAssert.IsTrue(stateUpdateHeader == 10, $"StateUpdate header must be 2+4+4 = 10, measured {stateUpdateHeader}");
			LogAssert.IsTrue(rpcUnreliableSmallId <= 10, "Unreliable RPC header must not exceed MAXIMUM_RPC_HEADER_SIZE.");
		}

		// ── 2. Replicate: owner → server, every tick ─────────────────────────

		private static CharacterReplicateData Input(float fwd, float right, int flags, Vector3 aim, int activation, long queued, uint tick)
		{
			CharacterReplicateData d = new CharacterReplicateData
			{
				MoveAxisForward = MoveAxisCompression.Quantize(fwd),
				MoveAxisRight = MoveAxisCompression.Quantize(right),
				MoveFlags = flags,
				AimDirection = AimDirectionCompression.Quantize(aim),
				ActivationFlags = activation,
				QueuedAbilityID = queued,
			};
			d.SetTick(tick);
			return d;
		}

		/// <summary>Models Writer.WriteDeltaReplicate: count, entry 0 absolute, deltas after, channel byte each.</summary>
		private static int ReplicatePacket(CharacterReplicateData[] entries)
		{
			return Bytes(w =>
			{
				w.WriteUInt8Unpacked((byte)entries.Length);
				for (int i = 0; i < entries.Length; i++)
				{
					if (i == 0) w.Write(entries[0]);
					else w.WriteDelta(entries[i - 1], entries[i], DeltaSerializerOption.RootSerialize);
					w.WriteUInt8Unpacked(0);
				}
			});
		}

		[Test]
		public void Replicate_OwnerToServer()
		{
			int actual = 1 << (int)KCCMoveFlags.IsActualData;
			int sprint = actual | (1 << (int)KCCMoveFlags.Sprint);
			Vector3 aim = new Vector3(0.3f, -0.1f, 0.95f);

			CharacterReplicateData[] idle =
			{
				Input(0, 0, actual, aim, 1, 0, 100), Input(0, 0, actual, aim, 1, 0, 101), Input(0, 0, actual, aim, 1, 0, 102),
			};
			CharacterReplicateData[] walking =
			{
				Input(1, 0, actual, aim, 1, 0, 100), Input(1, 0, actual, aim, 1, 0, 101), Input(1, 0.2f, actual, aim, 1, 0, 102),
			};
			CharacterReplicateData[] turning =
			{
				Input(1, 0, sprint, Quaternion.Euler(0, 40f, 0) * aim, 1, 0, 100),
				Input(1, 0, sprint, Quaternion.Euler(0, 43f, 0) * aim, 1, 0, 101),
				Input(1, 0, sprint, Quaternion.Euler(0, 46f, 0) * aim, 1, 0, 102),
			};
			CharacterReplicateData[] casting =
			{
				Input(0, 0, actual, aim, 1, 0, 100),
				Input(0, 0, actual, aim, 1 | (1 << (int)AbilityActivationFlags.IsHeld), 8_842_001_337L, 101),
				Input(0, 0, actual, aim, 1, 0, 102),
			};

			Record("rep.packetIdle", ReplicatePacket(idle));
			Record("rep.packetWalking", ReplicatePacket(walking));
			Record("rep.packetTurning", ReplicatePacket(turning));
			Record("rep.packetCasting", ReplicatePacket(casting));
			Record("rep.entryAbsolute", Bytes(w => w.Write(walking[0])));
			Record("rep.redundancy", RedundancyCount);
			Record("rep.hz", TickRate);
		}

		// ── 3. Reconcile: server → owner, every tick, inside the StateUpdate datagram ──

		private static CharacterReconcileData BaseReconcile()
		{
			CharacterReconcileData d = default;
			d.MotorState = default;
			d.MotorState.Position = new Vector3(112.5f, 30.9f, -47.25f);
			d.MotorState.Rotation = Quaternion.Euler(0, 40f, 0);
			d.MotorState.GroundingStatus = default;
			d.MotorState.GroundingStatus.FoundAnyGround = true;
			d.MotorState.GroundingStatus.IsStableOnGround = true;
			d.MotorState.GroundingStatus.GroundNormal = Vector3.up;
			d.MotorState.GroundingStatus.InnerGroundNormal = Vector3.up;
			d.MotorState.GroundingStatus.OuterGroundNormal = Vector3.up;
			d.Seed = 4242;
			d.PackedFlagsAndSlot = CharacterReconcileData.Pack(0, -1);
			d.ResourceState = new CharacterAttributeResourceState { MaxHealth = 1200, Health = 1200f, MaxMana = 800, Mana = 800f, MaxStamina = 400, Stamina = 400f, NextRegenTick = 900 };
			d.RngS0 = 0xDEADBEEF; d.RngS1 = 0x12345678; d.RngS2 = 0x0BADF00D; d.RngS3 = 0xFEEDFACE;
			d.Cooldowns = new[] { new CooldownReconcileEntry { AbilityID = 42, StartTick = 100, DurationTicks = 60 } };
			d.Buffs = new[] { new BuffReconcileEntry { TemplateID = 3, ExpiryTick = 500, NextTickTick = 20, Stacks = 1, TickCount = 4, CumulativeTickMultiplier = 1 } };
			d.Equipment = new[] { new EquipmentReconcileEntry { TemplateID = 5, Slot = 1, Seed = 77, InstanceID = 900 }, new EquipmentReconcileEntry { TemplateID = 6, Slot = 2, Seed = 78, InstanceID = 901 } };
			d.Attributes = new AttributeReconcileEntry[24];
			for (int i = 0; i < d.Attributes.Length; i++) d.Attributes[i] = new AttributeReconcileEntry { TemplateID = 100 + i, Value = 10 + i, ExternalModifier = 0 };
			d.Sequence = 7;
			return d;
		}

		private static CharacterReconcileData Next(CharacterReconcileData prev)
		{
			CharacterReconcileData n = prev;
			n.Sequence = unchecked((byte)(prev.Sequence + 1));
			return n;
		}

		[Test]
		public void Reconcile_ServerToOwner()
		{
			CharacterReconcileData prev = BaseReconcile();

			// Idle: nothing but the regen tick advancing (it changes only every regen pulse).
			CharacterReconcileData idle = Next(prev);
			// Walking: position, velocity, rotation.
			CharacterReconcileData walking = Next(prev);
			walking.MotorState.Position += new Vector3(0.12f, 0f, 0.04f);
			walking.MotorState.BaseVelocity = new Vector3(3.6f, 0f, 1.2f);
			walking.MotorState.Rotation = Quaternion.Euler(0f, 42f, 0f);
			// Combat: walking + health/stamina changing + a cast in progress + rng advanced.
			CharacterReconcileData combat = Next(walking);
			combat.Sequence = Next(prev).Sequence;
			combat.ResourceState.Health -= 37f;
			combat.ResourceState.Stamina -= 4.2f;
			combat.AbilityID = 8_842_001_337L;
			combat.RemainingTicks = 11;
			combat.Seed = 99181;
			combat.RngS0 ^= 0x5555; combat.RngS1 ^= 0x3333; combat.RngS2 ^= 0x0F0F; combat.RngS3 ^= 0xF0F0;
			combat.Cooldowns = new[] { new CooldownReconcileEntry { AbilityID = 42, StartTick = 100, DurationTicks = 60 }, new CooldownReconcileEntry { AbilityID = 8_842_001_337L, StartTick = 118, DurationTicks = 30 } };

			Record("rec.deltaIdle", Bytes(w => w.WriteDelta(prev, idle, DeltaSerializerOption.RootSerialize)));
			Record("rec.deltaWalking", Bytes(w => w.WriteDelta(prev, walking, DeltaSerializerOption.RootSerialize)));
			Record("rec.deltaCombat", Bytes(w => w.WriteDelta(prev, combat, DeltaSerializerOption.RootSerialize)));
			Record("rec.absolute", Bytes(w => w.WriteDelta(prev, walking, DeltaSerializerOption.FullSerialize)));
			Record("rec.absoluteHz", 1.0);
			Record("rec.hz", TickRate);
			Record("rec.attributeCount", prev.Attributes.Length);
		}

		// ── 4. NetworkTransform: server → each observer, per LOD band ────────

		private static int MeasureNetworkTransform(int changedMask)
		{
			GameObject go = new GameObject("NtMeasure");
			try
			{
				go.transform.localPosition = new Vector3(112.61f, 30.9f, -47.21f);
				go.transform.localRotation = Quaternion.Euler(0f, 22f, 0f);
				FishNet.Component.Transforming.NetworkTransform nt = go.AddComponent<FishNet.Component.Transforming.NetworkTransform>();
				Type ntType = typeof(FishNet.Component.Transforming.NetworkTransform);
				ntType.GetField("_cachedTransform", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(nt, go.transform);
				Type changedDelta = ntType.GetNestedType("ChangedDelta", BindingFlags.NonPublic);
				MethodInfo serialize = ntType.GetMethod("SerializeChanged", BindingFlags.Instance | BindingFlags.NonPublic);
				PooledWriter writer = WriterPool.Retrieve();
				try
				{
					serialize.Invoke(nt, new[] { Enum.ToObject(changedDelta, changedMask), writer });
					return writer.Length;
				}
				finally { writer.Store(); }
			}
			finally { UnityEngine.Object.DestroyImmediate(go); }
		}

		[Test]
		public void NetworkTransform_ServerToObservers()
		{
			// ChangedDelta bits: PositionX=1, PositionY=2, PositionZ=4, Rotation=8.
			Record("nt.moveAndTurn", MeasureNetworkTransform(1 | 2 | 4 | 8));
			Record("nt.moveOnly", MeasureNetworkTransform(1 | 4));
			Record("nt.turnOnly", MeasureNetworkTransform(8));
			Record("nt.lodNear", 1);   // ticks between sends, 0-20 m
			Record("nt.lodMid", 3);    // 20-40 m
			Record("nt.lodFar", 6);    // 40 m+
			Record("nt.hz", TickRate);
		}

		// ── 5. Broadcasts: server → observers, event-driven ───────────────────

		/* FishNet's IL post-processor generates a writer per IBroadcast struct in a player build;
		 * under EditMode those do not exist, so each message is written here field by field with
		 * the same default writers codegen emits (packed ints/longs/uints, 4-byte floats, 12-byte
		 * Vector3, 1-byte bool/byte, length-prefixed arrays), in declaration order. */
		private static void WriteResources(Writer w, CharacterResourcesBroadcast m)
		{ w.WriteInt32(m.CharacterObjectID); w.WriteInt32(m.Health); w.WriteInt32(m.MaxHealth); w.WriteInt32(m.Mana); w.WriteInt32(m.MaxMana); w.WriteInt32(m.Stamina); w.WriteInt32(m.MaxStamina); }
		/* The activation broadcast has a hand-written, mode-shaped wire format
		 * (AbilityObserverBroadcastSerializers). This used to model it field by field and
		 * serialised seven of eleven fields -- no header byte, no ServerTick, and never the spawn
		 * pose -- so it under-reported the real message. Call the production writer instead. */
		private static void WriteActivation(Writer w, AbilityActivatedBroadcast m)
		{ w.WriteAbilityActivatedBroadcast(m); }
		private static void WriteBuffs(Writer w, CharacterBuffsBroadcast m)
		{ w.WriteInt32(m.CharacterObjectID); w.WriteInt32(m.Buffs.Length); foreach (ObservedBuffEntry e in m.Buffs) { w.WriteInt32(e.TemplateID); w.WriteInt32(e.Stacks); w.WriteSingle(e.RemainingSeconds); w.WriteSingle(e.TotalSeconds); } }

		[Test]
		public void Broadcasts_ServerToObservers()
		{
			int resources = Bytes(w => WriteResources(w, new CharacterResourcesBroadcast { CharacterObjectID = 40, Health = 812, MaxHealth = 1200, Mana = 240, MaxMana = 800, Stamina = 310, MaxStamina = 400 }));
			int resourcesFloatForm = Bytes(w => { w.WriteInt32(40); w.WriteSingle(812f); w.WriteInt32(1200); w.WriteSingle(240f); w.WriteInt32(800); w.WriteSingle(310f); w.WriteInt32(400); });
			AbilityActivatedBroadcast activationMessage = new AbilityActivatedBroadcast
			{
				CasterObjectID = 40, AbilityID = 8_842_001_337L, Seed = -1_713_468_379,
				SpawnTick = 123_450, ServerTick = 123_456, TargetObjectID = 77,
				SpawnMode = (byte)AbilitySpawnTarget.Camera,
				AimOrigin = new Vector3(112.5f, 32.6f, -47.2f),
				PackedAimDirection = AimDirectionCompression.Encode(new Vector3(0.3f, -0.1f, 0.95f)),
				SpawnPosition = new Vector3(113.1f, 32.6f, -46.4f),
				SpawnRotation = Quaternion.Euler(12f, 200f, 0f),
			};
			int activation = Bytes(w => WriteActivation(w, activationMessage));
			AbilityActivatedBroadcast posedMessage = activationMessage;
			posedMessage.SpawnMode = (byte)AbilitySpawnTarget.Forward;
			int activationPosed = Bytes(w => WriteActivation(w, posedMessage));
			int buffs1 = Bytes(w => WriteBuffs(w, new CharacterBuffsBroadcast { CharacterObjectID = 40, Buffs = new[] { new ObservedBuffEntry { TemplateID = 200, Stacks = 1, RemainingSeconds = 12.4f, TotalSeconds = 30f } } }));
			int buffs4 = Bytes(w => WriteBuffs(w, new CharacterBuffsBroadcast { CharacterObjectID = 40, Buffs = new[] { new ObservedBuffEntry { TemplateID = 200, Stacks = 1, RemainingSeconds = 12.4f, TotalSeconds = 30f }, new ObservedBuffEntry { TemplateID = 201, Stacks = 3, RemainingSeconds = 5f, TotalSeconds = 10f }, new ObservedBuffEntry { TemplateID = 202, Stacks = 0, RemainingSeconds = 0f, TotalSeconds = 0f }, new ObservedBuffEntry { TemplateID = 203, Stacks = 2, RemainingSeconds = 44f, TotalSeconds = 60f } } }));
			int death = Bytes(w => { w.WriteInt32(40); w.WriteBoolean(true); });
			int mode = Bytes(w => { w.WriteInt32(40); w.WriteUInt8Unpacked(1); });

			Record("bc.resources", resources);
			Record("bc.resourcesFloatForm", resourcesFloatForm);
			Record("bc.resourcesHzMax", TickRate / 6.0); // observedResourcePushInterval = 6
			Record("bc.activationCamera", activation);
			Record("bc.activationPosed", activationPosed);
			Record("bc.buffs1", buffs1);
			Record("bc.buffs4", buffs4);
			Record("bc.death", death);
			Record("bc.mode", mode);

			LogAssert.IsTrue(resources < resourcesFloatForm, $"Whole-unit resource broadcast ({resources} B) must beat the float form ({resourcesFloatForm} B).");
		}

		// ── 6. Spawn payloads: once per observer add ──────────────────────────

		[Test]
		public void SpawnPayloads_PerObserverAdd()
		{
			GameObject go = new GameObject("PayloadProbe");
			List<UnityEngine.Object> assets = new List<UnityEngine.Object>();
			try
			{
				MockCharacter character = new MockCharacter(9);

				// Attributes: a realistic sheet — 24 non-resource + 3 resource attributes.
				CharacterAttributeController attributes = go.AddComponent<CharacterAttributeController>();
				attributes.InitializeOnce(character);
				for (int i = 0; i < 24; i++)
				{
					CharacterAttributeTemplate t = ScriptableObject.CreateInstance<CharacterAttributeTemplate>();
					t.name = $"Comp_Attr_{i}"; t.InitialValue = 10 + i; t.AddToCache(t.name); assets.Add(t);
					attributes.AddAttribute(new CharacterAttribute(attributes, t.ID, t.InitialValue, 0));
				}
				for (int i = 0; i < 3; i++)
				{
					CharacterAttributeTemplate t = ScriptableObject.CreateInstance<CharacterAttributeTemplate>();
					t.name = $"Comp_Res_{i}"; t.InitialValue = 1000; t.IsResourceAttribute = true; t.AddToCache(t.name); assets.Add(t);
					attributes.AddResourceAttribute(new CharacterResourceAttribute(attributes, t.ID, 1000, 1000, 0));
				}
				int attributePayload = Bytes(w => attributes.WritePayload(null, w));
				Record("spawn.attributes", attributePayload);

				// Abilities: 12 known abilities with 2 crafted events each, plus (owner) cooldowns.
				AbilityController abilityController = go.AddComponent<AbilityController>();
				// AddComponent never runs Awake in edit mode: build the collections OnAwake would,
				// and pre-seed the generator so WritePayload does not consult IsServerStarted on an
				// unspawned object (every NetworkBehaviour convenience property NREs there).
				abilityController.OnAwake();
				SetPrivate(abilityController, "abilitySeedGenerator", new DeterministicRNG(1));
				abilityController.InitializeOnce(character);
				for (int i = 0; i < 12; i++)
				{
					AbilityTemplate t = ScriptableObject.CreateInstance<CompositionAbilityTemplate>();
					t.name = $"Comp_Ability_{i}"; t.AddToCache(t.name); assets.Add(t);
					abilityController.LearnAbility(new Ability(10_000 + i, t));
				}
				Record("spawn.abilities12", Bytes(w => abilityController.WritePayload(null, w)));

				// Buffs: 3 visible (non-owner view).
				BuffController buffs = go.AddComponent<BuffController>();
				SetPrivate(buffs, "tickDelta", 1f / 30f); SetPrivate(buffs, "lastReplicateTick", 100u); SetPrivate(buffs, "hasSeenFirstReplicate", true);
				buffs.InitializeOnce(character);
				for (int i = 0; i < 3; i++)
				{
					CompositionBuffTemplate t = ScriptableObject.CreateInstance<CompositionBuffTemplate>();
					t.name = $"Comp_Buff_{i}"; t.Duration = 30f; t.AddToCache(t.name); assets.Add(t);
					buffs.Apply(t, new PredictionTick(100u));
				}
				Record("spawn.buffs3", Bytes(w => buffs.WritePayload(null, w)));

				// Equipment: 6 filled slots.
				EquipmentController equipment = go.AddComponent<EquipmentController>();
				equipment.InitializeOnce(character);
				int equipmentPayload = Bytes(w => equipment.WritePayload(null, w));
				Record("spawn.equipmentEmpty", equipmentPayload);
				Record("spawn.equipmentPerItem", 8 + 4 + 4 + 4 + 4); // id, template, slot, seed, stack — packed ints; upper bound
			}
			finally
			{
				foreach (UnityEngine.Object a in assets)
				{
					if (a is ICachedObject c) c.RemoveFromCache();
					UnityEngine.Object.DestroyImmediate(a);
				}
				UnityEngine.Object.DestroyImmediate(go);
			}
		}

		// ── 7. Still-forwarded: KCCPlatform (estimate — codegen types) ───────

		[Test]
		public void Platform_Forwarded()
		{
			// ReplicateData is tick-only (count + channel); ReconcileData is Vector3 + byte.
			int platformReplicate = Bytes(w => { w.WriteUInt8Unpacked(1); w.WriteUInt8Unpacked(0); });
			int platformReconcile = Bytes(w => { w.WriteVector3(new Vector3(10.5f, 2f, -3.25f)); w.WriteUInt8Unpacked(1); });
			Record("platform.replicate", platformReplicate);
			Record("platform.reconcile", platformReconcile);
		}

		// ── support ──────────────────────────────────────────────────────────

		private static void SetPrivate<T>(object o, string field, T value)
			=> o.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(o, value);

		private sealed class CompositionAbilityTemplate : AbilityTemplate { }
		private sealed class CompositionBuffTemplate : BaseBuffTemplate
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
			public void RegisterCharacterBehaviour(ICharacterBehaviour b) { }
			public void UnregisterCharacterBehaviour(ICharacterBehaviour b) { }
			public bool TryGet<T>(out T control) where T : class, ICharacterBehaviour { control = null; return false; }
			public void Invoke(List<Trigger> triggers, EventData eventData) { }
		}
	}
}
