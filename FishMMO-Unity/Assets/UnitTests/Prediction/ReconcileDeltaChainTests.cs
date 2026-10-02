using System;
using System.Reflection;
using System.Runtime.ExceptionServices;
using NUnit.Framework;
using FishMMO.Shared;
using FishNet.Object;
using FishNet.Serializing;
using UnityEngine;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// FishNet's delta reconcile wire (<c>FISHNET_DELTA_PREDICTION</c>, feat/delta-prediction-beta),
	/// driven through its real header code.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <c>Writer.WriteDeltaReconcile</c>, <c>Reader.ReadDeltaReconcile</c> and
	/// <c>Writer.HasDeltaSerializers</c> are <c>internal</c> to FishNet.Runtime, so they are reached by
	/// reflection. Reflecting rather than re-implementing means these fixtures fail when FishNet's
	/// header changes shape, which a hand-rolled copy of the header would not.
	/// </para>
	/// <para>
	/// The bookkeeping AROUND those calls lives in private <c>NetworkBehaviour</c> fields that an
	/// EditMode test cannot reach without a spawned object, so <see cref="DeltaReconcileSender{T}"/>
	/// and <see cref="DeltaReconcileReceiver{T}"/> mirror it line for line — about thirty lines, the
	/// same approach the PR's own harness takes. <c>PredictionAuditRegressionTests</c> pins the
	/// FishNet source those mirrors copy.
	/// </para>
	/// </remarks>
	internal static class DeltaReconcileWire
	{
		private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

		/// <summary>True when FishNet will use the delta path for <typeparamref name="T"/>: both a delta writer and a delta reader are registered.</summary>
		public static bool HasDeltaSerializers<T>()
		{
			return (bool)Invoke(Find(typeof(Writer), "HasDeltaSerializers").MakeGenericMethod(typeof(T)), null, Array.Empty<object>());
		}

		/// <summary><c>Writer.WriteDeltaReconcile</c>: the one-byte header, then the regular serializer (full) or a delta against <paramref name="fullReconcile"/>.</summary>
		public static void Write<T>(Writer writer, T fullReconcile, T value, byte fullReconcileId, bool fullSerialize)
		{
			Invoke(Find(typeof(Writer), "WriteDeltaReconcile").MakeGenericMethod(typeof(T)), writer,
				new object[] { fullReconcile, value, fullReconcileId, fullSerialize });
		}

		/// <summary><c>Reader.ReadDeltaReconcile</c>.</summary>
		public static T Read<T>(Reader reader, T fullReconcile, out bool isFull, out byte fullReconcileId)
		{
			object[] args = { fullReconcile, null, null };
			T value = (T)Invoke(Find(typeof(Reader), "ReadDeltaReconcile").MakeGenericMethod(typeof(T)), reader, args);
			isFull = (bool)args[1];
			fullReconcileId = (byte)args[2];
			return value;
		}

		/// <summary><c>NetworkBehaviour.MAXIMUM_FULL_RECONCILE_INTERVAL</c>: full reconciles are at most this many ticks apart.</summary>
		public static uint MaximumFullReconcileInterval
		{
			get
			{
				FieldInfo field = typeof(NetworkBehaviour).GetField("MAXIMUM_FULL_RECONCILE_INTERVAL", Any);
				LogAssert.IsNotNull(field, "NetworkBehaviour.MAXIMUM_FULL_RECONCILE_INTERVAL is gone; FishNet's delta reconcile bookkeeping changed and the mirrors in this file must be re-derived from it.");
				return Convert.ToUInt32(field.GetRawConstantValue());
			}
		}

		/// <summary>The bytes FishNet would send for one reconcile, header included.</summary>
		public static int Bytes<T>(T fullReconcile, T value, bool fullSerialize)
		{
			Writer writer = new Writer();
			Write(writer, fullReconcile, value, 1, fullSerialize);
			return writer.Length;
		}

		private static MethodInfo Find(Type type, string name)
		{
			MethodInfo method = type.GetMethod(name, Any);
			LogAssert.IsNotNull(method, $"{type.Name}.{name} was not found. These fixtures drive FishNet's delta prediction " +
				"(feat/delta-prediction-beta) directly; if it moved, re-point them rather than modelling the wire by hand.");
			return method;
		}

		private static object Invoke(MethodInfo method, object target, object[] args)
		{
			try
			{
				return method.Invoke(target, args);
			}
			catch (TargetInvocationException ex) when (ex.InnerException != null)
			{
				ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
				throw;
			}
		}
	}

	/// <summary>
	/// Mirrors the server half of <c>NetworkBehaviour</c>'s delta reconcile bookkeeping:
	/// <c>GetDeltaSerializeOption</c> and <c>Reconcile_WriteDelta</c>.
	/// </summary>
	internal sealed class DeltaReconcileSender<T>
	{
		private readonly uint fullInterval;
		private T baseline;
		private byte fullReconcileId;
		/// <summary><c>_lastFullReconcileWriteTick</c>; 0 is <c>TimeManager.UNSET_TICK</c>.</summary>
		private uint lastFullReconcileTick;

		/// <summary><c>NetworkObject.ObserverAddedTick</c>: set it to the tick an observer is added on.</summary>
		public uint ObserverAddedTick;

		/// <summary>Full reconciles written so far.</summary>
		public int FullCount { get; private set; }

		/// <summary>Reconciles written so far.</summary>
		public int SentCount { get; private set; }

		/// <summary>Bytes written so far, headers included.</summary>
		public int TotalBytes { get; private set; }

		/// <summary>The writer's baseline: the last full reconcile, as readers decode it.</summary>
		public T Baseline => baseline;

		public DeltaReconcileSender(ushort tickRate)
		{
			fullInterval = Math.Min(tickRate, DeltaReconcileWire.MaximumFullReconcileInterval);
		}

		public ArraySegment<byte> Send(T value, uint localTick) => Send(value, localTick, out _);

		public ArraySegment<byte> Send(T value, uint localTick, out bool wasFull)
		{
			// GetDeltaSerializeOption.
			wasFull = lastFullReconcileTick == 0
				|| localTick - lastFullReconcileTick >= fullInterval
				|| ObserverAddedTick >= lastFullReconcileTick;

			// Reconcile_WriteDelta.
			if (wasFull)
			{
				lastFullReconcileTick = localTick;
				fullReconcileId++;
				FullCount++;
			}

			Writer writer = new Writer();
			DeltaReconcileWire.Write(writer, baseline, value, fullReconcileId, wasFull);
			ArraySegment<byte> written = writer.GetArraySegment();

			if (wasFull)
			{
				// The writer decodes its own full reconcile, so a lossy full serializer cannot make it diverge from readers.
				Reader self = new Reader(written, null);
				baseline = DeltaReconcileWire.Read(self, baseline, out _, out _);
			}

			SentCount++;
			TotalBytes += written.Count;
			return written;
		}
	}

	/// <summary>
	/// Mirrors the client half of <c>NetworkBehaviour</c>'s delta reconcile bookkeeping:
	/// <c>Reconcile_ReadDelta</c>.
	/// </summary>
	internal sealed class DeltaReconcileReceiver<T>
	{
		/// <summary><c>_fullReconcileRead</c>: boxed, null until a full reconcile arrives.</summary>
		private object fullReconcile;
		private byte fullReconcileId;
		private uint fullReconcileServerTick;

		public bool HoldsFull => fullReconcile != null;

		/// <summary>The full reconcile deltas are applied to (a copy of the box; arrays are shared, which is what makes immutability testable).</summary>
		public T HeldFull => fullReconcile is T value ? value : default;

		/// <summary>
		/// Reads one reconcile. Returns false when FishNet would discard it: a delta against a full
		/// reconcile this receiver does not hold. The payload is consumed exactly either way.
		/// </summary>
		public bool Receive(ArraySegment<byte> payload, uint serverTick, out T value)
		{
			Reader reader = new Reader(payload, null);
			value = DeltaReconcileWire.Read(reader, HeldFull, out bool isFull, out byte id);
			LogAssert.AreEqual(0, reader.Remaining,
				"A reconcile must be consumed exactly, whether it is used or discarded; the shared state reader continues after it.");

			if (isFull)
			{
				fullReconcile = value;
				fullReconcileId = id;
				fullReconcileServerTick = serverTick;
				return true;
			}

			return fullReconcile != null
				&& id == fullReconcileId
				&& serverTick - fullReconcileServerTick <= DeltaReconcileWire.MaximumFullReconcileInterval * 2;
		}

		/// <summary>Replaces the held full reconcile; stands in for any way a baseline could go wrong.</summary>
		public void CorruptHeldFull(Func<T, T> corrupt)
		{
			fullReconcile = corrupt(HeldFull);
		}
	}

	/// <summary>
	/// End-to-end coverage for the character's delta reconcile on FishNet's delta prediction.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A delta reconcile is decodable only against the full reconcile it was written against, so
	/// the feature's correctness is a property of the <em>sequence</em>, not of any one payload.
	/// These tests run the real production serializers through FishNet's real header code, with the
	/// <c>NetworkBehaviour</c> bookkeeping mirrored around it, and attack the sequence the ways it can
	/// break on an unreliable channel: a peer that starts observing late, a lost delta, a lost FULL
	/// reconcile, reordering, and a baseline that is wrong for any reason at all.
	/// </para>
	/// <para>
	/// Play mode cannot be driven here, so this fixture is the closest available substitute for a
	/// live session.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class ReconcileDeltaChainTests
	{
		/// <summary>Server tick rate, matching <c>TimeManager._tickRate</c> on the scene server.</summary>
		private const ushort ServerTickRate = 30;

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
				MethodInfo register = serializerType.GetMethod("RegisterSerializers",
					BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
				LogAssert.IsNotNull(register, $"{serializerType.Name} must expose a RegisterSerializers hook.");
				register.Invoke(null, null);
			}

			// Without both, FishNet writes the type in full with no header and every test below would pass vacuously.
			LogAssert.IsTrue(DeltaReconcileWire.HasDeltaSerializers<CharacterReconcileData>(),
				"CharacterReconcileData must have a delta writer AND a delta reader, or FishNet never takes the delta path.");
		}

		[Test]
		public void Chain_SixtyTicks_ClientTracksServer()
		{
			DeltaReconcileSender<CharacterReconcileData> server = new DeltaReconcileSender<CharacterReconcileData>(ServerTickRate);
			DeltaReconcileReceiver<CharacterReconcileData> client = new DeltaReconcileReceiver<CharacterReconcileData>();
			CharacterReconcileData authoritative = MakeReconcileData();

			for (uint tick = 1; tick <= ServerTickRate * 2; tick++)
			{
				authoritative = Advance(authoritative, tick);
				LogAssert.IsTrue(client.Receive(server.Send(authoritative, tick), tick, out CharacterReconcileData received),
					$"tick {tick}: nothing was lost, so every reconcile must be usable");
				AssertReconcileEquals(authoritative, received, $"tick {tick}");
			}

			LogAssert.AreEqual(2, server.FullCount, "One full reconcile per second: ticks 1 and 31.");

			Writer regular = new Writer();
			regular.Write(authoritative);
			TestContext.WriteLine(
				$"MEASURE chain of {server.SentCount} reconciles: {server.TotalBytes}B total, " +
				$"{server.TotalBytes / (double)server.SentCount:F1}B/reconcile (header and {server.FullCount} full reconciles included); " +
				$"regular serializer {regular.Length}B/reconcile");
		}

		/// <summary>
		/// The deltas really are relative to the full reconcile: decoded against the wrong one they
		/// come out wrong. Without this, every positive test here could be passing on payloads that
		/// happen to be absolute.
		/// </summary>
		[Test]
		public void Delta_DecodedAgainstTheWrongFull_IsWrong()
		{
			DeltaReconcileSender<CharacterReconcileData> server = new DeltaReconcileSender<CharacterReconcileData>(ServerTickRate);
			CharacterReconcileData authoritative = MakeReconcileData();

			authoritative = Advance(authoritative, 1);
			server.Send(authoritative, 1);
			for (uint tick = 2; tick <= 10; tick++)
			{
				authoritative = Advance(authoritative, tick);
			}
			ArraySegment<byte> delta = server.Send(authoritative, 10);

			CharacterReconcileData wrongFull = server.Baseline;
			wrongFull.MotorState.Position += new Vector3(999f, -999f, 999f);
			wrongFull.ResourceState.Health = -12345f;

			Reader reader = new Reader(delta, null);
			CharacterReconcileData decoded = DeltaReconcileWire.Read(reader, wrongFull, out bool isFull, out _);
			LogAssert.IsFalse(isFull, "Tick 10 is a delta.");
			LogAssert.IsFalse(ReconcileEquals(authoritative, decoded),
				"A delta decoded against a full reconcile the writer did not use must come out wrong. If it does not, the " +
				"payload is not delta-encoded and the guard tests in this fixture prove nothing.");
		}

		[Test]
		public void LateObserver_IsBootstrappedByTheFullReconcileSentWhenItIsAdded()
		{
			DeltaReconcileSender<CharacterReconcileData> server = new DeltaReconcileSender<CharacterReconcileData>(ServerTickRate);
			DeltaReconcileReceiver<CharacterReconcileData> existing = new DeltaReconcileReceiver<CharacterReconcileData>();
			CharacterReconcileData authoritative = MakeReconcileData();

			// The object has been alive a while; the server's full reconcile is far from default.
			for (uint tick = 1; tick <= 12; tick++)
			{
				authoritative = Advance(authoritative, tick);
				existing.Receive(server.Send(authoritative, tick), tick, out _);
			}

			// A new observer, holding nothing, starts receiving at tick 13 — the tick it was added.
			DeltaReconcileReceiver<CharacterReconcileData> late = new DeltaReconcileReceiver<CharacterReconcileData>();
			server.ObserverAddedTick = 13;
			authoritative = Advance(authoritative, 13);
			ArraySegment<byte> joinPayload = server.Send(authoritative, 13, out bool joinWasFull);
			LogAssert.IsTrue(joinWasFull, "An observer added since the last full reconcile must get a full one.");

			LogAssert.IsTrue(late.Receive(joinPayload, 13, out CharacterReconcileData bootstrapped), "A full reconcile is always usable.");
			AssertReconcileEquals(authoritative, bootstrapped, "a late observer must decode the full reconcile from an empty baseline");
			LogAssert.IsTrue(existing.Receive(joinPayload, 13, out CharacterReconcileData existingView),
				"The existing observer takes the extra full reconcile in its stride.");
			AssertReconcileEquals(authoritative, existingView, "existing observer at the join tick");

			for (uint tick = 14; tick <= 40; tick++)
			{
				authoritative = Advance(authoritative, tick);
				ArraySegment<byte> payload = server.Send(authoritative, tick);
				LogAssert.IsTrue(late.Receive(payload, tick, out CharacterReconcileData received), $"late observer at tick {tick}");
				AssertReconcileEquals(authoritative, received, $"late observer at tick {tick}");
				LogAssert.IsTrue(existing.Receive(payload, tick, out received), $"existing observer at tick {tick}");
				AssertReconcileEquals(authoritative, received, $"existing observer at tick {tick}");
			}
		}

		/// <summary>
		/// A peer that starts receiving deltas with no full reconcile — it missed the one sent when
		/// it was added — must use none of them, and must be repaired by the next full one.
		/// </summary>
		[Test]
		public void PeerWithoutAFull_UsesNoDelta_UntilOneArrives()
		{
			DeltaReconcileSender<CharacterReconcileData> server = new DeltaReconcileSender<CharacterReconcileData>(ServerTickRate);
			DeltaReconcileReceiver<CharacterReconcileData> late = new DeltaReconcileReceiver<CharacterReconcileData>();
			CharacterReconcileData authoritative = MakeReconcileData();

			int rejected = 0;
			int wouldHaveBeenWrong = 0;
			for (uint tick = 1; tick < ServerTickRate; tick++)
			{
				authoritative = Advance(authoritative, tick);
				ArraySegment<byte> payload = server.Send(authoritative, tick);
				if (tick < 5)
				{
					continue; // Not yet observing; tick 1 was the full reconcile.
				}
				LogAssert.IsFalse(late.Receive(payload, tick, out CharacterReconcileData decoded),
					$"tick {tick}: a delta with no full reconcile to apply it to must be discarded");
				rejected++;
				if (!ReconcileEquals(authoritative, decoded))
				{
					wouldHaveBeenWrong++;
				}
			}
			LogAssert.IsTrue(wouldHaveBeenWrong > 0,
				"Decoded against the empty baseline, the discarded deltas must be wrong; otherwise discarding them proves nothing.");

			authoritative = Advance(authoritative, ServerTickRate + 1);
			LogAssert.IsTrue(late.Receive(server.Send(authoritative, ServerTickRate + 1, out bool full), ServerTickRate + 1, out CharacterReconcileData repaired) && full,
				"The periodic full reconcile is usable.");
			AssertReconcileEquals(authoritative, repaired, "the periodic full reconcile bootstraps the peer");

			TestContext.WriteLine($"MEASURE peer without a full reconcile: {rejected} deltas discarded ({wouldHaveBeenWrong} would have applied a wrong state)");
		}

		/// <summary>
		/// The design's main property: a lost delta costs only itself, because the next delta is
		/// written against the full reconcile, not against the delta that was lost.
		/// </summary>
		[Test]
		public void LostDelta_CostsOnlyItself()
		{
			DeltaReconcileSender<CharacterReconcileData> server = new DeltaReconcileSender<CharacterReconcileData>(ServerTickRate);
			DeltaReconcileReceiver<CharacterReconcileData> client = new DeltaReconcileReceiver<CharacterReconcileData>();
			CharacterReconcileData authoritative = MakeReconcileData();

			for (uint tick = 1; tick <= 5; tick++)
			{
				authoritative = Advance(authoritative, tick);
				client.Receive(server.Send(authoritative, tick), tick, out _);
			}

			// Ticks 6, 9 and 10 are lost, and 15 (a lumpy tick: buffs, attributes, RNG, exposure) too.
			for (uint tick = 6; tick < ServerTickRate; tick++)
			{
				authoritative = Advance(authoritative, tick);
				ArraySegment<byte> payload = server.Send(authoritative, tick);
				if (tick == 6 || tick == 9 || tick == 10 || tick == 15)
				{
					continue;
				}
				LogAssert.IsTrue(client.Receive(payload, tick, out CharacterReconcileData received),
					$"tick {tick}: a delta after a lost delta is still against the full reconcile this peer holds");
				AssertReconcileEquals(authoritative, received, $"tick {tick} after a lost delta");
			}
		}

		/// <summary>
		/// Losing a FULL reconcile is what costs time: every delta written against it is discarded
		/// — not decoded against the previous full one — until the next full reconcile.
		/// </summary>
		[Test]
		public void LostFull_DiscardsItsDeltas_UntilTheNextFull()
		{
			DeltaReconcileSender<CharacterReconcileData> server = new DeltaReconcileSender<CharacterReconcileData>(ServerTickRate);
			DeltaReconcileReceiver<CharacterReconcileData> client = new DeltaReconcileReceiver<CharacterReconcileData>();
			CharacterReconcileData authoritative = MakeReconcileData();

			for (uint tick = 1; tick <= ServerTickRate; tick++)
			{
				authoritative = Advance(authoritative, tick);
				client.Receive(server.Send(authoritative, tick), tick, out _);
			}

			// Tick 31 is the next full reconcile, and it is lost.
			authoritative = Advance(authoritative, ServerTickRate + 1);
			server.Send(authoritative, ServerTickRate + 1, out bool lostWasFull);
			LogAssert.IsTrue(lostWasFull, "Tick 31 must be the periodic full reconcile for this scenario to mean anything.");

			int discarded = 0;
			int wouldHaveBeenWrong = 0;
			for (uint tick = ServerTickRate + 2; tick <= ServerTickRate * 2; tick++)
			{
				authoritative = Advance(authoritative, tick);
				LogAssert.IsFalse(client.Receive(server.Send(authoritative, tick), tick, out CharacterReconcileData decoded),
					$"tick {tick}: written against the lost full reconcile, so it must be discarded rather than applied");
				discarded++;
				if (!ReconcileEquals(authoritative, decoded))
				{
					wouldHaveBeenWrong++;
				}
			}
			LogAssert.IsTrue(wouldHaveBeenWrong > 0,
				"Decoded against the older full reconcile the discarded deltas must be wrong, or the id check is not what protects this peer.");

			// Tick 61: the next full reconcile, and everything after it, is usable again.
			for (uint tick = ServerTickRate * 2 + 1; tick <= ServerTickRate * 2 + 6; tick++)
			{
				authoritative = Advance(authoritative, tick);
				LogAssert.IsTrue(client.Receive(server.Send(authoritative, tick), tick, out CharacterReconcileData received),
					$"tick {tick} after the next full reconcile");
				AssertReconcileEquals(authoritative, received, $"tick {tick} after the next full reconcile");
			}

			TestContext.WriteLine(
				$"MEASURE one lost full reconcile: {discarded} deltas discarded ({wouldHaveBeenWrong} would have applied a wrong state); " +
				$"worst-case uncorrected window = {discarded * 1000 / ServerTickRate} ms");
		}

		/// <summary>
		/// Reordering within one full reconcile's deltas is harmless, since each stands alone against
		/// the full. Reordering across a full reconcile discards what cannot be decoded.
		/// </summary>
		[Test]
		public void ReorderedDelivery_IsDecodedOrDiscarded_NeverMisapplied()
		{
			DeltaReconcileSender<CharacterReconcileData> server = new DeltaReconcileSender<CharacterReconcileData>(ServerTickRate);
			DeltaReconcileReceiver<CharacterReconcileData> client = new DeltaReconcileReceiver<CharacterReconcileData>();
			CharacterReconcileData authoritative = MakeReconcileData();

			for (uint tick = 1; tick <= 4; tick++)
			{
				authoritative = Advance(authoritative, tick);
				client.Receive(server.Send(authoritative, tick), tick, out _);
			}

			authoritative = Advance(authoritative, 5);
			CharacterReconcileData atFive = authoritative;
			ArraySegment<byte> five = server.Send(authoritative, 5);
			authoritative = Advance(authoritative, 6);
			CharacterReconcileData atSix = authoritative;
			ArraySegment<byte> six = server.Send(authoritative, 6);

			LogAssert.IsTrue(client.Receive(six, 6, out CharacterReconcileData received), "six arrives first and is usable");
			AssertReconcileEquals(atSix, received, "six, early");
			LogAssert.IsTrue(client.Receive(five, 5, out received), "five arrives late and is still usable");
			AssertReconcileEquals(atFive, received, "five, late");

			// Across a full reconcile: tick 29's delta (against full 1) and tick 32's (against full 31).
			ArraySegment<byte> twentyNine = default;
			ArraySegment<byte> thirtyOne = default;
			ArraySegment<byte> thirtyTwo = default;
			CharacterReconcileData atThirtyTwo = default;
			for (uint tick = 7; tick <= 32; tick++)
			{
				authoritative = Advance(authoritative, tick);
				ArraySegment<byte> payload = server.Send(authoritative, tick);
				if (tick == 29) twentyNine = payload;
				if (tick == 31) thirtyOne = payload;
				if (tick == 32) { thirtyTwo = payload; atThirtyTwo = authoritative; }
			}

			LogAssert.IsFalse(client.Receive(thirtyTwo, 32, out _), "A delta that overtakes its own full reconcile must be discarded.");
			LogAssert.IsTrue(client.Receive(thirtyOne, 31, out _), "The full reconcile, late, is usable.");
			LogAssert.IsFalse(client.Receive(twentyNine, 29, out _),
				"A delta against the previous full reconcile, arriving after the new one, must be discarded.");
			LogAssert.IsTrue(client.Receive(thirtyTwo, 32, out received), "A duplicate of tick 32 now decodes.");
			AssertReconcileEquals(atThirtyTwo, received, "tick 32 against full 31");
		}

		[Test]
		public void DriftedBaseline_IsRepairedByTheNextFull()
		{
			DeltaReconcileSender<CharacterReconcileData> server = new DeltaReconcileSender<CharacterReconcileData>(ServerTickRate);
			DeltaReconcileReceiver<CharacterReconcileData> client = new DeltaReconcileReceiver<CharacterReconcileData>();
			CharacterReconcileData authoritative = MakeReconcileData();

			for (uint tick = 1; tick <= 10; tick++)
			{
				authoritative = Advance(authoritative, tick);
				client.Receive(server.Send(authoritative, tick), tick, out _);
			}

			/* Corrupt the held full reconcile outright — a stand-in for any way it could go wrong that
			 * these tests have not thought of. The id still matches, so the guard cannot catch it: the
			 * guarantee asserted is recovery, not immunity. */
			client.CorruptHeldFull(full =>
			{
				full.MotorState.Position += new Vector3(999f, -999f, 999f);
				full.ResourceState.Health = -12345f;
				return full;
			});

			authoritative = Advance(authoritative, 11);
			client.Receive(server.Send(authoritative, 11), 11, out CharacterReconcileData corrupt);
			LogAssert.IsFalse(ReconcileEquals(authoritative, corrupt),
				"a delta decoded against a corrupted full reconcile is expected to be wrong — if this passes, the payload " +
				"is not actually delta-encoded and the test proves nothing.");

			for (uint tick = 12; tick <= ServerTickRate + 1; tick++)
			{
				authoritative = Advance(authoritative, tick);
				ArraySegment<byte> payload = server.Send(authoritative, tick, out bool full);
				client.Receive(payload, tick, out CharacterReconcileData received);
				if (full)
				{
					AssertReconcileEquals(authoritative, received, "the periodic full reconcile must repair a drifted baseline");
				}
			}

			for (uint tick = ServerTickRate + 2; tick <= ServerTickRate + 8; tick++)
			{
				authoritative = Advance(authoritative, tick);
				LogAssert.IsTrue(client.Receive(server.Send(authoritative, tick), tick, out CharacterReconcileData received), $"tick {tick}");
				AssertReconcileEquals(authoritative, received, $"tick {tick} after the repair");
			}
		}

		/// <summary>
		/// A discarded delta consumes exactly its own bytes, so whatever FishNet packed after it in
		/// the StateUpdate is still readable — the contract "consume the same bytes whatever the
		/// baseline", checked against the two wrong baselines a peer can hold: none, and a stale one.
		/// </summary>
		[Test]
		public void DiscardedDelta_ConsumesItsPayloadExactly()
		{
			DeltaReconcileSender<CharacterReconcileData> server = new DeltaReconcileSender<CharacterReconcileData>(ServerTickRate);
			CharacterReconcileData authoritative = MakeReconcileData();

			authoritative = Advance(authoritative, 1);
			server.Send(authoritative, 1);
			CharacterReconcileData staleFull = server.Baseline;
			for (uint tick = 2; tick <= ServerTickRate + 1; tick++)
			{
				authoritative = Advance(authoritative, tick);
				server.Send(authoritative, tick);
			}
			// Lumpy: buffs, attributes, RNG, cooldowns, ability id, exposure all move against full 31.
			authoritative = Advance(authoritative, ServerTickRate + 5);
			authoritative = Advance(authoritative, ServerTickRate + 9);
			authoritative.Buffs = new[] { authoritative.Buffs[0], new BuffReconcileEntry { TemplateID = 12, ExpiryTick = 900, Stacks = 2 } };

			const int Sentinel = 0x5EED;
			foreach (CharacterReconcileData heldBaseline in new[] { default(CharacterReconcileData), staleFull })
			{
				Writer writer = new Writer();
				DeltaReconcileWire.Write(writer, server.Baseline, authoritative, 2, false);
				writer.WriteInt32(Sentinel);

				Reader reader = new Reader(writer.GetArraySegment(), null);
				DeltaReconcileWire.Read(reader, heldBaseline, out bool isFull, out _);
				LogAssert.IsFalse(isFull, "This is a delta.");
				LogAssert.AreEqual(Sentinel, reader.ReadInt32(),
					"A delta decoded against the wrong baseline must still leave the reader positioned exactly after its own payload.");
			}
		}

		/// <summary>
		/// "Never modify the baseline instance": one full reconcile is the baseline for up to a
		/// second of deltas on both ends, and the arrays inside it are shared by reference.
		/// </summary>
		[Test]
		public void FullReconcile_IsNotModifiedByTheDeltasAppliedToIt()
		{
			DeltaReconcileSender<CharacterReconcileData> server = new DeltaReconcileSender<CharacterReconcileData>(ServerTickRate);
			DeltaReconcileReceiver<CharacterReconcileData> client = new DeltaReconcileReceiver<CharacterReconcileData>();
			CharacterReconcileData authoritative = MakeReconcileData();

			authoritative = Advance(authoritative, 1);
			client.Receive(server.Send(authoritative, 1), 1, out _);
			CharacterReconcileData readerFullCopy = DeepCopy(client.HeldFull);
			CharacterReconcileData writerFullCopy = DeepCopy(server.Baseline);

			for (uint tick = 2; tick < ServerTickRate; tick++)
			{
				authoritative = Advance(authoritative, tick);
				// Same-length arrays, so the index-delta path patches entries of a copy of the baseline.
				authoritative.Attributes[tick % 3].Value = (int)tick;
				authoritative.Cooldowns[0].DurationTicks = 60 + tick;
				LogAssert.IsTrue(client.Receive(server.Send(authoritative, tick), tick, out CharacterReconcileData received), $"tick {tick}");
				AssertReconcileEquals(authoritative, received, $"tick {tick}");
			}

			AssertReconcileEquals(readerFullCopy, client.HeldFull, "the reader's full reconcile after 28 deltas");
			LogAssert.IsTrue(ArrayEquals(readerFullCopy.Attributes, client.HeldFull.Attributes) && ArrayEquals(readerFullCopy.Exposure, client.HeldFull.Exposure),
				"A delta reader patched the full reconcile's arrays in place.");
			AssertReconcileEquals(writerFullCopy, server.Baseline, "the writer's full reconcile after 28 deltas");
		}

		/// <summary>
		/// A slow turn reaches the owner. The 4.6.12 quaternion delta ignored changes under a fixed
		/// dead zone and rebuilt the most-CHANGED component from a square root, so a slow yaw from
		/// near identity decoded as no turn at all.
		/// </summary>
		[Test]
		public void SlowTurn_IsReconciledEveryTick()
		{
			DeltaReconcileSender<CharacterReconcileData> server = new DeltaReconcileSender<CharacterReconcileData>(ServerTickRate);
			DeltaReconcileReceiver<CharacterReconcileData> client = new DeltaReconcileReceiver<CharacterReconcileData>();
			CharacterReconcileData authoritative = MakeReconcileData();

			float worst = 0f;
			for (uint tick = 1; tick <= ServerTickRate * 2; tick++)
			{
				authoritative.MotorState.Rotation = Quaternion.Euler(0f, tick * 0.1f, 0f);
				LogAssert.IsTrue(client.Receive(server.Send(authoritative, tick), tick, out CharacterReconcileData received), $"tick {tick}");
				float error = Quaternion.Angle(authoritative.MotorState.Rotation, received.MotorState.Rotation);
				worst = Mathf.Max(worst, error);
			}

			TestContext.WriteLine($"MEASURE 0.1 degree/tick turn over {ServerTickRate * 2} reconciles: worst error {worst:F3} degrees");
			LogAssert.IsTrue(worst < 0.1f, $"A slow turn reconciled {worst:F3} degrees away from the server; it must track within a tenth of a degree.");
		}

		// ── Helpers ──────────────────────────────────────────────────────────

		/// <summary>Moves the authoritative snapshot on by one tick's worth of plausible change.</summary>
		internal static CharacterReconcileData Advance(CharacterReconcileData d, uint tick)
		{
			CharacterReconcileData next = DeepCopy(d);

			next.MotorState.Position += new Vector3(0.11f, 0f, 0.03f);
			next.MotorState.BaseVelocity = new Vector3(3.3f, 0f, 0.9f);
			next.MotorState.Rotation = Quaternion.Euler(0f, tick * 1.5f, 0f);
			next.ResourceState.Health = Mathf.Max(1f, d.ResourceState.Health - 0.5f);
			next.ResourceState.NextRegenTick = 900 + tick;
			next.RemainingTicks = tick % 7;

			// Periodic lumpier changes, so the chain is not uniformly tiny deltas.
			if (tick % 5 == 0)
			{
				next.Buffs[0].Stacks = (int)(tick % 4) + 1;
				next.Attributes[1].ExternalModifier = (int)(tick % 11);
				next.RngS0 = 0x1000_0000u + tick;
				next.RngS1 = 0x2000_0000u + tick;
				next.Exposure[0].Level = (ushort)(tick * 1000 % 65536);
			}
			if (tick % 9 == 0)
			{
				next.Cooldowns[0].StartTick = 100 + tick;
				next.AbilityID = 8800 + tick;
			}
			return next;
		}

		/// <summary>A copy that shares no array with <paramref name="d"/>.</summary>
		internal static CharacterReconcileData DeepCopy(CharacterReconcileData d)
		{
			d.Cooldowns = (CooldownReconcileEntry[])d.Cooldowns?.Clone();
			d.Buffs = (BuffReconcileEntry[])d.Buffs?.Clone();
			d.Equipment = (EquipmentReconcileEntry[])d.Equipment?.Clone();
			d.Attributes = (AttributeReconcileEntry[])d.Attributes?.Clone();
			d.Exposure = (ExposureReconcileEntry[])d.Exposure?.Clone();
			return d;
		}

		internal static bool ReconcileEquals(CharacterReconcileData a, CharacterReconcileData b)
		{
			if (a.AbilityID != b.AbilityID || a.RemainingTicks != b.RemainingTicks ||
				a.Seed != b.Seed || a.PackedFlagsAndSlot != b.PackedFlagsAndSlot ||
				a.ChargedHoldTicks != b.ChargedHoldTicks ||
				a.RngS0 != b.RngS0 || a.RngS1 != b.RngS1 || a.RngS2 != b.RngS2 || a.RngS3 != b.RngS3)
			{
				return false;
			}
			if (a.ResourceState.MaxHealth != b.ResourceState.MaxHealth ||
				a.ResourceState.MaxMana != b.ResourceState.MaxMana ||
				a.ResourceState.MaxStamina != b.ResourceState.MaxStamina ||
				a.ResourceState.NextRegenTick != b.ResourceState.NextRegenTick ||
				Mathf.Abs(a.ResourceState.Health - b.ResourceState.Health) > 0.01f ||
				Mathf.Abs(a.ResourceState.Mana - b.ResourceState.Mana) > 0.01f ||
				Mathf.Abs(a.ResourceState.Stamina - b.ResourceState.Stamina) > 0.01f)
			{
				return false;
			}
			// Position and rotation ride quantised delta writers, so compare with the codebase's tolerance.
			if (Vector3.Distance(a.MotorState.Position, b.MotorState.Position) > 0.05f ||
				Quaternion.Angle(a.MotorState.Rotation, b.MotorState.Rotation) > 1.0f ||
				Vector3.Distance(a.MotorState.BaseVelocity, b.MotorState.BaseVelocity) > 0.05f)
			{
				return false;
			}
			if (a.MotorState.IsCrouching != b.MotorState.IsCrouching ||
				a.MotorState.JumpRequested != b.MotorState.JumpRequested ||
				a.MotorState.GroundingStatus.IsStableOnGround != b.MotorState.GroundingStatus.IsStableOnGround)
			{
				return false;
			}
			return ArrayEquals(a.Cooldowns, b.Cooldowns) && ArrayEquals(a.Buffs, b.Buffs)
				&& ArrayEquals(a.Equipment, b.Equipment) && ArrayEquals(a.Attributes, b.Attributes)
				&& ArrayEquals(a.Exposure, b.Exposure);
		}

		internal static bool ArrayEquals<T>(T[] a, T[] b) where T : IEquatable<T>
		{
			int aLen = a?.Length ?? 0;
			int bLen = b?.Length ?? 0;
			if (aLen != bLen)
			{
				return false;
			}
			for (int i = 0; i < aLen; i++)
			{
				if (!a[i].Equals(b[i]))
				{
					return false;
				}
			}
			return true;
		}

		internal static void AssertReconcileEquals(CharacterReconcileData expected, CharacterReconcileData actual, string context)
		{
			LogAssert.IsTrue(ReconcileEquals(expected, actual),
				$"Reconcile mismatch ({context}). " +
				$"pos {expected.MotorState.Position}/{actual.MotorState.Position} " +
				$"hp {expected.ResourceState.Health}/{actual.ResourceState.Health} " +
				$"abilityId {expected.AbilityID}/{actual.AbilityID} " +
				$"remainingTicks {expected.RemainingTicks}/{actual.RemainingTicks} " +
				$"rng0 {expected.RngS0}/{actual.RngS0} " +
				$"exposure0 {(expected.Exposure?.Length > 0 ? expected.Exposure[0].Level : -1)}/{(actual.Exposure?.Length > 0 ? actual.Exposure[0].Level : -1)}");
		}

		/// <summary>
		/// A geared, buffed character with weather exposure states — every array in the payload
		/// populated, so every field's wire form is exercised. Exposure was added to the reconcile by
		/// weather P4 and the fixtures that predate it never set it.
		/// </summary>
		internal static CharacterReconcileData MakeReconcileData()
		{
			CharacterReconcileData d = default;
			d.MotorState = default;
			d.MotorState.Position = new Vector3(112.5f, 30.9f, -47.25f);
			d.MotorState.Rotation = Quaternion.identity;
			d.MotorState.GroundingStatus = default;
			d.MotorState.GroundingStatus.FoundAnyGround = true;
			d.MotorState.GroundingStatus.IsStableOnGround = true;
			d.MotorState.GroundingStatus.GroundNormal = Vector3.up;
			d.Seed = 4242;
			d.PackedFlagsAndSlot = 0x1234;
			d.ChargedHoldTicks = 3;
			d.ResourceState = default;
			d.ResourceState.MaxHealth = 1200; d.ResourceState.Health = 1200f;
			d.ResourceState.MaxMana = 800; d.ResourceState.Mana = 800f;
			d.ResourceState.MaxStamina = 400; d.ResourceState.Stamina = 400f;
			d.ResourceState.NextRegenTick = 900;
			d.RngS0 = 0xDEADBEEF; d.RngS1 = 0x12345678;
			d.RngS2 = 0x0BADF00D; d.RngS3 = 0xFEEDFACE;
			d.Cooldowns = new[] { new CooldownReconcileEntry { AbilityID = 42, StartTick = 100, DurationTicks = 60 } };
			d.Buffs = new[] { new BuffReconcileEntry { TemplateID = 3, ExpiryTick = 500, NextTickTick = 20, Stacks = 1, TickCount = 4, CumulativeTickMultiplier = 1 } };
			d.Equipment = new[]
			{
				new EquipmentReconcileEntry { TemplateID = 5, Slot = 1, Seed = 77, ItemID = 900 },
				new EquipmentReconcileEntry { TemplateID = 6, Slot = 2, Seed = 78, ItemID = 901 },
			};
			d.Attributes = new[]
			{
				new AttributeReconcileEntry { TemplateID = 1, Value = 25, ExternalModifier = 4 },
				new AttributeReconcileEntry { TemplateID = 2, Value = 31, ExternalModifier = 0 },
				new AttributeReconcileEntry { TemplateID = 3, Value = 18, ExternalModifier = 6 },
			};
			d.Exposure = new[]
			{
				new ExposureReconcileEntry { TemplateID = 7, Level = ExposureReconcileEntry.Quantise(0.25f) },
				new ExposureReconcileEntry { TemplateID = 11, Level = 0 },
			};
			return d;
		}
	}
}
