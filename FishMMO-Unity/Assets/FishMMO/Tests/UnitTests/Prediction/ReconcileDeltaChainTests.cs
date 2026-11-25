using System;
using System.Reflection;
using System.Runtime.ExceptionServices;
using NUnit.Framework;
using FishMMO.Shared;
using FishNet.Object;
using FishNet.Object.Prediction;
using FishNet.Serializing;
using UnityEngine;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// FishNet's delta reconcile wire (<c>FISHNET_DELTA_PREDICTION</c>, feat/delta-prediction-beta and
	/// feat/acknowledged-delta-reconciles), driven through its real code.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The bookkeeping (ids, baselines, acknowledgements) is FishNet's own <see cref="ReconcileDeltaWriter{T}"/> and
	/// <see cref="ReconcileDeltaReader{T}"/>, the classes NetworkBehaviour uses, so nothing here is a hand-copied mirror.
	/// The header methods (<c>Writer.WriteDeltaReconcile</c>, <c>Reader.ReadDeltaReconcileHeader</c>/<c>Body</c>,
	/// <c>Writer.HasDeltaSerializers</c>) are <c>internal</c> to FishNet.Runtime and reached by reflection, for the few
	/// tests that write a payload by hand.
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

		/// <summary><c>Writer.WriteDeltaReconcile</c>: the header, then the regular serializer (distance 0) or a delta against <paramref name="baseline"/>.</summary>
		public static void Write<T>(Writer writer, T baseline, T value, byte id, byte baselineDistance)
		{
			Invoke(Find(typeof(Writer), "WriteDeltaReconcile").MakeGenericMethod(typeof(T)), writer,
				new object[] { baseline, value, id, baselineDistance });
		}

		/// <summary><c>Reader.ReadDeltaReconcileHeader</c> then <c>ReadDeltaReconcileBody</c> against <paramref name="baseline"/>.</summary>
		public static T Read<T>(Reader reader, T baseline, out bool isFull, out byte id, out byte baselineDistance)
		{
			object[] header = { null, null, null };
			Invoke(Find(typeof(Reader), "ReadDeltaReconcileHeader"), reader, header);
			isFull = (bool)header[0];
			id = (byte)header[1];
			baselineDistance = (byte)header[2];
			return (T)Invoke(Find(typeof(Reader), "ReadDeltaReconcileBody").MakeGenericMethod(typeof(T)), reader, new object[] { baseline, isFull });
		}

		/// <summary><c>NetworkBehaviour.MAXIMUM_FULL_RECONCILE_INTERVAL</c>: without acknowledgements, full reconciles are at most this many ticks apart.</summary>
		public static uint MaximumFullReconcileInterval
		{
			get
			{
				FieldInfo field = typeof(NetworkBehaviour).GetField("MAXIMUM_FULL_RECONCILE_INTERVAL", Any);
				LogAssert.IsNotNull(field, "NetworkBehaviour.MAXIMUM_FULL_RECONCILE_INTERVAL is gone; FishNet's delta reconcile bookkeeping changed.");
				return Convert.ToUInt32(field.GetRawConstantValue());
			}
		}

		/// <summary>The bytes FishNet would send for one reconcile, header included: in full, or as a delta against <paramref name="baseline"/>.</summary>
		public static int Bytes<T>(T baseline, T value, bool fullSerialize)
		{
			Writer writer = new Writer();
			Write(writer, baseline, value, 1, fullSerialize ? (byte)0 : (byte)1);
			return writer.Length;
		}

		private static MethodInfo Find(Type type, string name)
		{
			MethodInfo method = type.GetMethod(name, Any);
			LogAssert.IsNotNull(method, $"{type.Name}.{name} was not found. These fixtures drive FishNet's delta prediction " +
				"directly; if it moved, re-point them rather than modelling the wire by hand.");
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
	/// The server half, as NetworkBehaviour.Reconcile_WriteDelta drives FishNet's <see cref="ReconcileDeltaWriter{T}"/>.
	/// </summary>
	internal sealed class DeltaReconcileSender<T>
	{
		private readonly ReconcileDeltaWriter<T> writer = new ReconcileDeltaWriter<T>();
		private readonly uint fullInterval;
		private readonly uint refreshInterval;
		private readonly bool acknowledged;
		/// <summary><c>_lastFullReconcileWriteTick</c>; 0 is <c>TimeManager.UNSET_TICK</c>.</summary>
		private uint lastFullReconcileTick;

		/// <summary><c>NetworkObject.ObserverAddedTick</c>, honoured as with state forwarding: set it to the tick an observer is added on.</summary>
		public uint ObserverAddedTick;

		/// <summary>Full reconciles written so far.</summary>
		public int FullCount { get; private set; }

		/// <summary>Reconciles written so far.</summary>
		public int SentCount { get; private set; }

		/// <summary>Bytes written so far, headers included.</summary>
		public int TotalBytes { get; private set; }

		/// <summary>The last full reconcile written, as readers decode it.</summary>
		public T Baseline => writer.TryGetWritten(writer.LastFullId, out T value) ? value : default;

		/// <summary>Id of the last full reconcile written.</summary>
		public int LastFullId => writer.LastFullId;

		/// <summary>The writer, for acknowledgements.</summary>
		public ReconcileDeltaWriter<T> DeltaWriter => writer;

		/// <param name="acknowledged">
		/// True for an owner that acknowledges (NetworkBehaviour without state forwarding); false for state forwarding,
		/// where no acknowledgement is used and deltas are written against the last full reconcile.
		/// </param>
		public DeltaReconcileSender(ushort tickRate, bool acknowledged = false)
		{
			fullInterval = Math.Min(tickRate, DeltaReconcileWire.MaximumFullReconcileInterval);
			refreshInterval = tickRate * 10u;
			this.acknowledged = acknowledged;
		}

		public ArraySegment<byte> Send(T value, uint localTick) => Send(value, localTick, out _);

		public ArraySegment<byte> Send(T value, uint localTick, out bool wasFull)
		{
			bool forceFull = !acknowledged && ObserverAddedTick != 0 && ObserverAddedTick >= lastFullReconcileTick;
			FishNet.Serializing.Writer output = new FishNet.Serializing.Writer();
			writer.Write(output, value, localTick, acknowledged, fullInterval, refreshInterval, forceFull);
			wasFull = writer.LastWasFull;
			if (wasFull)
			{
				lastFullReconcileTick = localTick;
				FullCount++;
			}
			ArraySegment<byte> written = output.GetArraySegment();
			SentCount++;
			TotalBytes += written.Count;
			return written;
		}
	}

	/// <summary>
	/// The client half, as NetworkBehaviour.Reconcile_ReadDelta drives FishNet's <see cref="ReconcileDeltaReader{T}"/>.
	/// </summary>
	internal sealed class DeltaReconcileReceiver<T>
	{
		private readonly ReconcileDeltaReader<T> reader = new ReconcileDeltaReader<T>();

		/// <summary>True once any reconcile has been decoded.</summary>
		public bool HoldsFull => reader.NewestId >= 0;

		/// <summary>The reader, for acknowledgements.</summary>
		public ReconcileDeltaReader<T> DeltaReader => reader;

		/// <summary>What this reader decoded for <paramref name="id"/>.</summary>
		public T Held(int id) => reader.TryGetDecoded(id, out T value) ? value : default;

		/// <summary>
		/// Reads one reconcile. Returns false when FishNet would discard it: a delta against a reconcile this receiver does
		/// not hold. The payload is consumed exactly either way.
		/// </summary>
		public bool Receive(ArraySegment<byte> payload, uint serverTick, out T value)
		{
			FishNet.Serializing.Reader input = new FishNet.Serializing.Reader(payload, null);
			bool usable = reader.Read(input, serverTick, out value);
			LogAssert.AreEqual(0, input.Remaining,
				"A reconcile must be consumed exactly, whether it is used or discarded; the shared state reader continues after it.");
			return usable;
		}

		/// <summary>Replaces what this reader holds for <paramref name="id"/>; stands in for any way a baseline could go wrong.</summary>
		public void Corrupt(int id, Func<T, T> corrupt)
		{
			FieldInfo field = typeof(ReconcileDeltaReader<T>).GetField("_decoded", BindingFlags.Instance | BindingFlags.NonPublic);
			LogAssert.IsNotNull(field, "ReconcileDeltaReader<T>._decoded is gone.");
			T[] decoded = (T[])field.GetValue(reader);
			decoded[id] = corrupt(decoded[id]);
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
		/// The swim flag and a climb out of the water (KinematicCharacterMotorState bits 14 and 15) reach the owner on
		/// both the full reconcile and the deltas: a swimmer the server says is swimming replays as one.
		/// </summary>
		[Test]
		public void Swimming_AndAClimbOut_RideTheChain()
		{
			DeltaReconcileSender<CharacterReconcileData> server = new DeltaReconcileSender<CharacterReconcileData>(ServerTickRate);
			DeltaReconcileReceiver<CharacterReconcileData> client = new DeltaReconcileReceiver<CharacterReconcileData>();
			CharacterReconcileData authoritative = MakeReconcileData();

			for (uint tick = 1; tick <= 45; tick++)
			{
				authoritative = Advance(authoritative, tick);
				authoritative.MotorState.IsSwimming = tick >= 5 && tick < 40;
				authoritative.MotorState.ClimbOutTicks = tick >= 26 && tick < 38 ? (byte)(38 - tick) : (byte)0;
				LogAssert.IsTrue(client.Receive(server.Send(authoritative, tick), tick, out CharacterReconcileData received),
					$"tick {tick}: nothing was lost");
				LogAssert.AreEqual(authoritative.MotorState.IsSwimming, received.MotorState.IsSwimming, $"tick {tick}: swimming");
				LogAssert.AreEqual(authoritative.MotorState.ClimbOutTicks, received.MotorState.ClimbOutTicks, $"tick {tick}: climb");
			}
		}

		/// <summary>
		/// A climb (the climbable's index, the phase and the step's ticks: the motor state's extension behind bit 4) reaches
		/// the owner on both the full reconcile and the deltas, and the fields after it stay aligned.
		/// </summary>
		[Test]
		public void AClimb_RidesTheChain()
		{
			DeltaReconcileSender<CharacterReconcileData> server = new DeltaReconcileSender<CharacterReconcileData>(ServerTickRate);
			DeltaReconcileReceiver<CharacterReconcileData> client = new DeltaReconcileReceiver<CharacterReconcileData>();
			CharacterReconcileData authoritative = MakeReconcileData();

			for (uint tick = 1; tick <= 70; tick++)
			{
				authoritative = Advance(authoritative, tick);
				bool climbing = tick >= 5 && tick < 50;
				authoritative.MotorState.Climbable = climbing ? (ushort)312 : (ushort)0;
				authoritative.MotorState.ClimbPhase = !climbing ? (byte)0 : tick < 40 ? (byte)1 : (byte)2;
				authoritative.MotorState.ClimbTicks = tick >= 40 && tick < 50 ? (byte)(75 - (tick - 40)) : tick >= 50 && tick < 62 ? (byte)(62 - tick) : (byte)0;
				authoritative.MotorState.IsCrouching = tick % 7 == 0;
				LogAssert.IsTrue(client.Receive(server.Send(authoritative, tick), tick, out CharacterReconcileData received),
					$"tick {tick}: nothing was lost");
				LogAssert.AreEqual(authoritative.MotorState.Climbable, received.MotorState.Climbable, $"tick {tick}: climbable");
				LogAssert.AreEqual(authoritative.MotorState.ClimbPhase, received.MotorState.ClimbPhase, $"tick {tick}: phase");
				LogAssert.AreEqual(authoritative.MotorState.ClimbTicks, received.MotorState.ClimbTicks, $"tick {tick}: ticks");
				LogAssert.IsTrue(ReconcileEquals(authoritative, received), $"tick {tick}: the rest of the reconcile");
			}
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
			CharacterReconcileData decoded = DeltaReconcileWire.Read(reader, wrongFull, out bool isFull, out _, out _);
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
			CharacterReconcileData atTwentyNine = default;
			CharacterReconcileData atThirtyTwo = default;
			for (uint tick = 7; tick <= 32; tick++)
			{
				authoritative = Advance(authoritative, tick);
				ArraySegment<byte> payload = server.Send(authoritative, tick);
				if (tick == 29) { twentyNine = payload; atTwentyNine = authoritative; }
				if (tick == 31) thirtyOne = payload;
				if (tick == 32) { thirtyTwo = payload; atThirtyTwo = authoritative; }
			}

			LogAssert.IsFalse(client.Receive(thirtyTwo, 32, out _), "A delta that overtakes its own full reconcile must be discarded.");
			LogAssert.IsTrue(client.Receive(thirtyOne, 31, out _), "The full reconcile, late, is usable.");
			/* The reader keeps every recent reconcile by id, not only the newest full one, so a late delta against the
			 * previous full reconcile still names a baseline it holds and decodes to exactly what was written. That it is
			 * older than what the owner already applied is Reconcile_Reader_Remote's tick check to refuse, not this one's. */
			LogAssert.IsTrue(client.Receive(twentyNine, 29, out received),
				"A delta against the previous full reconcile, arriving after the new one, names a baseline still held.");
			AssertReconcileEquals(atTwentyNine, received, "tick 29 against full 1, late");
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
			client.Corrupt(server.LastFullId, full =>
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
				DeltaReconcileWire.Write(writer, server.Baseline, authoritative, 2, 1);
				writer.WriteInt32(Sentinel);

				Reader reader = new Reader(writer.GetArraySegment(), null);
				DeltaReconcileWire.Read(reader, heldBaseline, out bool isFull, out _, out _);
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
			CharacterReconcileData readerFullCopy = DeepCopy(client.Held(server.LastFullId));
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

			CharacterReconcileData readerFull = client.Held(server.LastFullId);
			AssertReconcileEquals(readerFullCopy, readerFull, "the reader's full reconcile after 28 deltas");
			LogAssert.IsTrue(ArrayEquals(readerFullCopy.Attributes, readerFull.Attributes) && ArrayEquals(readerFullCopy.Exposure, readerFull.Exposure),
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

		// ── Acknowledged deltas (feat/acknowledged-delta-reconciles) ──────────

		/// <summary>Hands the owner's acknowledgement to the server, as the replicate the owner sends would.</summary>
		private static void DeliverAcknowledgement(byte acknowledgement, ReconcileDeltaWriter<CharacterReconcileData> writer)
		{
			if ((acknowledgement & ReconcileDeltas.AcknowledgementFlag) != 0)
			{
				writer.Acknowledge((byte)(acknowledgement & ~ReconcileDeltas.AcknowledgementFlag));
			}
		}

		/// <summary>
		/// While the owner acknowledges, every reconcile is a delta against one it holds: no full reconcile after the first
		/// but the ten-second refresh, and every reconcile decodes exactly.
		/// </summary>
		[Test]
		public void Acknowledged_Chain_NeedsNoPeriodicFull()
		{
			DeltaReconcileSender<CharacterReconcileData> server = new DeltaReconcileSender<CharacterReconcileData>(ServerTickRate, acknowledged: true);
			DeltaReconcileSender<CharacterReconcileData> unacknowledged = new DeltaReconcileSender<CharacterReconcileData>(ServerTickRate);
			DeltaReconcileReceiver<CharacterReconcileData> client = new DeltaReconcileReceiver<CharacterReconcileData>();
			CharacterReconcileData authoritative = MakeReconcileData();
			// The acknowledgement rides the owner's replicates: it reaches the server a few ticks later.
			const int AcknowledgementLag = 3;
			var inFlight = new System.Collections.Generic.Queue<byte>();

			uint ticks = ServerTickRate * 20u;
			for (uint tick = 1; tick <= ticks; tick++)
			{
				authoritative = Advance(authoritative, tick);
				unacknowledged.Send(authoritative, tick);
				LogAssert.IsTrue(client.Receive(server.Send(authoritative, tick), tick, out CharacterReconcileData received),
					$"tick {tick}: nothing was lost, so every reconcile must be usable");
				AssertReconcileEquals(authoritative, received, $"tick {tick}");

				inFlight.Enqueue(client.DeltaReader.GetAcknowledgement(tick));
				if (inFlight.Count > AcknowledgementLag)
				{
					DeliverAcknowledgement(inFlight.Dequeue(), server.DeltaWriter);
				}
			}

			LogAssert.AreEqual(2, server.FullCount, "The first reconcile and the ten-second refresh are the only full ones in twenty seconds.");
			TestContext.WriteLine(
				$"MEASURE {ticks} reconciles: acknowledged {server.TotalBytes / (double)server.SentCount:F1}B/reconcile ({server.FullCount} full), " +
				$"against the last full {unacknowledged.TotalBytes / (double)unacknowledged.SentCount:F1}B/reconcile ({unacknowledged.FullCount} full)");
			/* Advance changes position, health, rotation and timers every tick, so a delta is ~70 B against any baseline and
			 * this chain only saves the full reconciles. A mostly idle owner, whose deltas are a few bytes, saves most of what
			 * it cost: there the once-a-second full reconcile was nearly all of it. */
			LogAssert.IsTrue(server.TotalBytes < unacknowledged.TotalBytes, "Acknowledged deltas must never cost more than the full-every-second scheme.");
		}

		/// <summary>
		/// The channel this exists for: reconciles and acknowledgements both lost and reordered. Every reconcile the owner
		/// accepts must be exactly what the server wrote for that tick, and a loss must cost only a tick or two, not the
		/// second until the next full reconcile.
		/// </summary>
		[Test]
		public void LossyReorderingChannel_NeverMisapplies_AndNeverStallsLong()
		{
			foreach (bool acknowledged in new[] { true, false })
			{
				var random = new System.Random(20211);
				DeltaReconcileSender<CharacterReconcileData> server = new DeltaReconcileSender<CharacterReconcileData>(ServerTickRate, acknowledged);
				DeltaReconcileReceiver<CharacterReconcileData> client = new DeltaReconcileReceiver<CharacterReconcileData>();
				var written = new System.Collections.Generic.Dictionary<uint, CharacterReconcileData>();
				var down = new System.Collections.Generic.List<(uint ArrivesOn, uint Tick, ArraySegment<byte> Payload)>();
				var up = new System.Collections.Generic.List<(uint ArrivesOn, byte Acknowledgement)>();
				CharacterReconcileData authoritative = MakeReconcileData();

				uint ticks = ServerTickRate * 30u;
				uint lastUsable = 0;
				uint longestGap = 0;
				int used = 0;
				for (uint tick = 1; tick <= ticks; tick++)
				{
					authoritative = Advance(authoritative, tick);
					written[tick] = authoritative;
					ArraySegment<byte> payload = server.Send(authoritative, tick);
					// 10% lost; the rest arrive up to two ticks late, so they also reorder.
					if (random.NextDouble() >= 0.10)
					{
						down.Add((tick + (uint)random.Next(0, 3), tick, new ArraySegment<byte>(payload.ToArray())));
					}

					for (int i = down.Count - 1; i >= 0; i--)
					{
						if (down[i].ArrivesOn != tick)
						{
							continue;
						}
						if (client.Receive(down[i].Payload, down[i].Tick, out CharacterReconcileData received))
						{
							AssertReconcileEquals(written[down[i].Tick], received, $"acknowledged {acknowledged}, tick {down[i].Tick} received on {tick}");
							longestGap = Math.Max(longestGap, tick - lastUsable);
							lastUsable = tick;
							used++;
						}
						down.RemoveAt(i);
					}

					// The owner's replicates: 10% lost, one to three ticks late.
					if (random.NextDouble() >= 0.10)
					{
						up.Add((tick + (uint)random.Next(1, 4), client.DeltaReader.GetAcknowledgement(tick)));
					}
					for (int i = up.Count - 1; i >= 0; i--)
					{
						if (up[i].ArrivesOn == tick)
						{
							DeliverAcknowledgement(up[i].Acknowledgement, server.DeltaWriter);
							up.RemoveAt(i);
						}
					}
				}

				TestContext.WriteLine(
					$"MEASURE 10% loss both ways, reordered, {ticks} ticks, {(acknowledged ? "acknowledged" : "against the last full")}: " +
					$"{used} reconciles used, longest gap {longestGap} ticks, {server.FullCount} full, " +
					$"{server.TotalBytes / (double)server.SentCount:F1}B/reconcile");
				if (acknowledged)
				{
					LogAssert.IsTrue(longestGap <= 8u, $"longest gap {longestGap} ticks. " + "With acknowledgements a loss must cost a few ticks, not the second until the next full reconcile.");
					LogAssert.IsTrue(server.FullCount <= 4, $"{server.FullCount} full. " + "Only the first full reconcile and the ten-second refreshes, whatever is lost.");
				}
			}
		}

		/// <summary>Acknowledgements arrive out of order: the newest wins, and one too old to be a baseline is not used.</summary>
		[Test]
		public void Acknowledgements_KeepTheNewest_AndExpire()
		{
			DeltaReconcileSender<CharacterReconcileData> server = new DeltaReconcileSender<CharacterReconcileData>(ServerTickRate, acknowledged: true);
			DeltaReconcileReceiver<CharacterReconcileData> client = new DeltaReconcileReceiver<CharacterReconcileData>();
			CharacterReconcileData authoritative = MakeReconcileData();
			byte eight = 0;
			byte five = 0;
			for (uint tick = 1; tick <= 10; tick++)
			{
				authoritative = Advance(authoritative, tick);
				client.Receive(server.Send(authoritative, tick), tick, out _);
				if (tick == 5) five = client.DeltaReader.GetAcknowledgement(tick);
				if (tick == 8) eight = client.DeltaReader.GetAcknowledgement(tick);
			}

			DeliverAcknowledgement(eight, server.DeltaWriter);
			DeliverAcknowledgement(five, server.DeltaWriter);
			LogAssert.AreEqual(eight & 0x7F, server.DeltaWriter.AcknowledgedId, "A late, older acknowledgement must not replace a newer one.");

			authoritative = Advance(authoritative, 11);
			LogAssert.IsTrue(client.Receive(server.Send(authoritative, 11, out bool full), 11, out CharacterReconcileData received) && !full,
				"Written against the acknowledged reconcile.");
			AssertReconcileEquals(authoritative, received, "tick 11 against the acknowledged reconcile");

			// No acknowledgement for more than the baseline window: the server must stop using the old one.
			for (uint tick = 12; tick <= 12 + ReconcileDeltas.MaximumBaselineWrites; tick++)
			{
				authoritative = Advance(authoritative, tick);
				server.Send(authoritative, tick);
			}
			LogAssert.AreEqual(-1, server.DeltaWriter.AcknowledgedId, "An acknowledgement older than the baseline window is not a baseline.");
		}

		/// <summary>The owner stops acknowledging what it decoded once that is too old to be anyone's baseline.</summary>
		[Test]
		public void Acknowledgement_StopsWhenStale()
		{
			DeltaReconcileSender<CharacterReconcileData> server = new DeltaReconcileSender<CharacterReconcileData>(ServerTickRate, acknowledged: true);
			DeltaReconcileReceiver<CharacterReconcileData> client = new DeltaReconcileReceiver<CharacterReconcileData>();
			LogAssert.AreEqual(0, client.DeltaReader.GetAcknowledgement(1), "Nothing decoded yet: nothing to acknowledge.");
			client.Receive(server.Send(MakeReconcileData(), 10), 10, out _);
			LogAssert.AreNotEqual(0, client.DeltaReader.GetAcknowledgement(10 + ReconcileDeltas.MaximumBaselineTicks), "Recent enough.");
			LogAssert.AreEqual(0, client.DeltaReader.GetAcknowledgement(11 + ReconcileDeltas.MaximumBaselineTicks), "Too old to acknowledge.");
		}

		/// <summary>
		/// A long outage, longer than the ids take to wrap: nothing reaches the owner for five seconds. Ids written in that time
		/// reuse the ones it holds, so a delta naming one of them must be refused, never decoded against the old reconcile;
		/// and the owner recovers within a second of the outage ending.
		/// </summary>
		[Test]
		public void LongOutage_PastTheIdWrap_NeverMisapplies_AndRecovers()
		{
			DeltaReconcileSender<CharacterReconcileData> server = new DeltaReconcileSender<CharacterReconcileData>(ServerTickRate, acknowledged: true);
			DeltaReconcileReceiver<CharacterReconcileData> client = new DeltaReconcileReceiver<CharacterReconcileData>();
			CharacterReconcileData authoritative = MakeReconcileData();
			uint outageStart = 40, outageEnd = 40 + ServerTickRate * 5u;
			uint recovered = 0;
			for (uint tick = 1; tick <= outageEnd + ServerTickRate * 2u; tick++)
			{
				authoritative = Advance(authoritative, tick);
				ArraySegment<byte> payload = server.Send(authoritative, tick);
				bool lost = tick >= outageStart && tick < outageEnd;
				if (!lost && client.Receive(payload, tick, out CharacterReconcileData received))
				{
					AssertReconcileEquals(authoritative, received, $"tick {tick}");
					if (tick >= outageEnd && recovered == 0)
					{
						recovered = tick;
					}
				}
				// The owner's replicates keep flowing (it is the downlink that failed); they carry whatever it holds.
				DeliverAcknowledgement(client.DeltaReader.GetAcknowledgement(tick), server.DeltaWriter);
			}
			LogAssert.IsTrue(recovered != 0 && recovered - outageEnd <= ServerTickRate,
				$"The owner must be using reconciles again within a second of the outage ending; it was at tick {recovered} (outage ended {outageEnd}).");
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
				a.MotorState.IsSwimming != b.MotorState.IsSwimming ||
				a.MotorState.ClimbOutTicks != b.MotorState.ClimbOutTicks ||
				a.MotorState.Climbable != b.MotorState.Climbable ||
				a.MotorState.ClimbPhase != b.MotorState.ClimbPhase ||
				a.MotorState.ClimbTicks != b.MotorState.ClimbTicks ||
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
