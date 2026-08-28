using System;
using System.Reflection;
using NUnit.Framework;
using FishMMO.Shared;
using FishNet.Serializing;
using UnityEngine;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// End-to-end bandwidth projection for a populated scene, built from measured payloads.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>What is measured.</b> Every payload figure below comes out of the production serializers
	/// in this run — the reconcile delta, the self-contained replicate packet, and the
	/// <c>NetworkTransform</c> update that an interpolated peer would cost. Nothing is copied from a
	/// previous run or estimated.
	/// </para>
	/// <para>
	/// <b>What is modelled.</b> The framing (QUIC/UDP/IP), the batching to MTU, and the per-second
	/// mix of one <c>FullSerialize</c> to twenty-nine <c>RootSerialize</c>. The framing constants
	/// come from the transport's own accounting and the relevant RFCs rather than a packet capture,
	/// so the totals are a good engineering estimate, not a measurement of the wire.
	/// </para>
	/// <para>
	/// <b>What is not covered at all.</b> Chat, inventory, quests, spawn payloads, scene transitions,
	/// and server CPU. Spawn payloads in particular spike at zone boundaries and are not small.
	/// </para>
	/// <para>
	/// The two models differ in one decision: whether a client predicts its peers or interpolates
	/// them. Predicting peers is what <c>_enableStateForwarding</c> does today, and it makes cost
	/// scale with the square of the players in a scene, because every player's reconcile and
	/// relayed replicate goes to every observer. Interpolating them sends each player's reconcile
	/// only to its owner and lets a transform update carry everyone else.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class ScaleProjectionTests
	{
		private const int ServerTickRate = 30;

		// Framing — see PredictionBandwidthBenchmarkTests for provenance.
		private const int IpUdpHeaderBytes = 20 + 8;
		private const int QuicDatagramOverheadBytes = 1200 - 1150;
		private const int DatagramMtuBytes = 1150;
		private const int QuicStreamPacketOverheadBytes = 1 + 8 + 2 + 16 + 8;
		private const int StreamBytesPerQuicPacket = 1200 - QuicStreamPacketOverheadBytes;
		private const int FishNetRpcHeaderBytes = 10;

		private static int reconcileDeltaBytes;
		private static int replicatePacketBytes;
		private static int networkTransformBytes;

		[OneTimeSetUp]
		public void MeasureProductionPayloads()
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
				t.GetMethod("RegisterSerializers", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
					.Invoke(null, null);
			}

			reconcileDeltaBytes = MeasureReconcileDelta();
			replicatePacketBytes = MeasureReplicatePacket();
			networkTransformBytes = MeasureNetworkTransformUpdate();
		}

		private static int Bytes(Action<Writer> write)
		{
			Writer writer = new Writer();
			write(writer);
			return writer.Length;
		}

		/// <summary>One walking tick of reconcile, delta-encoded as FishNet drives it.</summary>
		private static int MeasureReconcileDelta()
		{
			CharacterReconcileData prev = default;
			prev.MotorState = default;
			prev.MotorState.Position = new Vector3(112.5f, 30.9f, -47.25f);
			prev.MotorState.Rotation = Quaternion.Euler(0f, 20f, 0f);
			prev.MotorState.GroundingStatus = default;
			prev.MotorState.GroundingStatus.FoundAnyGround = true;
			prev.MotorState.GroundingStatus.IsStableOnGround = true;
			prev.MotorState.GroundingStatus.GroundNormal = Vector3.up;
			prev.MotorState.GroundingStatus.InnerGroundNormal = Vector3.up;
			prev.MotorState.GroundingStatus.OuterGroundNormal = Vector3.up;
			prev.ResourceState = default;
			prev.ResourceState.MaxHealth = 1200; prev.ResourceState.Health = 1200f;
			prev.Cooldowns = new[] { new CooldownReconcileEntry { AbilityID = 42, StartTick = 100, DurationTicks = 60 } };
			prev.Buffs = new[] { new BuffReconcileEntry { TemplateID = 3, ExpiryTick = 500, NextTickTick = 20, Stacks = 1, TickCount = 4, CumulativeTickMultiplier = 1 } };
			prev.Equipment = new[] { new EquipmentReconcileEntry { TemplateID = 5, Slot = 1, Seed = 77, InstanceID = 900 } };
			prev.Attributes = new[]
			{
				new AttributeReconcileEntry { TemplateID = 1, Value = 25, ExternalModifier = 4 },
				new AttributeReconcileEntry { TemplateID = 2, Value = 31, ExternalModifier = 0 },
				new AttributeReconcileEntry { TemplateID = 3, Value = 18, ExternalModifier = 6 },
			};

			CharacterReconcileData next = prev;
			next.Cooldowns = (CooldownReconcileEntry[])prev.Cooldowns.Clone();
			next.Buffs = (BuffReconcileEntry[])prev.Buffs.Clone();
			next.Equipment = (EquipmentReconcileEntry[])prev.Equipment.Clone();
			next.Attributes = (AttributeReconcileEntry[])prev.Attributes.Clone();
			next.MotorState.Position += new Vector3(0.12f, 0f, 0.04f);
			next.MotorState.BaseVelocity = new Vector3(3.6f, 0f, 1.2f);
			next.MotorState.Rotation = Quaternion.Euler(0f, 22f, 0f);
			next.ResourceState.Health -= 1f;

			return Bytes(w => w.WriteDelta(prev, next, DeltaSerializerOption.RootSerialize));
		}

		/// <summary>One replicate packet: count byte, absolute entry 0, then two deltas.</summary>
		private static int MeasureReplicatePacket()
		{
			CharacterReplicateData t0 = default;
			t0.CameraRotationless(40f);
			CharacterReplicateData t1 = t0;
			t1.AimDirection = AimDirectionCompression.Quantize(Quaternion.Euler(3f, 41.2f, 0f) * Vector3.forward);
			CharacterReplicateData t2 = t1;
			t2.AimDirection = AimDirectionCompression.Quantize(Quaternion.Euler(3f, 42.4f, 0f) * Vector3.forward);

			CharacterReplicateData[] entries = { t0, t1, t2 };
			return Bytes(w =>
			{
				w.WriteUInt8Unpacked((byte)entries.Length);
				for (int i = 0; i < entries.Length; i++)
				{
					if (i == 0)
					{
						w.Write(entries[0]);
					}
					else
					{
						w.WriteDelta(entries[i - 1], entries[i], DeltaSerializerOption.RootSerialize);
					}
					w.WriteUInt8Unpacked(0); // channel
				}
			});
		}

		/// <summary>
		/// One <c>NetworkTransform</c> update for a moving character, measured through FishNet's own
		/// <c>SerializeChanged</c> rather than estimated.
		/// </summary>
		/// <remarks>
		/// <c>SerializeChanged</c> and its <c>ChangedDelta</c> enum are private, and
		/// <c>_cachedTransform</c> is assigned in <c>OnStartNetwork</c> which does not run under
		/// EditMode — hence the reflection. Driving the real method is the point: a hand-rolled model
		/// of the format would not track FishNet's packing decisions.
		/// </remarks>
		private static int MeasureNetworkTransformUpdate()
		{
			GameObject go = new GameObject("NetworkTransformMeasurement");
			try
			{
				go.transform.localPosition = new Vector3(112.61f, 30.9f, -47.21f);
				go.transform.localRotation = Quaternion.Euler(0f, 22f, 0f);

				FishNet.Component.Transforming.NetworkTransform nt =
					go.AddComponent<FishNet.Component.Transforming.NetworkTransform>();

				Type ntType = typeof(FishNet.Component.Transforming.NetworkTransform);
				FieldInfo cached = ntType.GetField("_cachedTransform", BindingFlags.Instance | BindingFlags.NonPublic);
				LogAssert.IsNotNull(cached, "NetworkTransform._cachedTransform must exist; the measurement depends on it.");
				cached.SetValue(nt, go.transform);

				Type changedDelta = ntType.GetNestedType("ChangedDelta", BindingFlags.NonPublic);
				LogAssert.IsNotNull(changedDelta, "NetworkTransform.ChangedDelta must exist.");
				// PositionX | PositionY | PositionZ | Rotation — a character that moved and turned.
				object changed = Enum.ToObject(changedDelta, 1 | 2 | 4 | 8);

				MethodInfo serialize = ntType.GetMethod("SerializeChanged", BindingFlags.Instance | BindingFlags.NonPublic);
				LogAssert.IsNotNull(serialize, "NetworkTransform.SerializeChanged must exist.");

				PooledWriter writer = WriterPool.Retrieve();
				try
				{
					serialize.Invoke(nt, new[] { changed, writer });
					return writer.Length;
				}
				finally
				{
					writer.Store();
				}
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(go);
			}
		}

		// ── Framing ──────────────────────────────────────────────────────────

		private static int PacketsFor(int bytes, int perPacket) => bytes <= 0 ? 0 : (bytes + perPacket - 1) / perPacket;

		/// <summary>Bytes per second a client receives, given how many peers it predicts and how many it interpolates.</summary>
		private static int ClientBytesPerSecond(int predictedPeers, int interpolatedPeers, bool includeOwnPrediction = true)
		{
			int own = includeOwnPrediction ? 1 : 0;

			// Reliable: reconcile for self plus every predicted peer.
			int reliable = (own + predictedPeers) * (FishNetRpcHeaderBytes + reconcileDeltaBytes);
			// Unreliable: relayed replicate for every predicted peer, plus a transform for every
			// interpolated one. A client's own replicate is upstream, not part of this figure.
			int unreliable = predictedPeers * (FishNetRpcHeaderBytes + replicatePacketBytes)
				+ interpolatedPeers * (FishNetRpcHeaderBytes + networkTransformBytes);

			int perTick =
				reliable + PacketsFor(reliable, StreamBytesPerQuicPacket) * (IpUdpHeaderBytes + QuicStreamPacketOverheadBytes) +
				unreliable + PacketsFor(unreliable, DatagramMtuBytes) * (IpUdpHeaderBytes + QuicDatagramOverheadBytes);

			return perTick * ServerTickRate;
		}

		[Test]
		public void Projection_ScenePopulation()
		{
			TestContext.WriteLine();
			TestContext.WriteLine("MEASURED PAYLOADS (this run, production serializers)");
			TestContext.WriteLine($"  reconcile delta (walking tick)      {reconcileDeltaBytes,4} B");
			TestContext.WriteLine($"  replicate packet (3 redundant)      {replicatePacketBytes,4} B");
			TestContext.WriteLine($"  NetworkTransform update (moving)    {networkTransformBytes,4} B");
			TestContext.WriteLine($"  FishNet RPC header (per message)    {FishNetRpcHeaderBytes,4} B");
			TestContext.WriteLine();

			LogAssert.IsTrue(networkTransformBytes > 0,
				"The NetworkTransform measurement returned zero bytes — the reflection call is not " +
				"reaching the real serializer, so every interpolated-peer figure below is meaningless.");
			LogAssert.IsTrue(networkTransformBytes < replicatePacketBytes + reconcileDeltaBytes,
				"An interpolated peer must cost less than a predicted one, or the model has no point.");

			TestContext.WriteLine($"SCENE PROJECTION — every player mutually visible, tickRate {ServerTickRate}");
			TestContext.WriteLine($"  {"players",-9}{"predicted KB/s",16}{"interpolated KB/s",19}{"pred scene Mbps",17}{"interp scene Mbps",19}");
			TestContext.WriteLine("  " + new string('-', 80));

			foreach (int players in new[] { 25, 50, 100, 150, 200 })
			{
				int peers = players - 1;
				double predKb = ClientBytesPerSecond(peers, 0) / 1024.0;
				double interpKb = ClientBytesPerSecond(0, peers) / 1024.0;

				TestContext.WriteLine(
					$"  {players,-9}{predKb,16:F1}{interpKb,19:F1}{predKb * players * 8 / 1000,17:F0}{interpKb * players * 8 / 1000,19:F0}");
			}
			TestContext.WriteLine("  " + new string('-', 80));
			TestContext.WriteLine("  'predicted' = _enableStateForwarding on players, today's behaviour (cost scales with players squared)");
			TestContext.WriteLine("  'interpolated' = reconcile to owner only, peers carried by NetworkTransform (scales linearly)");
		}

		[Test]
		public void Projection_ServerAndCost()
		{
			// Sparse zones and dungeons are where most players are; a capital is the worst case.
			(string label, int players, int visible)[] shapes =
			{
				("dungeon party", 5, 4),
				("sparse field", 60, 20),
				("busy field", 120, 40),
				("capital, culled to 50m", 150, 60),
				("capital, all visible", 150, 149),
			};

			TestContext.WriteLine();
			TestContext.WriteLine($"PER-CLIENT DOWNSTREAM BY SCENE SHAPE (tickRate {ServerTickRate})");
			TestContext.WriteLine($"  {"shape",-26}{"visible",9}{"predicted KB/s",16}{"interp KB/s",13}{"pred Mbps",11}");
			TestContext.WriteLine("  " + new string('-', 76));
			foreach (var (label, _, visible) in shapes)
			{
				double pred = ClientBytesPerSecond(visible, 0) / 1024.0;
				double interp = ClientBytesPerSecond(0, visible) / 1024.0;
				TestContext.WriteLine($"  {label,-26}{visible,9}{pred,16:F1}{interp,13:F1}{pred * 8 / 1000,11:F2}");
			}
			TestContext.WriteLine("  " + new string('-', 76));

			TestContext.WriteLine();
			TestContext.WriteLine("SERVER EGRESS AND COST — prediction traffic only, $0.09/GB");
			TestContext.WriteLine($"  {"shape",-26}{"players",9}{"pred TB/mo",12}{"pred $/mo",11}{"interp TB/mo",14}{"interp $/mo",13}");
			TestContext.WriteLine("  " + new string('-', 87));
			foreach (var (label, players, visible) in shapes)
			{
				double predTb = ClientBytesPerSecond(visible, 0) / 1024.0 * players * 3600.0 * 24 * 30 / 1024 / 1024 / 1024;
				double interpTb = ClientBytesPerSecond(0, visible) / 1024.0 * players * 3600.0 * 24 * 30 / 1024 / 1024 / 1024;
				TestContext.WriteLine(
					$"  {label,-26}{players,9}{predTb,12:F1}{predTb * 1024 * 0.09,11:F0}{interpTb,14:F1}{interpTb * 1024 * 0.09,13:F0}");
			}
			TestContext.WriteLine("  " + new string('-', 71));
			TestContext.WriteLine("  Figures are per SCENE. A Scene Server hosting several multiplies them.");
		}

		[Test]
		public void PerClientDownstream_StaysWithinAReasonableLink()
		{
			/* The binding constraint is usually the client's link, not server egress. 200 mutually
			 * visible predicted players is the shape that breaks it, and this is the assertion that
			 * says so out loud rather than leaving it in a table nobody reads. */
			const double reasonableClientMbps = 3.0;

			double predicted200 = ClientBytesPerSecond(199, 0) / 1024.0 * 8 / 1000.0;
			double interpolated200 = ClientBytesPerSecond(0, 199) / 1024.0 * 8 / 1000.0;

			TestContext.WriteLine(
				$"MEASURE 200 mutually visible: predicted={predicted200:F2} Mbps/client, " +
				$"interpolated={interpolated200:F2} Mbps/client");

			LogAssert.IsTrue(predicted200 > reasonableClientMbps,
				$"Predicting 199 peers is expected to exceed {reasonableClientMbps} Mbps per client " +
				$"({predicted200:F2} measured). If it no longer does, this projection has drifted and the " +
				"architectural advice built on it needs rechecking.");
			LogAssert.IsTrue(interpolated200 < predicted200,
				"Interpolating peers must cost less than predicting them.");
		}
	}

	/// <summary>Test-local helper so the fixture reads cleanly.</summary>
	internal static class ReplicateDataTestExtensions
	{
		public static void CameraRotationless(ref this CharacterReplicateData data, float yaw)
		{
			data.AimDirection = AimDirectionCompression.Quantize(Quaternion.Euler(3f, yaw, 0f) * Vector3.forward);
			data.MoveAxisForward = MoveAxisCompression.Quantize(1f);
			data.MoveFlags = 1;
		}
	}
}
