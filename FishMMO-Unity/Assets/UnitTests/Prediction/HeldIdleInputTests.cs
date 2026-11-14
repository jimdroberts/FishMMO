using System;
using System.Collections.Generic;
using FishNet.Object.Prediction;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Held idle input (FishNet, FISHMMO EDIT: ReplicateIdle and ReplicateHoldPlanner) and batched replicates: an owner standing
	/// still stops sending the same input every tick, the server repeats it, and when the owner moves again the server runs its
	/// input on exactly the tick the owner did. Proven on a tick-by-tick timeline that drives the REAL policy, the REAL planner
	/// and the REAL character input rules through a model of FishNet's client resends, batching, the network, and the server's
	/// queue, reader and start delay. A desync is any tick number the server runs with an input other than the owner's.
	/// </summary>
	public class HeldIdleInputTests
	{
		[SetUp]
		public void Register()
		{
			CharacterPredictionController.RegisterHeldInput();
		}

		[TearDown]
		public void Unregister()
		{
			ReplicateIdle<CharacterReplicateData>.CanHold = null;
			ReplicateIdle<CharacterReplicateData>.SameInput = null;
		}

		// ── Inputs ────────────────────────────────────────────────────────

		private static CharacterReplicateData Idle(uint tick, float aimYaw)
		{
			CharacterReplicateData input = default;
			input.MoveFlags = 1 << (int)KCCMoveFlags.IsActualData;
			input.ActivationFlags = 1 << (int)AbilityActivationFlags.IsActualData;
			input.AimDirection = AimDirectionCompression.Quantize(Quaternion.Euler(0f, aimYaw, 0f) * Vector3.forward);
			input.ViewTickOffset = 65533;
			input.ViewTickFraction = 128;
			input.SetTick(tick);
			return input;
		}

		private static CharacterReplicateData Moving(uint tick, float aimYaw, float forward)
		{
			CharacterReplicateData input = Idle(tick, aimYaw);
			input.MoveAxisForward = MoveAxisCompression.Quantize(forward);
			return input;
		}

		[Test]
		public void OnlyAnUnchangedIdleInputIsARepeat()
		{
			CharacterReplicateData a = Idle(10, 30f), b = Idle(11, 30f);
			LogAssert.IsTrue(ReplicateIdle<CharacterReplicateData>.IsRepeat(a, b), "Standing still with the same aim repeats.");
			LogAssert.IsFalse(ReplicateIdle<CharacterReplicateData>.IsRepeat(a, Idle(11, 30.5f)), "A turned camera is new input.");
			LogAssert.IsFalse(ReplicateIdle<CharacterReplicateData>.IsRepeat(Moving(10, 30f, 1f), Moving(11, 30f, 1f)), "Moving is never held, even unchanged.");
			CharacterReplicateData claim = Idle(11, 30f);
			claim.ViewTickFraction = 129;
			LogAssert.IsFalse(ReplicateIdle<CharacterReplicateData>.IsRepeat(a, claim), "A changed view claim is new input.");
			CharacterReplicateData jump = Idle(11, 30f);
			jump.MoveFlags |= 1 << (int)KCCMoveFlags.Jump;
			LogAssert.IsFalse(ReplicateIdle<CharacterReplicateData>.IsRepeat(a, jump), "A jump is never held.");
		}

		// ── Planner ───────────────────────────────────────────────────────

		[Test]
		public void ThePlannerLinesRealInputUpByTickNumber()
		{
			LogAssert.AreEqual(ReplicateHoldAction.RunHeld, ReplicateHoldPlanner.Next(false, 0, false, 100, 5, out uint run), "Nothing queued: hold.");
			LogAssert.AreEqual(100u, run, "On the next number.");
			LogAssert.AreEqual(ReplicateHoldAction.DropHead, ReplicateHoldPlanner.Next(true, 103, true, 100, 5, out _), "A queued repeat is what is being synthesised: dropped.");
			LogAssert.AreEqual(ReplicateHoldAction.RunHeld, ReplicateHoldPlanner.Next(true, 103, false, 100, 5, out run), "Real input early: hold until its number.");
			LogAssert.AreEqual(100u, run, "Still numbered on.");
			LogAssert.AreEqual(ReplicateHoldAction.RunHead, ReplicateHoldPlanner.Next(true, 100, false, 100, 5, out _), "Due: run it.");
			LogAssert.AreEqual(ReplicateHoldAction.RunHead, ReplicateHoldPlanner.Next(true, 97, false, 100, 5, out _), "Late: run it at once, never discarded.");
			LogAssert.AreEqual(ReplicateHoldAction.RunHeld, ReplicateHoldPlanner.Next(true, 130, false, 100, 5, out run), "Far ahead (the owner's clock moved): hold, but jump.");
			LogAssert.AreEqual(125u, run, "Jumped so the input waits exactly the buffer.");
		}

		// ── Timeline ──────────────────────────────────────────────────────

		private sealed class Run
		{
			public uint Time;
			public uint Number;
			public bool Default;
			public CharacterReplicateData Data;
		}

		private struct Packet
		{
			public uint SentTick;
			public uint ArriveTime;
			public List<CharacterReplicateData> Entries;
		}

		private sealed class Outcome
		{
			public int Desyncs;
			public int DefaultTicks;
			public int LostRealInputs;
			public int PacketsSent;
			public int IdlePacketsSent;
			public List<Run> Runs = new List<Run>();
			public List<uint> DefaultTimes = new List<uint>();
		}

		/// <summary>The owner's input for each tick: idle and moving stretches, a camera turned while standing.</summary>
		private static CharacterReplicateData[] Script(int ticks, int seed)
		{
			var random = new System.Random(seed);
			var inputs = new CharacterReplicateData[ticks + 1];
			float aim = 0f;
			bool moving = false;
			int left = 0;
			for (int t = 1; t <= ticks; ++t)
			{
				if (left-- <= 0)
				{
					moving = !moving;
					left = moving ? random.Next(10, 90) : random.Next(30, 400);
					if (!moving && random.Next(3) == 0)
					{
						aim += random.Next(1, 40);
					}
				}
				inputs[t] = moving ? Moving((uint)t, aim, random.Next(2) == 0 ? 1f : 0.5f) : Idle((uint)t, aim);
				// Now and then the camera is turned while standing: one new idle input, then idle again.
				if (!moving && random.Next(200) == 0)
				{
					aim += 3f;
					inputs[t] = Idle((uint)t, aim);
				}
			}
			return inputs;
		}

		/// <summary>
		/// One owner and the server over <paramref name="ticks"/>. <paramref name="held"/> false is today's behaviour: every
		/// input sent, an empty queue runs default data.
		/// </summary>
		private static Outcome Simulate(CharacterReplicateData[] inputs, int ticks, bool held, int latency, int jitter, double loss, int seed)
		{
			const int Redundancy = 3, Interval = 2, StateInterpolation = 3;
			var outcome = new Outcome();
			var inFlight = new List<Packet>();

			// Client state (FishNet Replicate_Authoritative with the edits).
			int remaining = 0;
			bool pending = false;
			uint lastWrite = 0;
			bool hasWritten = false;

			// Server state (Replicate_Reader / EnqueueReceived / NonAuthoritative with the edits).
			var queue = new Queue<CharacterReplicateData>();
			uint lastReadTick = 0, lastPacketTick = 0, startTick = 0, lastHeld = 0;
			bool startArmed = false, holding = false;
			var history = new List<Run>();
			var ranRealTicks = new HashSet<uint>();
			int leadTicks = latency + StateInterpolation;

			for (int time = 1; time <= ticks + 60; ++time)
			{
				// ── Client tick `time` ──
				if (time <= ticks)
				{
					uint t = (uint)time;
					bool isRepeat = held && t >= 2 && ReplicateIdle<CharacterReplicateData>.IsRepeat(inputs[t - 1], inputs[t]);
					if (!isRepeat)
					{
						remaining = Redundancy;
					}
					else if (remaining <= 0 && !pending && (!hasWritten || t - lastWrite >= ReplicateIdle.KeepaliveTicks))
					{
						remaining = 1;
					}
					if (remaining > 0)
					{
						remaining--;
						pending = true;
					}
					if (pending && t % Interval == 0)
					{
						int count = Redundancy + Interval - 1;
						var entries = new List<CharacterReplicateData>();
						for (uint e = (uint)Math.Max(1, (int)t - count + 1); e <= t; ++e)
						{
							entries.Add(inputs[e]);
						}
						outcome.PacketsSent++;
						if (isRepeat)
						{
							outcome.IdlePacketsSent++;
						}
						/* The network is a function of the send tick, not of how many packets went before: a packet sent on the
						 * same tick meets the same delay and loss in both modes, so the comparison is like for like. */
						if (Network(seed, t, 0) >= loss)
						{
							int delay = latency + (int)(Network(seed, t, 1) * (jitter + 1));
							inFlight.Add(new Packet { SentTick = t, ArriveTime = (uint)(time + Math.Min(delay, latency + jitter)), Entries = entries });
						}
						pending = false;
						lastWrite = t;
						hasWritten = true;
					}
				}

				// ── Server receives ──
				for (int i = inFlight.Count - 1; i >= 0; --i)
				{
					Packet packet = inFlight[i];
					if (packet.ArriveTime != (uint)time)
					{
						continue;
					}
					inFlight.RemoveAt(i);
					if (packet.SentTick < lastPacketTick)
					{
						continue;
					}
					lastPacketTick = packet.SentTick;
					int before = queue.Count;
					foreach (CharacterReplicateData entry in packet.Entries)
					{
						uint tick = entry.GetTick();
						if (tick <= lastReadTick)
						{
							continue;
						}
						lastReadTick = tick;
						queue.Enqueue(entry);
					}
					// The arming rule (Replicate_EnqueueReceivedReplicate + ContinuesRunStream).
					bool continues = held && history.Count > 0 && queue.Count > 0 &&
						((!DefaultSinceCreated(history) && queue.Peek().GetTick() == LastCreatedTick(history) + 1) || LastIsHoldable(history));
					if (before == 0 && queue.Count > 0 && !holding && !continues)
					{
						startTick = (uint)time + StateInterpolation;
						startArmed = true;
					}
				}

				// ── Server tick: the server runs client tick (time - leadTicks) in steady state ──
				if (time <= leadTicks)
				{
					continue;
				}
				Run run = ServerTick(held, queue, history, ref holding, ref lastHeld, ref startArmed, startTick, (uint)time);
				if (run != null)
				{
					run.Time = (uint)time;
					history.Add(run);
					outcome.Runs.Add(run);
					if (!run.Default)
					{
						ranRealTicks.Add(run.Number);
					}
				}
			}

			// Score: every run inside the scripted window is compared with the owner's input for its number.
			foreach (Run run in outcome.Runs)
			{
				if (run.Time > ticks + leadTicks)
				{
					continue; // The script has ended: nothing was sent to run.
				}
				if (run.Default)
				{
					outcome.DefaultTimes.Add(run.Time);
					outcome.DefaultTicks++;
					outcome.Desyncs++;
					continue;
				}
				if (run.Number > ticks)
				{
					continue; // After the script ended: nothing to compare with.
				}
				if (run.Number < 1 || !CharacterPredictionController.SameHeldInput(run.Data, inputs[run.Number]))
				{
					outcome.Desyncs++;
				}
			}
			// A non-repeat input the owner made that the server never ran.
			for (uint t = 2; t <= ticks - 40; ++t)
			{
				bool isRepeat = ReplicateIdle<CharacterReplicateData>.IsRepeat(inputs[t - 1], inputs[t]);
				if (!isRepeat && !ranRealTicks.Contains(t))
				{
					outcome.LostRealInputs++;
				}
			}
			return outcome;
		}

		private static bool LastIsHoldable(List<Run> history) =>
			history.Count > 0 && !history[history.Count - 1].Default && ReplicateIdle<CharacterReplicateData>.CanHold(history[history.Count - 1].Data);

		private static uint LastCreatedTick(List<Run> history)
		{
			for (int i = history.Count - 1; i >= 0; --i)
			{
				if (!history[i].Default)
				{
					return history[i].Number;
				}
			}
			return 0;
		}

		private static bool DefaultSinceCreated(List<Run> history) => history.Count > 0 && history[history.Count - 1].Default;

		/// <summary>A deterministic value in [0, 1) for a packet sent on <paramref name="tick"/>.</summary>
		private static double Network(int seed, uint tick, int salt)
		{
			unchecked
			{
				uint h = (uint)seed * 2654435761u ^ tick * 2246822519u ^ (uint)salt * 3266489917u;
				h ^= h >> 15;
				h *= 2246822519u;
				h ^= h >> 13;
				h *= 3266489917u;
				h ^= h >> 16;
				return h / 4294967296.0;
			}
		}

		/// <summary>One server tick, mirroring Replicate_NonAuthoritative with the hold.</summary>
		private static Run ServerTick(bool held, Queue<CharacterReplicateData> queue, List<Run> history, ref bool holding, ref uint lastHeld,
			ref bool startArmed, uint startTick, uint time)
		{
			const uint MaximumWait = 3 + 2;
			if (holding)
			{
				CharacterReplicateData heldData = history[history.Count - 1].Data;
				while (true)
				{
					bool hasHead = queue.Count > 0;
					CharacterReplicateData head = hasHead ? queue.Peek() : default;
					bool headIsRepeat = hasHead && ReplicateIdle<CharacterReplicateData>.IsRepeat(heldData, head);
					ReplicateHoldAction action = ReplicateHoldPlanner.Next(hasHead, hasHead ? head.GetTick() : 0, headIsRepeat, lastHeld + 1, MaximumWait, out uint runTick);
					if (action == ReplicateHoldAction.DropHead)
					{
						queue.Dequeue();
						continue;
					}
					if (action == ReplicateHoldAction.RunHead)
					{
						holding = false;
						startArmed = false;
						CharacterReplicateData real = queue.Dequeue();
						return new Run { Number = real.GetTick(), Data = real };
					}
					lastHeld = runTick;
					CharacterReplicateData copy = heldData;
					copy.SetTick(runTick);
					return new Run { Number = runTick, Data = copy };
				}
			}
			if (queue.Count == 0)
			{
				if (held && history.Count > 0 && !history[history.Count - 1].Default &&
					ReplicateIdle<CharacterReplicateData>.CanHold(history[history.Count - 1].Data))
				{
					holding = true;
					lastHeld = history[history.Count - 1].Data.GetTick();
					return ServerTick(held, queue, history, ref holding, ref lastHeld, ref startArmed, startTick, time);
				}
				return new Run { Default = true };
			}
			// Input arrived for a later tick than the next: the owner skipped holdable repeats. Hold through the gap.
			if (held && LastIsHoldable(history) && queue.Peek().GetTick() > LastCreatedTick(history) + 1)
			{
				holding = true;
				lastHeld = history[history.Count - 1].Data.GetTick();
				return ServerTick(held, queue, history, ref holding, ref lastHeld, ref startArmed, startTick, time);
			}
			if (startArmed && time < startTick)
			{
				return new Run { Default = true };
			}
			startArmed = false;
			CharacterReplicateData next = queue.Dequeue();
			return new Run { Number = next.GetTick(), Data = next };
		}

		[TestCase(0, 0)]
		[TestCase(4, 0)]
		[TestCase(4, 2)]
		public void OnAGoodNetwork_HeldInputNeverDesyncs_AndIdleSendsAlmostNothing(int latency, int jitter)
		{
			const int Ticks = 30 * 300;
			for (int seed = 1; seed <= 4; ++seed)
			{
				CharacterReplicateData[] inputs = Script(Ticks, seed);
				Outcome held = Simulate(inputs, Ticks, held: true, latency, jitter, loss: 0, seed);
				Outcome today = Simulate(inputs, Ticks, held: false, latency, jitter, loss: 0, seed);
				Debug.Log($"[HeldIdle] good latency {latency} jitter {jitter} seed {seed}: held desyncs {held.Desyncs} (default {held.DefaultTicks} at {string.Join(",", held.DefaultTimes)}) packets {held.PacketsSent} (idle {held.IdlePacketsSent}) lost {held.LostRealInputs} | today desyncs {today.Desyncs} (default {today.DefaultTicks} at {string.Join(",", today.DefaultTimes)}) packets {today.PacketsSent} lost {today.LostRealInputs}");
				LogAssert.AreEqual(0, held.LostRealInputs, $"seed {seed}: every input the owner made reached the server and ran.");
				LogAssert.IsTrue(held.Desyncs <= today.Desyncs,
					$"seed {seed}: held input must desync no more than today ({held.Desyncs} vs {today.Desyncs}).");
				LogAssert.AreEqual(0, held.Desyncs - held.DefaultTicks,
					$"seed {seed}: no tick runs with an input other than the owner's ({held.Desyncs - held.DefaultTicks}).");
				LogAssert.IsTrue(held.PacketsSent < today.PacketsSent * 0.6,
					$"seed {seed}: standing still sends far fewer packets ({held.PacketsSent} vs {today.PacketsSent}).");
			}
		}

		[TestCase(2, 1, 0.01)]
		[TestCase(4, 3, 0.02)]
		[TestCase(6, 4, 0.05)]
		[TestCase(8, 6, 0.03)]
		public void OnABadNetwork_HeldInputIsNoWorseThanToday(int latency, int jitter, double loss)
		{
			const int Ticks = 30 * 300;
			int heldDesyncs = 0, todayDesyncs = 0, heldLost = 0, todayLost = 0;
			for (int seed = 1; seed <= 6; ++seed)
			{
				CharacterReplicateData[] inputs = Script(Ticks, seed);
				Outcome held = Simulate(inputs, Ticks, held: true, latency, jitter, loss, seed);
				Outcome today = Simulate(inputs, Ticks, held: false, latency, jitter, loss, seed);
				Debug.Log($"[HeldIdle] bad latency {latency} jitter {jitter} loss {loss} seed {seed}: held desyncs {held.Desyncs} (default {held.DefaultTicks}) packets {held.PacketsSent} lost {held.LostRealInputs} | today desyncs {today.Desyncs} (default {today.DefaultTicks}) packets {today.PacketsSent} lost {today.LostRealInputs}");
				heldDesyncs += held.Desyncs;
				todayDesyncs += today.Desyncs;
				heldLost += held.LostRealInputs;
				todayLost += today.LostRealInputs;
			}
			LogAssert.IsTrue(heldDesyncs <= todayDesyncs, $"held input must not desync more than today under loss and jitter ({heldDesyncs} vs {todayDesyncs}).");
			LogAssert.IsTrue(heldLost <= todayLost, $"held input must not lose more of the owner's inputs than today ({heldLost} vs {todayLost}).");
		}

		// ── Batched replicates ────────────────────────────────────────────

		[Test]
		public void BatchedReplicatesStillCarryEveryInputTwice()
		{
			const int Redundancy = 3, Interval = 2;
			int count = Redundancy + Interval - 1;
			var seen = new Dictionary<int, int>();
			for (int flush = Interval; flush <= 200; flush += Interval)
			{
				for (int e = flush - count + 1; e <= flush; ++e)
				{
					seen[e] = seen.TryGetValue(e, out int n) ? n + 1 : 1;
				}
			}
			for (int t = 10; t <= 190; ++t)
			{
				LogAssert.IsTrue(seen[t] >= 2, $"input {t} travels in {seen[t]} packets; one lost packet must not lose it");
			}
		}
	}
}
