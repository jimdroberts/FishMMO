using System;
using System.Reflection;
using NUnit.Framework;
using FishMMO.Shared;
using UnityEngine;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Coverage for the rewind that makes aimed and projectile abilities accurate at high latency.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Without compensation a hit resolves against where a character is now, while the shooter aimed
	/// at where it was rendered — its interpolation buffer plus its own latency in the past. The
	/// measured gap runs from 0.45&#160;m on a same-city connection to 2.2&#160;m at 300&#160;ms,
	/// against ability hitboxes authored at half a metre, so at any real latency the shooter's aim
	/// and the server's answer are describing different worlds.
	/// </para>
	/// <para>
	/// These tests exercise the ring buffer's resolution and the arithmetic that decides how far
	/// back to look. They do not stand up two networked peers, so they establish that the mechanism
	/// resolves the right tick and returns characters afterwards — not that it feels right in a live
	/// session.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class LagCompensationTests
	{
		private GameObject go;
		private CharacterPositionHistory history;
		private MethodInfo allocate;
		private MethodInfo record;

		[SetUp]
		public void CreateHistory()
		{
			go = new GameObject("HistoryTest");
			go.AddComponent<BoxCollider>();
			history = go.AddComponent<CharacterPositionHistory>();

			Type t = typeof(CharacterPositionHistory);
			allocate = t.GetMethod("AllocateBuffer", BindingFlags.Instance | BindingFlags.NonPublic);
			record = t.GetMethod("Record", BindingFlags.Instance | BindingFlags.NonPublic);

			LogAssert.IsNotNull(allocate, "AllocateBuffer must exist; the ring is allocated through it.");
			LogAssert.IsNotNull(record, "Record must exist; the ring is written through it.");
		}

		[TearDown]
		public void DestroyHistory()
		{
			LagCompensationRegistryClear();
			if (go != null)
			{
				UnityEngine.Object.DestroyImmediate(go);
			}
		}

		private static void LagCompensationRegistryClear()
		{
			MethodInfo clear = typeof(LagCompensationRegistry)
				.GetMethod("Clear", BindingFlags.Static | BindingFlags.NonPublic);
			clear?.Invoke(null, null);
		}

		private void Allocate(int ticks) => allocate.Invoke(history, new object[] { ticks });

		private void Record(uint tick, Vector3 position)
			=> record.Invoke(history, new object[] { tick, position, Quaternion.identity });

		/// <summary>Records a straight-line walk, one tick per metre travelled at <paramref name="speed"/>.</summary>
		private void RecordWalk(uint firstTick, int ticks, float speed, float tickDelta)
		{
			for (int i = 0; i < ticks; i++)
			{
				Record(firstTick + (uint)i, new Vector3(0f, 0f, speed * tickDelta * i));
			}
		}

		#region Ring buffer.

		/// <summary>
		/// A recorded tick resolves to the position that was recorded for it.
		/// </summary>
		[Test]
		public void History_ResolvesARecordedTick_Exactly()
		{
			Allocate(16);
			for (uint i = 0; i < 10; i++)
			{
				Record(100 + i, new Vector3(0f, 0f, i));
			}

			LogAssert.IsTrue(history.TryResolve(105, out CharacterPositionHistory.Snapshot s),
				"Tick 105 was recorded and must resolve.");
			LogAssert.IsTrue(Mathf.Abs(s.Position.z - 5f) < 0.001f,
				$"Tick 105 was recorded at z=5 but resolved to z={s.Position.z}.");
		}

		/// <summary>
		/// The ring evicts oldest-first, clamps a tick a little past the window, and refuses one
		/// wildly past it.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The two out-of-window cases answer different questions and must not share an answer.
		/// </para>
		/// <para>
		/// <b>A little past the window is a real client that is simply slower than the recording.</b>
		/// Refusing it was a cliff, not a defence: the ceiling on how far back anybody can shoot is
		/// the recording itself, and an attacker reaches that ceiling by claiming a value just INSIDE
		/// the window — which was always accepted. Refusing only rejected claims that overshot, and
		/// those buy strictly less, so the rule deterred nobody and cut off honest high-latency
		/// players entirely.
		/// </para>
		/// <para>
		/// <b>Wildly past it is a tick-domain error</b>, and clamping that would hand back a
		/// real-looking pose for a tick nobody recorded. See
		/// <see cref="History_AndCompensationAnchor_ShareOneTickDomain"/>, which is the test that
		/// depends on this half.
		/// </para>
		/// </remarks>
		[Test]
		public void History_EvictsOldest_ClampsNearMissesAndRefusesFarOnes()
		{
			Allocate(8);
			for (uint i = 0; i < 20; i++)
			{
				Record(100 + i, new Vector3(0f, 0f, i));
			}

			LogAssert.AreEqual(8, history.Capacity, "The ring must not grow past its allocation.");
			LogAssert.AreEqual(8, history.RecordedTicks, "A saturated ring holds exactly its capacity.");

			// Oldest held is 112 (ticks 112..119). 100 is 12 ticks past it, inside one window of grace.
			LogAssert.IsTrue(history.TryResolve(100, out CharacterPositionHistory.Snapshot clamped),
				"Tick 100 has been evicted but is within one window of the oldest sample, so it must " +
				"clamp rather than decline — refusing it is the cliff that cut off high-ping players.");
			LogAssert.IsTrue(Mathf.Abs(clamped.Position.z - 12f) < 0.001f,
				$"A clamped resolve must return the OLDEST held sample (z=12), not z={clamped.Position.z}.");

			LogAssert.IsFalse(history.TryResolve(50, out _),
				"Tick 50 is more than one window past the oldest sample. Nothing a latency claim can " +
				"produce reaches that far — only a tick-domain error does — so it must be refused.");

			LogAssert.IsTrue(history.TryResolve(115, out CharacterPositionHistory.Snapshot recent),
				"A tick still inside the window must resolve.");
			LogAssert.IsTrue(Mathf.Abs(recent.Position.z - 15f) < 0.001f,
				$"Tick 115 was recorded at z=15 but resolved to z={recent.Position.z}.");
		}

		/// <summary>
		/// Asking for the present or the future resolves to the newest sample.
		/// </summary>
		[Test]
		public void History_ResolvesFutureTicks_ToThePresent()
		{
			Allocate(16);
			for (uint i = 0; i < 5; i++)
			{
				Record(100 + i, new Vector3(0f, 0f, i));
			}

			LogAssert.IsTrue(history.TryResolve(999, out CharacterPositionHistory.Snapshot s),
				"A future tick must resolve rather than fail.");
			LogAssert.IsTrue(Mathf.Abs(s.Position.z - 4f) < 0.001f,
				"A future tick must resolve to the newest recorded position, not extrapolate.");
		}

		#endregion

		#region What compensation actually buys.

		/// <summary>
		/// The error a rewind removes, across the latency range the design targets.
		/// </summary>
		/// <remarks>
		/// The result this whole mechanism exists for. A character walking in a straight line is
		/// recorded per tick; the test then compares where an uncompensated query would find it
		/// (its live position) against where the shooter saw it, and against what the rewind
		/// resolves. The residual is interpolation error within a tick, not latency error.
		/// </remarks>
		[Test]
		public void Measure_RewindRemovesLatencyError_UpTo300ms()
		{
			const float tickDelta = 1f / 30f;
			const float speed = 6f;
			const uint firstTick = 1000;
			const int ticks = 40;

			Allocate(64);
			RecordWalk(firstTick, ticks, speed, tickDelta);

			uint nowTick = firstTick + (uint)ticks - 1;
			float livePosition = speed * tickDelta * (ticks - 1);

			TestContext.WriteLine("MEASURE peer walking a straight line at 6 m/s, 30Hz history");

			foreach (int oneWayMs in new[] { 8, 20, 40, 75, 130, 200, 300 })
			{
				// What the shooter saw: interpolation buffer plus one-way latency, in ticks.
				uint staleTicks = (uint)Mathf.RoundToInt(
					(oneWayMs / 1000f + LagCompensationTick.SpectatorInterpolationTicks * tickDelta) / tickDelta);
				uint rewindTick = nowTick - staleTicks;

				float whatShooterSaw = speed * tickDelta * (rewindTick - firstTick);
				float uncompensatedError = Mathf.Abs(livePosition - whatShooterSaw);

				LogAssert.IsTrue(history.TryResolve(rewindTick, out CharacterPositionHistory.Snapshot s),
					$"History must cover a {oneWayMs}ms rewind; it is sized for the design's 300ms target.");
				float compensatedError = Mathf.Abs(s.Position.z - whatShooterSaw);

				TestContext.WriteLine(
					$"MEASURE one-way {oneWayMs,3}ms -> uncompensated error {uncompensatedError:F2}m, " +
					$"after rewind {compensatedError:F3}m");

				LogAssert.IsTrue(compensatedError < 0.01f,
					$"At {oneWayMs}ms the rewind left {compensatedError:F3}m of error; it should resolve " +
					"to the recorded position almost exactly.");
			}

			LogAssert.IsTrue(history.Capacity >= 30,
				"The default window must cover 300ms at 30Hz with margin, or the target latency is unreachable.");
		}

		/// <summary>
		/// Sub-tick positions interpolate between the bracketing samples rather than snapping.
		/// </summary>
		/// <remarks>
		/// Matters because a client's view offset is rarely a whole number of ticks. Snapping to the
		/// nearest recorded sample would leave up to one tick of error — 0.2&#160;m at 6&#160;m/s and
		/// 30&#160;Hz, which is most of a hitbox.
		/// </remarks>
		[Test]
		public void History_InterpolatesBetweenRecordedTicks()
		{
			Allocate(16);
			Record(100, new Vector3(0f, 0f, 0f));
			Record(110, new Vector3(0f, 0f, 10f));

			LogAssert.IsTrue(history.TryResolve(105, out CharacterPositionHistory.Snapshot s),
				"A tick between two samples must resolve.");
			LogAssert.IsTrue(Mathf.Abs(s.Position.z - 5f) < 0.001f,
				$"Tick 105 sits midway between z=0 and z=10 and must resolve to z=5, got z={s.Position.z}.");
		}

		#endregion

		#region Rewind scope safety.

		/// <summary>
		/// A rewind scope restores the character even when the body throws.
		/// </summary>
		/// <remarks>
		/// The reason the API is a scope and not a pair of calls. A query that throws mid-rewind
		/// without restoring would strand every character in the scene at a past position — the
		/// server would then simulate, record and broadcast from there, turning a transient error
		/// into permanent corruption.
		/// </remarks>
		[Test]
		public void RewindScope_RestoresEvenWhenTheBodyThrows()
		{
			Allocate(16);
			for (uint i = 0; i < 10; i++)
			{
				Record(100 + i, new Vector3(0f, 0f, i));
			}

			go.transform.position = new Vector3(0f, 0f, 9f);
			Vector3 live = go.transform.position;

			MethodInfo rewind = typeof(CharacterPositionHistory)
				.GetMethod("Rewind", BindingFlags.Instance | BindingFlags.NonPublic);
			MethodInfo restore = typeof(CharacterPositionHistory)
				.GetMethod("Restore", BindingFlags.Instance | BindingFlags.NonPublic);
			LogAssert.IsNotNull(rewind, "Rewind must exist.");
			LogAssert.IsNotNull(restore, "Restore must exist.");

			try
			{
				bool moved = (bool)rewind.Invoke(history, new object[] { new RewindTarget(103u) });
				LogAssert.IsTrue(moved, "Rewinding to a recorded tick must displace the transform.");
				LogAssert.IsTrue(Mathf.Abs(go.transform.position.z - 3f) < 0.001f,
					$"Rewound to tick 103 the transform should sit at z=3, got z={go.transform.position.z}.");
				throw new InvalidOperationException("simulated query failure");
			}
			catch (InvalidOperationException)
			{
				// The scope's finally does this in production; done explicitly here.
				restore.Invoke(history, null);
			}

			LogAssert.IsTrue((go.transform.position - live).sqrMagnitude < 1e-6f,
				$"The character must return to its live position, got {go.transform.position} vs {live}.");
			LogAssert.IsTrue(!history.IsRewound, "The rewound flag must clear on restore.");
		}

		/// <summary>
		/// Recording is suppressed while displaced.
		/// </summary>
		/// <remarks>
		/// Without this a tick boundary landing inside a rewind scope would write the rewound
		/// position into history as though the character had really been there, and every later
		/// rewind would compound the error.
		/// </remarks>
		[Test]
		public void History_DoesNotRecord_WhileRewound()
		{
			Allocate(16);
			for (uint i = 0; i < 5; i++)
			{
				Record(100 + i, new Vector3(0f, 0f, i));
			}
			int before = history.RecordedTicks;

			MethodInfo rewind = typeof(CharacterPositionHistory)
				.GetMethod("Rewind", BindingFlags.Instance | BindingFlags.NonPublic);
			MethodInfo recordTick = typeof(CharacterPositionHistory)
				.GetMethod("RecordTick", BindingFlags.Instance | BindingFlags.NonPublic);
			LogAssert.IsNotNull(recordTick, "RecordTick must exist.");

			go.transform.position = new Vector3(0f, 0f, 4f);
			rewind.Invoke(history, new object[] { new RewindTarget(101u) });

			recordTick.Invoke(history, null);

			LogAssert.AreEqual(before, history.RecordedTicks,
				"A tick that fires while the character is displaced must not be recorded.");
		}

		#endregion

		#region Compensation arithmetic.

		/// <summary>
		/// The interpolation constant here must match what the prefabs are authored with.
		/// </summary>
		/// <remarks>
		/// <c>NetworkObject._spectatorInterpolation</c> is private, so the value is mirrored in
		/// <see cref="LagCompensationTick"/> rather than read. If the two drift apart, compensation
		/// is wrong by the difference — and it fails quietly, as hits landing consistently ahead of
		/// or behind where the shooter aimed rather than as an obvious break.
		/// </remarks>
		[Test]
		public void CompensationConstant_MatchesTheAuthoredPrefabSetting()
		{
			int authored = -1;

			/* Every REWINDABLE prefab, not only the playable ones. Compensation undoes the
			 * interpolation the shooter's client applied to whatever it was aiming at, and it aims
			 * at monsters and pets as well as at players — so an NPC authored at a different
			 * interpolation is compensated by the wrong amount just as surely. The roots match
			 * EveryCharacterPrefab_CarriesPositionHistory, which already covers both trees. */
			string[] roots =
			{
				"Assets/Prefabs/Shared/Entity/PlayableCharacters",
				"Assets/Prefabs/Shared/Entity/NPCs",
			};

			foreach (string guid in UnityEditor.AssetDatabase.FindAssets("t:Prefab", roots))
			{
				string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
				foreach (string line in System.IO.File.ReadAllLines(path))
				{
					if (!line.Contains("_spectatorInterpolation:"))
					{
						continue;
					}
					int parsed = int.Parse(line.Split(':')[1].Trim());
					if (authored >= 0)
					{
						LogAssert.AreEqual(authored, parsed,
							$"Rewindable prefabs must agree on _spectatorInterpolation; " +
							$"'{System.IO.Path.GetFileNameWithoutExtension(path)}' says {parsed} but an " +
							$"earlier prefab says {authored}, and one constant cannot compensate two settings.");
					}
					authored = parsed;
				}
			}

			TestContext.WriteLine(
				$"MEASURE authored _spectatorInterpolation = {authored}, " +
				$"LagCompensationTick constant = {LagCompensationTick.SpectatorInterpolationTicks}");

			LogAssert.IsTrue(authored >= 0, "No rewindable prefab declared _spectatorInterpolation.");
			LogAssert.AreEqual((uint)authored, LagCompensationTick.SpectatorInterpolationTicks,
				$"Prefabs interpolate {authored} ticks but LagCompensationTick compensates for " +
				$"{LagCompensationTick.SpectatorInterpolationTicks}. Hits will land offset by the difference.");
		}

		/// <summary>
		/// The history is keyed in the same tick domain the rewind measures back from.
		/// </summary>
		/// <remarks>
		/// <para>
		/// This is the invariant the whole subsystem rests on, and it was broken silently. The
		/// anchor was taken from <c>CharacterPredictionController.CurrentReplicateTickSnapshot</c>,
		/// which on the server is the OWNING CLIENT'S <c>TimeManager.LocalTick</c> — an
		/// unsynchronised counter that restarts at zero when that client connects, while the history
		/// is keyed by the server's own tick, which has been running since the process started.
		/// </para>
		/// <para>
		/// The failure has no symptom at the call site. Every <c>TryResolve</c> declines, so every
		/// <c>Rewind</c> declines, so <c>LagCompensationRegistry.Rewind</c> hands back an inactive
		/// scope and the query runs against live positions — the exact behaviour compensation exists
		/// to remove, reached without a single log line. This pins the two domains to one function
		/// so they cannot be edited apart again.
		/// </para>
		/// </remarks>
		[Test]
		public void History_AndCompensationAnchor_ShareOneTickDomain()
		{
			/* A server that has been up for two hours at 30 tps, which is unremarkable, and a client
			 * that connected a minute ago. Those are the two numbers that used to be subtracted from
			 * each other. */
			const uint serverTickNow = 216_000u;
			const uint clientDomainTick = 1_800u;

			Allocate(16);
			for (uint i = 0; i < 16; i++)
			{
				Record(serverTickNow - 15u + i, new Vector3(0f, 0f, i));
			}

			// The server's own domain resolves, and resolves to the sample that was recorded for it.
			LogAssert.IsTrue(
				history.TryResolve(new RewindTarget(serverTickNow - 2u), out CharacterPositionHistory.Snapshot inDomain),
				"A target measured back from the server's tick must resolve; this is the normal path.");
			LogAssert.IsTrue(Mathf.Abs(inDomain.Position.z - 13f) < 0.001f,
				$"Expected the sample recorded for tick {serverTickNow - 2u} (z=13) but resolved to z={inDomain.Position.z}.");

			/* The owning client's domain does not, and must not be quietly clamped to the oldest
			 * sample either — that would hand back a real-looking pose for a tick nobody recorded,
			 * turning a dead subsystem into a silently wrong one.
			 *
			 * This is the half of the out-of-window rule that survives the clamp added for
			 * high-latency clients: a near miss clamps, but a domain error overshoots by the
			 * client's entire uptime and is still refused. The separation is enormous and not a
			 * tuned threshold — see History_EvictsOldest_ClampsNearMissesAndRefusesFarOnes. */
			LogAssert.IsFalse(
				history.TryResolve(new RewindTarget(clientDomainTick), out _),
				"A target built from the owning client's replicate tick is not in this history's domain " +
				"and must be refused outright, not clamped.");

			TestContext.WriteLine(
				$"MEASURE server-domain anchor {serverTickNow} resolves; client-domain anchor {clientDomainTick} " +
				$"is {serverTickNow - clientDomainTick} ticks outside the window and is refused.");
		}

		/// <summary>
		/// <see cref="CharacterPositionHistory"/> stamps its samples with
		/// <see cref="LagCompensationTick.ServerTickDomain"/>, the same call the rewind anchors on.
		/// </summary>
		/// <remarks>
		/// Asserting the shared helper rather than the value it returns is the point: a future edit
		/// that reaches for <c>TimeManager.LocalTick</c> on one side only is what this is here to
		/// stop, and a value comparison would pass right up until the two peers differed.
		/// </remarks>
		[Test]
		public void RecordTick_StampsThroughTheSharedDomainHelper()
		{
			string source = System.IO.File.ReadAllText(
				"Assets/Scripts/Shared/Implementation/Entity/Prediction/LagCompensation/CharacterPositionHistory.cs");

			LogAssert.IsTrue(source.Contains("LagCompensationTick.ServerTickDomain(base.TimeManager)"),
				"CharacterPositionHistory.RecordTick must key the ring through " +
				"LagCompensationTick.ServerTickDomain so it cannot drift out of the domain the rewind " +
				"anchors in. Reading TimeManager.LocalTick directly here is how that drift starts.");

			string tickSource = System.IO.File.ReadAllText(
				"Assets/Scripts/Shared/Implementation/Entity/Prediction/LagCompensation/LagCompensationTick.cs");

			LogAssert.IsFalse(tickSource.Contains("CurrentReplicateTickSnapshot"),
				"LagCompensationTick must not anchor on CurrentReplicateTickSnapshot. That value is the " +
				"owning client's tick and cannot index a history keyed by the server's — using it " +
				"disables compensation entirely, with nothing logged.");
		}

		/// <summary>
		/// Every hittable character prefab carries a history component.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Asset-level guard. The component was attached by editing prefab YAML directly, so this
		/// asserts the result imported as a live component rather than a missing script — a
		/// malformed block leaves <c>GetComponent</c> returning null while the file still contains
		/// the GUID and looks correct to a grep.
		/// </para>
		/// <para>
		/// It also catches the quieter failure: a character with no history is not an error, it just
		/// resolves uncompensated. Hits against it would be off by the shooter's full latency while
		/// everything else in the scene was accurate, which reads as "that one enemy feels wrong"
		/// rather than as a bug with an obvious cause.
		/// </para>
		/// </remarks>
		[Test]
		public void EveryCharacterPrefab_CarriesPositionHistory()
		{
			string[] roots =
			{
				"Assets/Prefabs/Shared/Entity/PlayableCharacters",
				"Assets/Prefabs/Shared/Entity/NPCs",
			};

			int checkedPrefabs = 0;

			foreach (string guid in UnityEditor.AssetDatabase.FindAssets("t:Prefab", roots))
			{
				string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
				GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
				if (prefab == null)
				{
					continue;
				}

				// Only characters are rewound; props and spawners have no history to keep.
				if (prefab.GetComponent<FishNet.Object.NetworkObject>() == null)
				{
					continue;
				}
				if (prefab.GetComponent<CharacterPredictionController>() == null)
				{
					continue;
				}

				checkedPrefabs++;
				/* Compared to null rather than passed to IsNotNull: the assert stringifies whatever it
				 * is given, and NetworkBehaviour.ToString() dereferences spawn-time state that does
				 * not exist on a prefab asset, so a PASSING assert would throw while building its
				 * own message. */
				bool hasHistory = prefab.GetComponent<CharacterPositionHistory>() != null;
				LogAssert.IsTrue(hasHistory,
					$"'{prefab.name}' is a predicted character with no CharacterPositionHistory, so hits " +
					"against it resolve against live positions and are off by the shooter's full latency.");
			}

			TestContext.WriteLine($"MEASURE predicted character prefabs carrying position history: {checkedPrefabs}");
			LogAssert.IsTrue(checkedPrefabs > 0,
				"No predicted character prefab was found — this guard is not actually checking anything.");
		}

		#endregion

		#region View offset composition.

		/// <summary>
		/// The client's claim covers the FULL round trip, not half of it.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The offset is subtracted from the server tick at which the replicate body RUNS. Two
		/// network trips separate that instant from the one the client built the input on: the state
		/// it was looking at travelled server → client, and the input travelled client → server.
		/// Half the round trip covered only the first, so every shot resolved against a world a
		/// one-way trip newer than the one the shooter aimed in — an error that grew with ping, on
		/// exactly the connections compensation exists for.
		/// </para>
		/// <para>
		/// Pinned against the source rather than by calling <c>ResolveViewOffset</c>, which is a
		/// private instance method on a <c>NetworkBehaviour</c> that needs a live TimeManager to say
		/// anything at all. The arithmetic itself is one line and reverting it is one token.
		/// </para>
		/// </remarks>
		[Test]
		public void ViewOffset_UsesFullRoundTrip_NotHalf()
		{
			string source = ReadSource(
				"Assets/Scripts/Shared/Implementation/Entity/Prediction/KCC/KCCPlayer.cs");

			LogAssert.IsFalse(source.Contains("HalfRoundTripTime"),
				"KCCPlayer must not build the view offset from HalfRoundTripTime. The anchor is the " +
				"tick the replicate RUNS on, so the input's own trip to the server is part of the gap " +
				"and both halves of the round trip count.");
			LogAssert.IsTrue(source.Contains("timeManager.RoundTripTime"),
				"The view offset must be built from the full RoundTripTime.");
		}

		/// <summary>
		/// The server adds its own replicate queue depth, which the client cannot see.
		/// </summary>
		/// <remarks>
		/// FishNet deliberately holds <c>StateInterpolation</c> entries in the replicates queue
		/// before consuming one, so an input that has ARRIVED has still not RUN for that many ticks.
		/// The client's claim ends at "sent"; without this term every player was compensated two
		/// ticks short of their real view, whatever their ping.
		/// </remarks>
		[Test]
		public void ViewOffset_AddsTheServerReplicateQueueDepth()
		{
			LogAssert.AreEqual(0u, LagCompensationTick.ResolveReplicateQueueTicks(null),
				"With no network object to read a PredictionManager from, the queue term is zero " +
				"rather than a throw — that loses compensation, it does not break the hit.");

			/* Asserted through the arithmetic rather than by grepping TryResolve's body. The two
			 * halves of the derivation now live in ResolveViewOffset and ResolveAnchor precisely so
			 * they can be exercised directly (see LagCompensationClosedLoopTests, which composes
			 * them); a source-text assertion could only pin one spelling of the addition. */
			const uint anchor = 50_000;
			LogAssert.IsTrue(LagCompensationTick.ResolveAnchor(anchor, 5, 0, 0, out RewindTarget noQueue),
				"A claim of five ticks must resolve.");
			LogAssert.IsTrue(LagCompensationTick.ResolveAnchor(anchor, 5, 0, 2, out RewindTarget withQueue),
				"The same claim with a queue depth must also resolve.");

			LogAssert.AreEqual(anchor - 5u, noQueue.Tick,
				"With no queue the rewind is the client's claim alone.");
			LogAssert.AreEqual(anchor - 7u, withQueue.Tick,
				"The server's replicate queue depth must be ADDED to the client's claim. Without it " +
				"every player is compensated the queue depth short of their real view, whatever " +
				"their ping.");

			string source = ReadSource(
				"Assets/Scripts/Shared/Implementation/Entity/Prediction/LagCompensation/LagCompensationTick.cs");
			LogAssert.IsTrue(source.Contains("predictionManager.StateInterpolation"),
				"The queue depth must be read live from PredictionManager.StateInterpolation rather " +
				"than assumed — it is authored per deployment. No behavioural assertion can cover " +
				"this one: it is about where the number comes from, not what is done with it.");
		}

		/// <summary>
		/// The queue term is added AFTER the client's claim is capped, so it cannot be inflated.
		/// </summary>
		/// <remarks>
		/// <see cref="LagCompensationTick.MaximumCompensationTicks"/> bounds the attacker-controlled
		/// half. Adding the server's own term afterwards keeps it outside anything a client can
		/// influence; folding it in before the cap would let a maximal claim absorb it instead.
		/// </remarks>
		[Test]
		public void ViewOffset_CapsTheClaimBeforeAddingTheServerTerm()
		{
			/* Ordering asserted by its consequence rather than by the order two statements appear
			 * in. An over-cap claim is the only input that can tell the two orderings apart: cap
			 * first and the queue term survives on top of the cap; cap the sum and it is swallowed. */
			const uint anchor = 100_000;
			byte overCap = (byte)(LagCompensationTick.MaximumCompensationTicks + 50);
			const uint queue = 2;

			LogAssert.IsTrue(
				LagCompensationTick.ResolveAnchor(anchor, overCap, 0, queue, out RewindTarget target),
				"An over-cap claim must still resolve; it simply buys no more than the cap.");

			LogAssert.AreEqual(anchor - (LagCompensationTick.MaximumCompensationTicks + queue), target.Tick,
				"MaximumCompensationTicks must cap the CLIENT's claim before the server's queue depth " +
				"is added, so the server-side term is never something a client can inflate — and so a " +
				"deployment that holds more states does not lose that many ticks of compensation for " +
				"its worst-connected players.");

			LogAssert.IsTrue(
				LagCompensationTick.ResolveAnchor(anchor, (byte)LagCompensationTick.MaximumCompensationTicks,
					0, queue, out RewindTarget atCap),
				"A claim exactly at the cap must resolve.");
			LogAssert.AreEqual(atCap.Tick, target.Tick,
				"A claim above the cap must buy exactly what a claim at the cap buys, and no more.");
		}

		private static string ReadSource(string relativePath)
		{
			string path = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), relativePath);
			LogAssert.IsTrue(System.IO.File.Exists(path), $"Expected source at {path}.");
			return System.IO.File.ReadAllText(path);
		}

		#endregion
	}
}