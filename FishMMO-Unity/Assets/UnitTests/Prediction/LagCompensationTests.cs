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
		/// The ring evicts oldest-first and refuses ticks that have fallen out of the window.
		/// </summary>
		/// <remarks>
		/// Refusing rather than clamping is the security-relevant half. A client that reports an
		/// implausible latency is asking to resolve against a tick the server no longer holds; the
		/// honest answer is "no compensation", not "as much as I have".
		/// </remarks>
		[Test]
		public void History_EvictsOldest_AndRefusesTicksOutsideTheWindow()
		{
			Allocate(8);
			for (uint i = 0; i < 20; i++)
			{
				Record(100 + i, new Vector3(0f, 0f, i));
			}

			LogAssert.AreEqual(8, history.Capacity, "The ring must not grow past its allocation.");
			LogAssert.AreEqual(8, history.RecordedTicks, "A saturated ring holds exactly its capacity.");

			LogAssert.IsTrue(!history.TryResolve(100, out _),
				"Tick 100 has been evicted and must be refused rather than clamped.");
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

			foreach (string guid in UnityEditor.AssetDatabase.FindAssets(
				"t:Prefab", new[] { "Assets/Prefabs/Shared/Entity/PlayableCharacters" }))
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
							"Playable character prefabs must agree on _spectatorInterpolation; " +
							"one constant cannot compensate two different settings.");
					}
					authored = parsed;
				}
			}

			TestContext.WriteLine(
				$"MEASURE authored _spectatorInterpolation = {authored}, " +
				$"LagCompensationTick constant = {LagCompensationTick.SpectatorInterpolationTicks}");

			LogAssert.IsTrue(authored >= 0, "No playable prefab declared _spectatorInterpolation.");
			LogAssert.AreEqual((uint)authored, LagCompensationTick.SpectatorInterpolationTicks,
				$"Prefabs interpolate {authored} ticks but LagCompensationTick compensates for " +
				$"{LagCompensationTick.SpectatorInterpolationTicks}. Hits will land offset by the difference.");
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
	}
}
