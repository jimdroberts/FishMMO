using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using FishNet.Connection;
using FishNet.Managing.Predicting;
using FishNet.Object;
using FishNet.Serializing;
using KinematicCharacterController;
using UnityEngine;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Measures every message the character prediction pipeline puts on the wire, in both observer
	/// synchronisation modes, with the production serializers.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why a new fixture rather than extending the old ones.</b> Several existing benchmarks
	/// model a message by hand (<c>BandwidthCompositionTests.WriteActivation</c> serialised seven of
	/// <c>AbilityActivatedBroadcast</c>'s eleven fields and predated the mode-shaped hand-written
	/// format entirely), and the newer observer broadcasts had no coverage at all. Everything here
	/// goes through the real writer: a custom serializer is called directly where one exists, and
	/// where FishNet's IL post-processor would generate one — which does not run under EditMode —
	/// the generated shape is reproduced field for field in declaration order using the same
	/// <see cref="Writer"/> primitives codegen emits, and the model is cross-checked against a real
	/// call wherever a real call is reachable.
	/// </para>
	/// <para>
	/// <b>Serializer registration is mandatory.</b> The delta serializers register from
	/// <c>[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]</c>, which never fires in EditMode.
	/// Without the reflection call in <see cref="RegisterProductionSerializers"/>,
	/// <c>GenericDeltaWriter&lt;T&gt;.Write</c> is null, <c>WriteDelta</c> writes nothing, and every
	/// measurement is vacuously zero while the test still passes. <see cref="Guard_SerializersAreLive"/>
	/// asserts that did not happen.
	/// </para>
	/// <para>
	/// Every figure is emitted as a <c>MEASURE key = value</c> line so a run's results XML can be
	/// harvested mechanically.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class PredictionModeBandwidthMapTests
	{
		// ── Design parameters, from the project's own configuration ──────────
		// TickRate 30 and stateInterpolation 2: Assets/Scenes/Server/SceneServer.unity.
		// RedundancyCount = stateInterpolation + 1: PredictionManager.cs:234.
		private const int TickRate = 30;
		private const int RedundancyCount = 3;

		// WebTransport.cs:50 — QUIC guarantees 1200 B; 1150 B is what the datagram may carry.
		private const int QuicDatagramTotal = 1200;
		private const int DatagramMtu = 1150;
		private const int IpUdpBytes = 28;

		private static readonly Dictionary<string, object> Measured = new Dictionary<string, object>();

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
				MethodInfo register = t.GetMethod("RegisterSerializers",
					BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
				LogAssert.IsNotNull(register, $"{t.Name} must expose RegisterSerializers.");
				register.Invoke(null, null);
			}
			Measured.Clear();
		}

		// ── helpers ──────────────────────────────────────────────────────────

		private static int Bytes(Action<Writer> write)
		{
			Writer w = new Writer();
			write(w);
			return w.Length;
		}

		private static int Record(string key, int value)
		{
			Measured[key] = value;
			TestContext.WriteLine($"MEASURE {key} = {value}");
			return value;
		}

		private static double Record(string key, double value)
		{
			Measured[key] = value;
			TestContext.WriteLine($"MEASURE {key} = {value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}");
			return value;
		}

		/// <summary>
		/// Proves the delta writers are actually registered before anything trusts a zero.
		/// </summary>
		[Test]
		public void Guard_SerializersAreLive()
		{
			CharacterReconcileData a = BaseReconcile();
			CharacterReconcileData b = a;
			b.Sequence = unchecked((byte)(a.Sequence + 1));
			b.MotorState.Position += new Vector3(1f, 0f, 0f);

			int delta = Bytes(w => w.WriteDelta(a, b, DeltaSerializerOption.RootSerialize));
			int absolute = Bytes(w => w.WriteDelta(a, b, DeltaSerializerOption.FullSerialize));

			LogAssert.IsTrue(delta > 0, "CharacterReconcileData delta wrote zero bytes — GenericDeltaWriter is not registered.");
			LogAssert.IsTrue(absolute > delta, "The absolute snapshot must be larger than a one-field delta.");

			CharacterReplicateData r0 = Input(1f, 0f, 1, Vector3.forward, 1, 0, 100);
			CharacterReplicateData r1 = Input(1f, 0.5f, 1, Vector3.forward, 1, 0, 101);
			LogAssert.IsTrue(Bytes(w => w.WriteDelta(r0, r1, DeltaSerializerOption.RootSerialize)) > 0,
				"CharacterReplicateData delta wrote zero bytes — GenericDeltaWriter is not registered.");
		}

		// ── 1. Framing ───────────────────────────────────────────────────────

		/// <summary>
		/// Per-message and per-datagram framing, written with the calls FishNet itself makes.
		/// </summary>
		[Test]
		public void Framing()
		{
			// CreateRpc: PacketId (2, unpacked) + NetworkObject id (signed packed) + component
			// index (1) + rpc hash (1 while a behaviour declares < 256 rpcs).
			int rpcUnreliable = Record("hdr.rpcUnreliable", Bytes(w =>
			{
				w.WriteUInt16Unpacked((ushort)FishNet.Transporting.PacketId.Replicate);
				w.WriteSignedPackedWhole(40); w.WriteUInt8Unpacked(3); w.WriteUInt8Unpacked(1);
			}));
			// Reliable adds the method length.
			Record("hdr.rpcReliable", Bytes(w =>
			{
				w.WriteUInt16Unpacked((ushort)FishNet.Transporting.PacketId.Reconcile);
				w.WriteSignedPackedWhole(40); w.WriteUInt8Unpacked(3); w.WriteInt32(48); w.WriteUInt8Unpacked(1);
			}));
			// Replicate RPC body prefix: the run tick, unpacked.
			Record("hdr.replicateTickPrefix", Bytes(w => w.WriteTickUnpacked(123456)));
			// StateUpdate datagram header, once per tick regardless of how many objects ride in it.
			int stateUpdate = Record("hdr.stateUpdatePerTick", Bytes(w =>
			{
				w.WriteUInt16Unpacked((ushort)FishNet.Transporting.PacketId.StateUpdate);
				w.WriteTickUnpacked(123456); w.WriteInt32Unpacked(200);
			}));
			/* Broadcast framing, exactly as BroadcastsSerializers.WriteBroadcast writes it:
			 * PacketId unpacked (2) + the type's stable 16-bit hash through WriteUInt16, which is
			 * packed (3 B for a hash that uses the high bits) + the payload length, packed. */
			Record("hdr.broadcastSmall", Bytes(w =>
			{
				w.WriteUInt16Unpacked((ushort)FishNet.Transporting.PacketId.Broadcast);
				w.WriteUInt16(0xABCD); w.WriteInt32(22);
			}));
			Record("hdr.broadcastLarge", Bytes(w =>
			{
				w.WriteUInt16Unpacked((ushort)FishNet.Transporting.PacketId.Broadcast);
				w.WriteUInt16(0xABCD); w.WriteInt32(400);
			}));

			Record("hdr.ipUdp", IpUdpBytes);
			Record("hdr.quicDatagramOverhead", QuicDatagramTotal - DatagramMtu);
			Record("hdr.datagramMtu", DatagramMtu);

			LogAssert.AreEqual(10, stateUpdate, "StateUpdate header is PacketId(2) + tick(4) + length(4).");
			LogAssert.IsTrue(rpcUnreliable <= 10, "Unreliable RPC header must fit MAXIMUM_RPC_HEADER_SIZE.");
		}

		// ── 2. Replicate — owner → server always; server → observers in Mode B ──

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

		/// <summary>
		/// Mirrors <c>Writer.WriteDeltaReplicate</c>: count byte, entry 0 absolute, the rest deltas
		/// against the entry before them within the packet, each followed by its channel byte.
		/// </summary>
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
		public void Replicate_Packets()
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
				Input(1, 0, actual, aim, 1, 0, 100),
				Input(1, 0, actual, Quaternion.Euler(0, 1.2f, 0) * aim, 1, 0, 101),
				Input(1, 0.2f, actual, Quaternion.Euler(0, 2.4f, 0) * aim, 1, 0, 102),
			};
			CharacterReplicateData[] combat =
			{
				Input(1, 0, sprint, Quaternion.Euler(0, 40f, 0) * aim, 1, 0, 100),
				Input(1, 0, sprint, Quaternion.Euler(0, 43f, 0) * aim,
					1 | (1 << (int)AbilityActivationFlags.IsHeld), 8_842_001_337L, 101),
				Input(1, 0.4f, sprint, Quaternion.Euler(0, 46f, 0) * aim, 1, 0, 102),
			};
			CharacterReplicateData[] burst =
			{
				Input(1, -1, sprint | (1 << (int)KCCMoveFlags.Jump), Quaternion.Euler(-20f, 40f, 0) * aim,
					1 | (1 << (int)AbilityActivationFlags.IsHeld), 8_842_001_337L, 100),
				Input(-1, 1, sprint, Quaternion.Euler(25f, 100f, 0) * aim, 1, 991_002_003L, 101),
				Input(0, -1, actual, Quaternion.Euler(-40f, 200f, 0) * aim, 1, 0, 102),
			};

			Record("rep.packetIdle", ReplicatePacket(idle));
			Record("rep.packetWalking", ReplicatePacket(walking));
			Record("rep.packetCombat", ReplicatePacket(combat));
			Record("rep.packetBurst", ReplicatePacket(burst));
			Record("rep.entryAbsolute", Bytes(w => w.Write(walking[0])));
			Record("rep.entryDeltaWalking", Bytes(w => w.WriteDelta(walking[0], walking[1], DeltaSerializerOption.RootSerialize)));
			Record("rep.entryDeltaIdle", Bytes(w => w.WriteDelta(idle[0], idle[1], DeltaSerializerOption.RootSerialize)));
			Record("rep.redundancy", RedundancyCount);
			Record("rep.hz", TickRate);
		}

		// ── 3. Reconcile — server → owner always; server → every observer in Mode B ──

		private static CharacterReconcileData BaseReconcile()
		{
			CharacterReconcileData d = default;
			d.MotorState = default;
			d.MotorState.Position = new Vector3(112.5f, 30.9f, -47.25f);
			d.MotorState.Rotation = Quaternion.Euler(0f, 40f, 0f);
			d.MotorState.GroundingStatus = default;
			d.MotorState.GroundingStatus.FoundAnyGround = true;
			d.MotorState.GroundingStatus.IsStableOnGround = true;
			d.MotorState.GroundingStatus.GroundNormal = Vector3.up;
			d.MotorState.GroundingStatus.InnerGroundNormal = Vector3.up;
			d.MotorState.GroundingStatus.OuterGroundNormal = Vector3.up;
			d.Seed = 4242;
			d.PackedFlagsAndSlot = CharacterReconcileData.Pack(0, -1);
			d.ResourceState = new CharacterAttributeResourceState
			{
				MaxHealth = 1200, Health = 1200f, MaxMana = 800, Mana = 800f,
				MaxStamina = 400, Stamina = 400f, NextRegenTick = 900,
			};
			d.RngS0 = 0xDEADBEEF; d.RngS1 = 0x12345678; d.RngS2 = 0x0BADF00D; d.RngS3 = 0xFEEDFACE;
			d.Cooldowns = new[] { new CooldownReconcileEntry { AbilityID = 42, StartTick = 100, DurationTicks = 60 } };
			d.Buffs = new[] { new BuffReconcileEntry { TemplateID = 3, ExpiryTick = 500, NextTickTick = 20, Stacks = 1, TickCount = 4, CumulativeTickMultiplier = 1 } };
			d.Equipment = new[]
			{
				new EquipmentReconcileEntry { TemplateID = 5, Slot = 1, Seed = 77, InstanceID = 900 },
				new EquipmentReconcileEntry { TemplateID = 6, Slot = 2, Seed = 78, InstanceID = 901 },
			};
			d.Attributes = new AttributeReconcileEntry[24];
			for (int i = 0; i < d.Attributes.Length; i++)
			{
				d.Attributes[i] = new AttributeReconcileEntry { TemplateID = 100 + i, Value = 10 + i, ExternalModifier = 0 };
			}
			d.Sequence = 7;
			return d;
		}

		/// <summary>Clones the arrays so a mutation does not alias the baseline's reference.</summary>
		private static CharacterReconcileData NextTick(CharacterReconcileData prev)
		{
			CharacterReconcileData n = prev;
			n.Sequence = unchecked((byte)(prev.Sequence + 1));
			return n;
		}

		private static CharacterReconcileData Detach(CharacterReconcileData d)
		{
			d.Cooldowns = (CooldownReconcileEntry[])d.Cooldowns?.Clone();
			d.Buffs = (BuffReconcileEntry[])d.Buffs?.Clone();
			d.Equipment = (EquipmentReconcileEntry[])d.Equipment?.Clone();
			d.Attributes = (AttributeReconcileEntry[])d.Attributes?.Clone();
			return d;
		}

		private static int Delta(CharacterReconcileData prev, CharacterReconcileData next)
			=> Bytes(w => w.WriteDelta(prev, next, DeltaSerializerOption.RootSerialize));

		[Test]
		public void Reconcile_Scenarios()
		{
			CharacterReconcileData prev = BaseReconcile();

			// Idle: standing still, full resources, nothing casting. Only the sequence byte moves.
			CharacterReconcileData idle = NextTick(prev);

			// Walking: motor position, velocity and rotation, plus a regen pulse landing.
			CharacterReconcileData walking = NextTick(prev);
			walking.MotorState.Position += new Vector3(0.12f, 0f, 0.04f);
			walking.MotorState.BaseVelocity = new Vector3(3.6f, 0f, 1.2f);
			walking.MotorState.Rotation = Quaternion.Euler(0f, 42f, 0f);
			CharacterReconcileData walkingRegen = walking;
			walkingRegen.ResourceState.NextRegenTick = 930;
			walkingRegen.ResourceState.Stamina = 396f;

			// Combat: walking, plus health and stamina moving, a cast in flight, the RNG advanced,
			// one buff ticking and one new cooldown started.
			CharacterReconcileData combat = Detach(NextTick(prev));
			combat.MotorState.Position += new Vector3(0.12f, 0f, 0.04f);
			combat.MotorState.BaseVelocity = new Vector3(3.6f, 0f, 1.2f);
			combat.MotorState.Rotation = Quaternion.Euler(0f, 42f, 0f);
			combat.ResourceState.Health -= 37f;
			combat.ResourceState.Stamina -= 4.2f;
			combat.ResourceState.Mana -= 25f;
			combat.AbilityID = 8_842_001_337L;
			combat.RemainingTicks = 11;
			combat.Seed = 99181;
			combat.PackedFlagsAndSlot = CharacterReconcileData.Pack(1 << (int)AbilityActivationFlags.IsHeld, -1);
			combat.RngS0 ^= 0x5555; combat.RngS1 ^= 0x3333; combat.RngS2 ^= 0x0F0F; combat.RngS3 ^= 0xF0F0;
			combat.Buffs[0].NextTickTick = 50;
			combat.Buffs[0].TickCount = 3;
			combat.Buffs[0].CumulativeTickMultiplier = 2;
			combat.Cooldowns = new[]
			{
				new CooldownReconcileEntry { AbilityID = 42, StartTick = 100, DurationTicks = 60 },
				new CooldownReconcileEntry { AbilityID = 8_842_001_337L, StartTick = 118, DurationTicks = 30 },
			};

			/* Burst: everything in combat, plus three buffs changing at once (array length change,
			 * so the whole array is re-sent), an equipment swap, and four attributes moving with
			 * the gear. */
			CharacterReconcileData burst = Detach(combat);
			burst.Buffs = new[]
			{
				new BuffReconcileEntry { TemplateID = 3, ExpiryTick = 500, NextTickTick = 50, Stacks = 1, TickCount = 3, CumulativeTickMultiplier = 2 },
				new BuffReconcileEntry { TemplateID = 9, ExpiryTick = 640, NextTickTick = 130, Stacks = 2, TickCount = 8, CumulativeTickMultiplier = 5 },
				new BuffReconcileEntry { TemplateID = 11, ExpiryTick = 700, NextTickTick = 145, Stacks = 0, TickCount = 1, CumulativeTickMultiplier = 1 },
			};
			burst.Equipment = new[]
			{
				new EquipmentReconcileEntry { TemplateID = 5, Slot = 1, Seed = 77, InstanceID = 900 },
				new EquipmentReconcileEntry { TemplateID = 61, Slot = 2, Seed = 4242, InstanceID = 9501 },
				new EquipmentReconcileEntry { TemplateID = 62, Slot = 4, Seed = 4243, InstanceID = 9502 },
			};
			for (int i = 0; i < 4; i++)
			{
				burst.Attributes[i] = new AttributeReconcileEntry { TemplateID = 100 + i, Value = 10 + i, ExternalModifier = 12 + i };
			}

			Record("rec.deltaIdle", Delta(prev, idle));
			Record("rec.deltaWalking", Delta(prev, walking));
			Record("rec.deltaWalkingRegenPulse", Delta(prev, walkingRegen));
			Record("rec.deltaCombat", Delta(prev, combat));
			Record("rec.deltaBurst", Delta(prev, burst));
			Record("rec.absolute", Bytes(w => w.WriteDelta(prev, walking, DeltaSerializerOption.FullSerialize)));
			Record("rec.absoluteBurst", Bytes(w => w.WriteDelta(prev, burst, DeltaSerializerOption.FullSerialize)));
			Record("rec.absoluteHz", 1.0);
			Record("rec.hz", TickRate);
			Record("rec.attributeCount", prev.Attributes.Length);

			LogAssert.IsTrue(Delta(prev, idle) < Delta(prev, walking),
				"An idle reconcile must be smaller than a walking one.");
		}

		/// <summary>
		/// Attributes each reconcile controller's share of the payload by ablation: change only
		/// that controller's fields and take the difference against a no-change delta.
		/// </summary>
		[Test]
		public void Reconcile_PerControllerAblation()
		{
			CharacterReconcileData prev = BaseReconcile();
			int floor = Delta(prev, NextTick(prev));
			Record("rec.ablation.floorSequenceOnly", floor);

			// KCCPlayer (Order 80) — motor state.
			CharacterReconcileData kcc = NextTick(prev);
			kcc.MotorState.Position += new Vector3(0.12f, 0f, 0.04f);
			kcc.MotorState.BaseVelocity = new Vector3(3.6f, 0f, 1.2f);
			kcc.MotorState.Rotation = Quaternion.Euler(0f, 42f, 0f);
			Record("rec.ablation.kccWalking", Delta(prev, kcc) - floor);

			CharacterReconcileData kccPos = NextTick(prev);
			kccPos.MotorState.Position += new Vector3(0.12f, 0f, 0.04f);
			Record("rec.ablation.kccPositionOnly", Delta(prev, kccPos) - floor);

			CharacterReconcileData kccGround = NextTick(prev);
			kccGround.MotorState.GroundingStatus.GroundNormal = new Vector3(0.05f, 0.9987f, 0.01f).normalized;
			Record("rec.ablation.kccGroundNormal", Delta(prev, kccGround) - floor);

			// BuffController (Order 85) — one buff entry changing in place.
			CharacterReconcileData buff = Detach(NextTick(prev));
			buff.Buffs[0].NextTickTick = 50;
			buff.Buffs[0].TickCount = 3;
			Record("rec.ablation.buffOneEntryChanged", Delta(prev, buff) - floor);

			CharacterReconcileData buffAdd = Detach(NextTick(prev));
			buffAdd.Buffs = new[]
			{
				prev.Buffs[0],
				new BuffReconcileEntry { TemplateID = 9, ExpiryTick = 640, NextTickTick = 130, Stacks = 2, TickCount = 8, CumulativeTickMultiplier = 5 },
			};
			Record("rec.ablation.buffAddedFullArray2", Delta(prev, buffAdd) - floor);

			// CooldownController (Order 90).
			CharacterReconcileData cd = Detach(NextTick(prev));
			cd.Cooldowns = new[]
			{
				prev.Cooldowns[0],
				new CooldownReconcileEntry { AbilityID = 8_842_001_337L, StartTick = 118, DurationTicks = 30 },
			};
			Record("rec.ablation.cooldownAddedFullArray2", Delta(prev, cd) - floor);

			// EquipmentController (Order 93).
			CharacterReconcileData eq = Detach(NextTick(prev));
			eq.Equipment[1] = new EquipmentReconcileEntry { TemplateID = 61, Slot = 2, Seed = 4242, InstanceID = 9501 };
			Record("rec.ablation.equipmentOneSlotChanged", Delta(prev, eq) - floor);

			// CharacterAttributeController (Order 95) — resources and the attribute sheet.
			CharacterReconcileData res = NextTick(prev);
			res.ResourceState.Health -= 37f;
			Record("rec.ablation.resourceHealthOnly", Delta(prev, res) - floor);

			CharacterReconcileData res3 = NextTick(prev);
			res3.ResourceState.Health -= 37f; res3.ResourceState.Mana -= 25f; res3.ResourceState.Stamina -= 4.2f;
			Record("rec.ablation.resourceAllThree", Delta(prev, res3) - floor);

			CharacterReconcileData attr = Detach(NextTick(prev));
			attr.Attributes[3] = new AttributeReconcileEntry { TemplateID = 103, Value = 13, ExternalModifier = 12 };
			Record("rec.ablation.attributeOneOf24", Delta(prev, attr) - floor);

			CharacterReconcileData attr4 = Detach(NextTick(prev));
			for (int i = 0; i < 4; i++)
			{
				attr4.Attributes[i] = new AttributeReconcileEntry { TemplateID = 100 + i, Value = 10 + i, ExternalModifier = 12 + i };
			}
			Record("rec.ablation.attributeFourOf24", Delta(prev, attr4) - floor);

			// AbilityController (Order 100) — activation fields plus the RNG words.
			CharacterReconcileData ability = NextTick(prev);
			ability.AbilityID = 8_842_001_337L;
			ability.RemainingTicks = 11;
			ability.Seed = 99181;
			ability.PackedFlagsAndSlot = CharacterReconcileData.Pack(1 << (int)AbilityActivationFlags.IsHeld, -1);
			Record("rec.ablation.abilityActivation", Delta(prev, ability) - floor);

			CharacterReconcileData rng = NextTick(prev);
			rng.RngS0 ^= 0x5555; rng.RngS1 ^= 0x3333; rng.RngS2 ^= 0x0F0F; rng.RngS3 ^= 0xF0F0;
			Record("rec.ablation.abilityRngWords", Delta(prev, rng) - floor);

			// Absolute-snapshot composition: what each block costs when everything is written.
			Record("rec.absolute.motorState", Bytes(w => w.Write(prev.MotorState)));
			Record("rec.absolute.resourceState", Bytes(w => w.Write(prev.ResourceState)));
			Record("rec.absolute.rngWords", Bytes(w =>
			{
				w.WriteUInt32(prev.RngS0); w.WriteUInt32(prev.RngS1); w.WriteUInt32(prev.RngS2); w.WriteUInt32(prev.RngS3);
			}));
			Record("rec.absolute.attributeEntry", Bytes(w => prev.Attributes[0].WriteTo(w)));
			Record("rec.absolute.buffEntry", Bytes(w => prev.Buffs[0].WriteTo(w)));
			Record("rec.absolute.cooldownEntry", Bytes(w => prev.Cooldowns[0].WriteTo(w)));
			Record("rec.absolute.equipmentEntry", Bytes(w => EquipmentReconcileEntry.WriteTo(w, prev.Equipment[0])));
		}

		// ── 4. NetworkTransform — Mode A for players, both modes for NPCs ────

		private static int MeasureNetworkTransform(int changedMask)
		{
			GameObject go = new GameObject("NtMeasure");
			try
			{
				go.transform.localPosition = new Vector3(112.61f, 30.9f, -47.21f);
				go.transform.localRotation = Quaternion.Euler(0f, 22f, 0f);
				FishNet.Component.Transforming.NetworkTransform nt =
					go.AddComponent<FishNet.Component.Transforming.NetworkTransform>();
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
		public void NetworkTransform_AndDistanceLod()
		{
			// ChangedDelta bits: PositionX=1, PositionY=2, PositionZ=4, Rotation=8.
			Record("nt.moveAndTurn", MeasureNetworkTransform(1 | 2 | 4 | 8));
			Record("nt.moveFlat", MeasureNetworkTransform(1 | 4));
			Record("nt.turnOnly", MeasureNetworkTransform(8));

			/* Bands authored on the playable-character prefabs (Human/Elf/Orc):
			 * 0-20 m every tick, 20-40 m every 3rd, 40 m+ every 6th. NPC prefabs carry no
			 * NetworkTransformDistanceLod at all and so send every tick to every observer. */
			Record("nt.lodBandNearMaxDistance", 20);
			Record("nt.lodBandNearInterval", 1);
			Record("nt.lodBandMidMaxDistance", 40);
			Record("nt.lodBandMidInterval", 3);
			Record("nt.lodBandFarMaxDistance", 80);
			Record("nt.lodBandFarInterval", 6);
			Record("nt.hz", TickRate);
			Record("nt.observerStreamingFullRateCap", ObserverStreamingPolicy.FullRateObserverCap);
		}

		// ── 5. Observer broadcasts ───────────────────────────────────────────

		/* FishNet's IL post-processor generates a writer per IBroadcast struct in a player build;
		 * those do not exist under EditMode. Structs that carry a hand-written serializer are
		 * written through it below. The rest are reproduced field for field in declaration order
		 * with the primitives codegen emits: WriteInt32/WriteInt64/WriteUInt32 are variable-width
		 * packed, WriteSingle is 4 B, WriteBoolean and a byte are 1 B, and an array is a signed
		 * packed count followed by its elements (Writer.WriteArray). */

		private static void WriteResourcesBroadcast(Writer w, CharacterResourcesBroadcast m)
		{
			w.WriteInt32(m.CharacterObjectID);
			w.WriteInt32(m.Health); w.WriteInt32(m.MaxHealth);
			w.WriteInt32(m.Mana); w.WriteInt32(m.MaxMana);
			w.WriteInt32(m.Stamina); w.WriteInt32(m.MaxStamina);
		}

		private static void WriteBuffsBroadcast(Writer w, CharacterBuffsBroadcast m)
		{
			w.WriteInt32(m.CharacterObjectID);
			int count = m.Buffs?.Length ?? 0;
			w.WriteSignedPackedWhole(count);
			for (int i = 0; i < count; i++)
			{
				ObservedBuffEntry e = m.Buffs[i];
				w.WriteInt32(e.TemplateID);
				w.WriteInt32(e.Stacks);
				w.WriteSingle(e.RemainingSeconds);
				w.WriteSingle(e.TotalSeconds);
			}
		}

		private static void WriteEquipmentObservedSlot(Writer w, EquipmentObservedSlotBroadcast m)
		{
			w.WriteInt32(m.CharacterObjectID);
			w.WriteUInt8Unpacked(m.Slot);
			w.WriteInt32(m.TemplateID);
			w.WriteInt32(m.Seed);
		}

		private static void WriteAbilityObjectDestroyed(Writer w, AbilityObjectDestroyedBroadcast m)
		{
			w.WriteInt32(m.CasterObjectID);
			w.WriteInt64(m.AbilityID);
			w.WriteInt32(m.ContainerID);
			w.WriteInt32(m.ObjectID);
		}

		private static void WriteDeathState(Writer w, CharacterDeathStateBroadcast m)
		{
			w.WriteInt32(m.CharacterObjectID);
			w.WriteBoolean(m.Dead);
		}

		private static ObservedBuffEntry[] Buffs(int n)
		{
			ObservedBuffEntry[] entries = new ObservedBuffEntry[n];
			for (int i = 0; i < n; i++)
			{
				entries[i] = new ObservedBuffEntry
				{
					TemplateID = 200 + i, Stacks = i % 3,
					RemainingSeconds = 12.4f + i, TotalSeconds = 30f,
				};
			}
			return entries;
		}

		[Test]
		public void Broadcasts_ObserverChannel()
		{
			// CharacterAttributeController → resources.
			Record("bc.resources", Bytes(w => WriteResourcesBroadcast(w, new CharacterResourcesBroadcast
			{
				CharacterObjectID = 40, Health = 812, MaxHealth = 1200,
				Mana = 240, MaxMana = 800, Stamina = 310, MaxStamina = 400,
			})));
			Record("bc.resourcesHzMax", TickRate / 6.0); // observedResourcePushInterval = 6
			Record("bc.resourceConfirmDelayTicks", (int)ObservedResourcePushScheduler.ConfirmDelayTicks);

			// CharacterAttributeController → attributes. Real hand-written serializer.
			Record("bc.attributes1", Bytes(w => w.WriteCharacterAttributesBroadcast(new CharacterAttributesBroadcast
			{
				CharacterObjectID = 40, IsFullSet = false,
				Attributes = new[] { new AttributeReconcileEntry { TemplateID = 103, Value = 42, ExternalModifier = 12 } },
			})));
			Record("bc.attributes4", Bytes(w =>
			{
				AttributeReconcileEntry[] e = new AttributeReconcileEntry[4];
				for (int i = 0; i < 4; i++) e[i] = new AttributeReconcileEntry { TemplateID = 100 + i, Value = 10 + i, ExternalModifier = 12 + i };
				w.WriteCharacterAttributesBroadcast(new CharacterAttributesBroadcast { CharacterObjectID = 40, IsFullSet = false, Attributes = e });
			}));
			Record("bc.attributesFullSet24", Bytes(w =>
			{
				AttributeReconcileEntry[] e = new AttributeReconcileEntry[24];
				for (int i = 0; i < 24; i++) e[i] = new AttributeReconcileEntry { TemplateID = 100 + i, Value = 10 + i, ExternalModifier = 0 };
				w.WriteCharacterAttributesBroadcast(new CharacterAttributesBroadcast { CharacterObjectID = 40, IsFullSet = true, Attributes = e });
			}));

			// BuffController → observed buffs.
			Record("bc.buffs0", Bytes(w => WriteBuffsBroadcast(w, new CharacterBuffsBroadcast { CharacterObjectID = 40, Buffs = Buffs(0) })));
			Record("bc.buffs1", Bytes(w => WriteBuffsBroadcast(w, new CharacterBuffsBroadcast { CharacterObjectID = 40, Buffs = Buffs(1) })));
			Record("bc.buffs3", Bytes(w => WriteBuffsBroadcast(w, new CharacterBuffsBroadcast { CharacterObjectID = 40, Buffs = Buffs(3) })));
			Record("bc.buffs6", Bytes(w => WriteBuffsBroadcast(w, new CharacterBuffsBroadcast { CharacterObjectID = 40, Buffs = Buffs(6) })));
			Record("bc.buffEntry", Bytes(w =>
			{
				w.WriteInt32(200); w.WriteInt32(1); w.WriteSingle(12.4f); w.WriteSingle(30f);
			}));

			// EquipmentController → one observed slot.
			Record("bc.equipmentSlotFilled", Bytes(w => WriteEquipmentObservedSlot(w, new EquipmentObservedSlotBroadcast
			{
				CharacterObjectID = 40, Slot = 2, TemplateID = 61, Seed = 4242,
			})));
			Record("bc.equipmentSlotEmptied", Bytes(w => WriteEquipmentObservedSlot(w, new EquipmentObservedSlotBroadcast
			{
				CharacterObjectID = 40, Slot = 2, TemplateID = 0, Seed = 0,
			})));

			// AbilityController → destroyed, learned.
			Record("bc.abilityObjectDestroyed", Bytes(w => WriteAbilityObjectDestroyed(w, new AbilityObjectDestroyedBroadcast
			{
				CasterObjectID = 40, AbilityID = 8_842_001_337L, ContainerID = -1_713_468_379, ObjectID = 3,
			})));
			Record("bc.abilityLearned0Events", Bytes(w => w.WriteAbilityLearnedObserverBroadcast(new AbilityLearnedObserverBroadcast
			{
				CasterObjectID = 40, AbilityID = 8_842_001_337L, TemplateID = 512, Events = System.Array.Empty<int>(),
			})));
			Record("bc.abilityLearned3Events", Bytes(w => w.WriteAbilityLearnedObserverBroadcast(new AbilityLearnedObserverBroadcast
			{
				CasterObjectID = 40, AbilityID = 8_842_001_337L, TemplateID = 512, Events = new[] { 601, 602, 603 },
			})));

			// CharacterDamageController → combat numbers. Real hand-written serializer.
			Record("bc.combatEventDamage", Bytes(w => w.WriteCombatEventBroadcast(new CombatEventBroadcast
			{
				TargetObjectID = 40, SourceObjectID = 55, Amount = 372,
				Kind = (byte)CombatEventKind.Damage, DamageTemplateID = 7,
			})));
			Record("bc.combatEventHeal", Bytes(w => w.WriteCombatEventBroadcast(new CombatEventBroadcast
			{
				TargetObjectID = 40, SourceObjectID = 55, Amount = 120,
				Kind = (byte)CombatEventKind.Heal, DamageTemplateID = 0,
			})));
			Record("bc.combatEventCoalesceCap", CombatEventCoalescer.MaxEntries);

			// CharacterDamageController → death state.
			Record("bc.deathState", Bytes(w => WriteDeathState(w, new CharacterDeathStateBroadcast { CharacterObjectID = 40, Dead = true })));
		}

		/// <summary>
		/// <see cref="AbilityActivatedBroadcast"/> through its real, mode-shaped serializer.
		/// </summary>
		/// <remarks>
		/// The one message whose size depends on the ability template. Camera spawns carry the aim
		/// origin and packed direction and omit the pose; everything else carries the pose (a
		/// Vector3 plus a 64-bit quaternion) and omits the aim.
		/// </remarks>
		[Test]
		public void Broadcasts_AbilityActivated_PerSpawnMode()
		{
			AbilityActivatedBroadcast Template(AbilitySpawnTarget mode, int targetId, uint spawnTick, uint serverTick)
				=> new AbilityActivatedBroadcast
				{
					CasterObjectID = 40,
					AbilityID = 8_842_001_337L,
					Seed = -1_713_468_379,
					SpawnTick = spawnTick,
					ServerTick = serverTick,
					SpawnMode = (byte)mode,
					TargetObjectID = targetId,
					AimOrigin = new Vector3(112.5f, 32.6f, -47.2f),
					PackedAimDirection = AimDirectionCompression.Encode(new Vector3(0.3f, -0.1f, 0.95f)),
					SpawnPosition = new Vector3(113.1f, 32.6f, -46.4f),
					SpawnRotation = Quaternion.Euler(12f, 200f, 0f),
				};

			foreach (AbilitySpawnTarget mode in Enum.GetValues(typeof(AbilitySpawnTarget)))
			{
				int noTarget = Bytes(w => w.WriteAbilityActivatedBroadcast(Template(mode, -1, 123_450u, 123_456u)));
				int withTarget = Bytes(w => w.WriteAbilityActivatedBroadcast(Template(mode, 77, 123_450u, 123_456u)));
				Record($"bc.activation.{mode}.noTarget", noTarget);
				Record($"bc.activation.{mode}.withTarget", withTarget);
			}

			// The full-width tick fallback: the two tick domains further apart than a short.
			Record("bc.activation.Camera.tickFallback",
				Bytes(w => w.WriteAbilityActivatedBroadcast(Template(AbilitySpawnTarget.Camera, 77, 1u, 400_000u))));

			// Round-trip proof that the measured bytes are a real message, not a truncated one.
			AbilityActivatedBroadcast source = Template(AbilitySpawnTarget.Forward, 77, 123_450u, 123_456u);
			Writer writer = new Writer();
			writer.WriteAbilityActivatedBroadcast(source);
			Reader reader = new Reader(writer.GetArraySegment(), null);
			AbilityActivatedBroadcast round = reader.ReadAbilityActivatedBroadcast();
			LogAssert.AreEqual(source.CasterObjectID, round.CasterObjectID, "Activation round trip: caster.");
			LogAssert.AreEqual(source.AbilityID, round.AbilityID, "Activation round trip: ability.");
			LogAssert.AreEqual(source.SpawnTick, round.SpawnTick, "Activation round trip: spawn tick.");
			LogAssert.AreEqual(source.TargetObjectID, round.TargetObjectID, "Activation round trip: target.");

			/* The stale model this replaces. BandwidthCompositionTests.WriteActivation wrote
			 * caster, ability, seed, spawn tick, aim origin, packed aim and target — no header
			 * byte, no server tick, and never the pose. Recorded so the report can state the size
			 * of the error rather than assert it. */
			Record("bc.activation.legacyModelledForm", Bytes(w =>
			{
				w.WriteInt32(40); w.WriteInt64(8_842_001_337L); w.WriteInt32(-1_713_468_379);
				w.WriteUInt32(123_450u); w.WriteVector3(new Vector3(112.5f, 32.6f, -47.2f));
				w.WriteUInt32(AimDirectionCompression.Encode(new Vector3(0.3f, -0.1f, 0.95f)));
				w.WriteInt32(77);
			}));
		}

		// ── 6. Spawn payloads — once per observer add, both modes ────────────

		[Test]
		public void SpawnPayloads_OwnerAndObserverShapes()
		{
			GameObject go = new GameObject("PayloadProbe");
			List<UnityEngine.Object> assets = new List<UnityEngine.Object>();
			try
			{
				MockCharacter character = new MockCharacter(9);

				// ── attributes: 24 non-resource + 3 resource. One shape for everyone. ──
				CharacterAttributeController attributes = go.AddComponent<CharacterAttributeController>();
				attributes.InitializeOnce(character);
				for (int i = 0; i < 24; i++)
				{
					CharacterAttributeTemplate t = ScriptableObject.CreateInstance<CharacterAttributeTemplate>();
					t.name = $"MapAttr_{i}"; t.InitialValue = 10 + i; t.AddToCache(t.name); assets.Add(t);
					attributes.AddAttribute(new CharacterAttribute(attributes, t.ID, t.InitialValue, 0));
				}
				for (int i = 0; i < 3; i++)
				{
					CharacterAttributeTemplate t = ScriptableObject.CreateInstance<CharacterAttributeTemplate>();
					t.name = $"MapRes_{i}"; t.InitialValue = 1000; t.IsResourceAttribute = true; t.AddToCache(t.name); assets.Add(t);
					attributes.AddResourceAttribute(new CharacterResourceAttribute(attributes, t.ID, 1000, 1000, 0));
				}
				Record("spawn.attributes24plus3", Bytes(w => attributes.WritePayload(null, w)));

				// ── abilities: observer shape (conn == null is never the owner) ──
				AbilityController abilityController = go.AddComponent<AbilityController>();
				abilityController.OnAwake();
				SetPrivate(abilityController, "abilitySeedGenerator", new DeterministicRNG(1));
				abilityController.InitializeOnce(character);
				Record("spawn.abilities0.observer", Bytes(w => abilityController.WritePayload(null, w)));
				for (int i = 0; i < 12; i++)
				{
					AbilityTemplate t = ScriptableObject.CreateInstance<MapAbilityTemplate>();
					t.name = $"MapAbility_{i}"; t.AddToCache(t.name); assets.Add(t);
					abilityController.LearnAbility(new Ability(10_000 + i, t));
				}
				Record("spawn.abilities12.observer", Bytes(w => abilityController.WritePayload(null, w)));

				/* The owner shape is not reachable here — PayloadVisibility.IsOwner needs a valid
				 * NetworkConnection that owns a spawned NetworkObject, and neither exists in
				 * EditMode. The owner block is the observer block plus the shape-selected extras
				 * the writer adds, modelled with the same writer calls: abilitySeed, currentSeed
				 * and the four xoshiro words (AbilityController.Networking.cs:578-585). */
				Record("spawn.abilities.ownerExtraRngBlock", Bytes(w =>
				{
					w.WriteInt32(12345); w.WriteInt32(67890);
					w.WriteUInt32(0xDEADBEEF); w.WriteUInt32(0x12345678);
					w.WriteUInt32(0x0BADF00D); w.WriteUInt32(0xFEEDFACE);
				}));
				/* And the bounded in-flight list, capped at MAX_PAYLOAD_IN_FLIGHT_OBJECTS = 8
				 * (AbilityController.Networking.cs:289). Sent to every receiver, owner included. */
				Record("spawn.abilities.inFlightEntry", Bytes(w =>
				{
					w.WriteInt64(8_842_001_337L); w.WriteInt32(-1_713_468_379);
					w.WriteUInt32(123_450u); w.WriteUInt32(123_456u);
					w.WriteVector3(new Vector3(113.1f, 32.6f, -46.4f));
					w.WriteQuaternion64(Quaternion.Euler(12f, 200f, 0f));
				}));
				Record("spawn.abilities.inFlightCap", 8);

				// ── buffs: observer (display) shape via the real writer ──
				BuffController buffs = go.AddComponent<BuffController>();
				SetPrivate(buffs, "tickDelta", 1f / 30f);
				SetPrivate(buffs, "lastReplicateTick", 100u);
				SetPrivate(buffs, "hasSeenFirstReplicate", true);
				buffs.InitializeOnce(character);
				Record("spawn.buffs0.observer", Bytes(w => buffs.WritePayload(null, w)));
				for (int i = 0; i < 3; i++)
				{
					MapBuffTemplate t = ScriptableObject.CreateInstance<MapBuffTemplate>();
					t.name = $"MapBuff_{i}"; t.Duration = 30f; t.AddToCache(t.name); assets.Add(t);
					buffs.Apply(t, new PredictionTick(100u));
				}
				Record("spawn.buffs3.observer", Bytes(w => buffs.WritePayload(null, w)));
				/* Owner (simulation) shape, modelled from BuffController.cs:1360-1368: template,
				 * expiry tick, next tick, stacks, tick count, cumulative multiplier. */
				Record("spawn.buffs.ownerEntry", Bytes(w =>
				{
					w.WriteInt32(200); w.WriteUInt32(500); w.WriteUInt32(20);
					w.WriteInt32(1); w.WriteInt32(4); w.WriteInt32(2);
				}));
				/* Observer entry, from BuffController.cs:1379-1382: template, stacks, remaining
				 * seconds. TotalSeconds is omitted — the receiver reads it off the template. */
				Record("spawn.buffs.observerEntry", Bytes(w =>
				{
					w.WriteInt32(200); w.WriteInt32(1); w.WriteSingle(12.4f);
				}));

				// ── equipment: real writer, both shapes, with real items ──
				EquipmentController equipment = go.AddComponent<EquipmentController>();
				equipment.OnAwake();
				equipment.InitializeOnce(character);
				Record("spawn.equipment0.observer", Bytes(w => equipment.WritePayload(null, w)));
				for (int i = 0; i < 6; i++)
				{
					MapItemTemplate t = ScriptableObject.CreateInstance<MapItemTemplate>();
					t.name = $"MapItem_{i}"; t.AddToCache(t.name); assets.Add(t);
					Item item = new Item(9000 + i, 4242 + i, t, 1);
					equipment.SetItemSlot(item, i);
				}
				Record("spawn.equipment6.observer", Bytes(w => equipment.WritePayload(null, w)));
				/* Owner shape adds the instance id and the stack amount per item
				 * (EquipmentController.cs:806-819). */
				Record("spawn.equipment.ownerExtraPerItem", Bytes(w =>
				{
					w.WriteInt64(9000L); w.WriteUInt32(1u);
				}));
				Record("spawn.equipment.observerPerItem", Bytes(w =>
				{
					w.WriteInt32(512); w.WriteUInt8Unpacked(2); w.WriteInt32(4242);
				}));
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

		// ── support ──────────────────────────────────────────────────────────

		private static void SetPrivate<T>(object o, string field, T value)
			=> o.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(o, value);

		private sealed class MapAbilityTemplate : AbilityTemplate { }

		private sealed class MapBuffTemplate : BaseBuffTemplate
		{
			public override void OnApply(Buff buff, ICharacter target) { }
			public override void OnRemove(Buff buff, ICharacter target) { }
		}

		private sealed class MapItemTemplate : BaseItemTemplate { }

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
