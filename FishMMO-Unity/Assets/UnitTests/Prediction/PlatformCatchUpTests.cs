using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using FishMMO.Shared;
using NUnit.Framework;
using UnityEngine;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Guards the moving-platform phase contract: a client that receives a platform snapshot
	/// catches up to the server's phase by re-running the same deterministic step, and stays there.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Before the catch-up, a client started stepping from the payload snapshot as written — a pose
	/// that was already one transit old on arrival — and then stepped one tick per tick forever, so
	/// its platform ran permanently behind the server's by the whole transit. Riders paid for it on
	/// every reconcile: the server measured them against ITS platform, the local motor stood on the
	/// lagging copy, and the correction dragged the character toward a pose that is off (or inside)
	/// the local platform near edges and direction reversals — the lived experience being "falling
	/// through the moving platform".
	/// </para>
	/// <para>
	/// The whole fix rests on one property, and that is what the behavioural test pins:
	/// <c>KCCPlatform.Step</c> is a pure function of (position, goal index, delta), so
	/// snapshot-then-step-K equals step-W-then-K — including the corner snap and goal-index
	/// advance, which is where naive extrapolation (position + velocity × transit) goes wrong.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class PlatformCatchUpTests
	{
		private readonly List<GameObject> spawned = new List<GameObject>();

		[TearDown]
		public void TearDown()
		{
			for (int i = 0; i < spawned.Count; ++i)
			{
				if (spawned[i] != null)
				{
					UnityEngine.Object.DestroyImmediate(spawned[i]);
				}
			}
			spawned.Clear();
		}

		/// <summary>
		/// A twin that starts from the server's tick-W snapshot and steps K more ticks lands
		/// exactly where the server lands after W + K ticks — across several direction reversals.
		/// </summary>
		[Test]
		public void SnapshotPlusCatchUp_EqualsTheServersWalk()
		{
			const float tickDelta = 1f / 30f;
			// Long enough to cross multiple corners at the default 4 u/s over 5 u legs.
			const int snapshotTick = 47;
			const int transitTicks = 23;

			KCCPlatform server = MakePlatform("serverPlatform");
			KCCPlatform client = MakePlatform("clientPlatform");

			// The server walks to the snapshot tick...
			for (int i = 0; i < snapshotTick; ++i)
			{
				server.Step(tickDelta);
			}

			// ...the payload carries its pose and goal index...
			client.transform.position = server.transform.position;
			SetPrivateField(client, "goalIndex", GetPrivateField<byte>(server, "goalIndex"));

			// ...and both sides then advance the same number of ticks: the server live, the
			// client as catch-up plus live ticks. The two walks must be the same walk.
			for (int i = 0; i < transitTicks; ++i)
			{
				server.Step(tickDelta);
				client.Step(tickDelta);
			}

			LogAssert.IsTrue((server.transform.position - client.transform.position).sqrMagnitude < 1e-10f,
				"Snapshot-then-step must reproduce the server's walk exactly — this is the determinism the " +
				"payload catch-up relies on. A drift here means Step reads something outside (position, " +
				"goalIndex, delta) and the platform phase can never be trusted.");
			LogAssert.AreEqual(GetPrivateField<byte>(server, "goalIndex"), GetPrivateField<byte>(client, "goalIndex"),
				"Including the corner snap: both walks must agree which waypoint they are heading for.");
		}

		/// <summary>
		/// SOURCE — the payload actually carries the server tick and the reader actually catches
		/// up. The behavioural half above proves stepping is safe; this half proves it happens.
		/// </summary>
		[Test]
		public void PlatformPayload_CarriesTheTickAndCatchesUp()
		{
			string source = ReadSource("Assets/Scripts/Shared/Implementation/Entity/Prediction/KCC/KCCPlatform.cs");
			LogAssert.IsTrue(source.Contains("writer.WriteUInt32(base.NetworkObject != null && TimeManager != null ? TimeManager.LocalTick : 0u);"),
				"WritePayload must stamp the server tick the snapshot was true on (behind the null-safe " +
				"NetworkObject guard — the TimeManager accessor throws on an unspawned component).");
			LogAssert.IsTrue(source.Contains("uint serverTickAtWrite = reader.ReadUInt32();"),
				"ReadPayload must consume it in the same wire position.");
			LogAssert.IsTrue(source.Contains("ComputeObserverFastForwardTicks("),
				"And catch up with the same transit arithmetic a streamed ability object uses — without " +
				"this the client's platform runs one full transit behind the server's for the whole session, " +
				"and every rider reconcile fights that offset at the platform's edges.");
			LogAssert.IsTrue(source.Contains("MaxCatchUpTicks"),
				"Bounded, so a clock-estimate glitch cannot spin an unbounded catch-up loop on spawn.");
		}

		/// <summary>
		/// The pose ring hands back the position the deck actually held at the end of each tick —
		/// the geometry half of what a rider needs to replay a reconcile honestly.
		/// </summary>
		/// <remarks>
		/// The velocity ring alone was not enough (issue #228). A rider replaying k ticks inherited
		/// the right velocity for each of them while its ground probes — real physics queries —
		/// hit the deck collider wherever it stands NOW, up to a full round trip downstream. At the
		/// shipped platform's 4 u/s a 500 ms round trip is 2 units of a deck only 2.5 units deep,
		/// so a rider standing anywhere in the back of the deck replayed over open air: it
		/// ungrounded, stopped inheriting the platform velocity (the motor only conveys a stably
		/// grounded rider on a horizontal platform), fell, and was hauled back by the next
		/// reconcile — sinking through the deck it was standing on, worst at high ping.
		/// </remarks>
		[Test]
		public void PoseRing_ReturnsThePoseEachTickActuallyHeld()
		{
			const float tickDelta = 1f / 30f;
			KCCPlatform platform = MakePlatform("ringPlatform");

			// Walk the platform the way its tick does — step, then record — remembering the truth.
			const uint firstTick = 500;
			const int ticks = 40;
			Vector3[] truth = new Vector3[ticks];
			for (int i = 0; i < ticks; ++i)
			{
				platform.Step(tickDelta);
				truth[i] = platform.transform.position;
				RecordTickState(platform, firstTick + (uint)i, platform.LastCompletedTickVelocity, truth[i]);
			}

			for (int i = 0; i < ticks; ++i)
			{
				LogAssert.IsTrue(platform.TryGetPositionForTick(firstTick + (uint)i, out Vector3 pose),
					$"Tick {firstTick + (uint)i} is inside the ring and must still be readable — a replay " +
					"that cannot recover a tick's geometry falls back to replaying against the present.");
				LogAssert.IsTrue((pose - truth[i]).sqrMagnitude < 1e-10f,
					"The ring must return the pose that tick actually held, not a neighbouring tick's. " +
					"Riding is decided by where the deck was when the rider's probe ran.");
			}

			/* The ring must move with the deck, not just exist: a ring that returned one frozen
			 * pose for every tick would pass the lookups above if the platform never moved. */
			LogAssert.IsTrue((truth[ticks - 1] - truth[0]).sqrMagnitude > 0.01f,
				"The fixture must actually walk the platform, or it proves nothing.");
		}

		/// <summary>
		/// A tick older than the ring reports a miss rather than a wrong pose — a replay window
		/// longer than the history degrades to the old present-pose behaviour, never to geometry
		/// from some unrelated lap.
		/// </summary>
		[Test]
		public void PoseRing_ReportsAMissForTicksItNoLongerHolds()
		{
			const float tickDelta = 1f / 30f;
			KCCPlatform platform = MakePlatform("ringOverflowPlatform");

			const uint firstTick = 1000;
			const int ringLength = 64;
			for (int i = 0; i < ringLength + 5; ++i)
			{
				platform.Step(tickDelta);
				RecordTickState(platform, firstTick + (uint)i, platform.LastCompletedTickVelocity, platform.transform.position);
			}

			/* Slot reuse is what makes this a real question: tick T and tick T+64 share a slot, so
			 * an implementation that only indexed by slot would happily return the NEWER lap's
			 * pose for the older tick. The stored tick is checked, so it misses instead. */
			LogAssert.IsFalse(platform.TryGetPositionForTick(firstTick, out _),
				"A tick the ring has since overwritten must miss, not return the pose of the tick " +
				"that took its slot — that would place the deck a whole lap away from where the " +
				"replayed rider stood.");
			LogAssert.IsTrue(platform.TryGetPositionForTick(firstTick + ringLength + 4, out _),
				"The most recent tick must still be held.");
		}

		/// <summary>
		/// SOURCE — the rewind is actually wired to FishNet's reconcile, at the three seams that
		/// make it line up with a live tick, and the live pose is always put back.
		/// </summary>
		[Test]
		public void Reconcile_RewindsPlatformGeometryAndRestoresIt()
		{
			string source = ReadSource("Assets/Scripts/Shared/Implementation/Entity/Prediction/KCC/KCCPlatform.cs");

			/* A physics query answers from the last SYNCED collider pose, and FishNet syncs once
			 * per tick after OnTick — so a rider simulating tick T stands on the deck as it was at
			 * the end of T-1. The replay reproduces that relationship by applying the state tick's
			 * pose before the SyncTransforms that follows OnPreReconcile, then each replayed
			 * tick's pose before the Simulate that closes that tick. Move either hook and the
			 * replayed world slips a tick out of step with the live one. */
			LogAssert.IsTrue(source.Contains("manager.OnPreReconcile += PredictionManager_OnPreReconcile"),
				"The deck must be placed at the reconciled tick's pose BEFORE FishNet's pre-replay " +
				"SyncTransforms, or the first replayed tick probes the present.");
			LogAssert.IsTrue(source.Contains("manager.OnPreReplicateReplay += PredictionManager_OnPreReplicateReplay"),
				"And advanced per replayed tick, so each replayed probe sees that tick's geometry.");
			LogAssert.IsTrue(source.Contains("manager.OnPostReconcile += PredictionManager_OnPostReconcile"),
				"And returned to the live pose when the replay ends — a deck left parked in the past " +
				"would desynchronise from the server permanently, which is worse than the bug.");
			LogAssert.IsTrue(source.Contains("private void RestoreLivePose()") &&
				source.Contains("transform.position = livePositionDuringReplay;"),
				"The restore must use the pose saved when the reconcile began, not a recomputed one: " +
				"the ring can miss (short history, a platform spawned mid-window) and the live pose " +
				"is the only value known to be correct in that case.");
			LogAssert.IsTrue(source.Contains("base.IsClientOnlyStarted"),
				"Client-only: the server runs every replicate once and never replays, so it has no " +
				"historic tick to rewind to.");
		}

		// ── Helpers ──────────────────────────────────────────────────────────────────

		/// <summary>
		/// Files one tick into the platform's history ring, exactly as its tick callback does.
		/// Reflection because the recorder is private and its caller needs a spawned NetworkObject.
		/// </summary>
		private static void RecordTickState(KCCPlatform platform, uint tick, Vector3 velocity, Vector3 position)
		{
			MethodInfo method = typeof(KCCPlatform).GetMethod("RecordTickState",
				BindingFlags.Instance | BindingFlags.NonPublic);
			Assert.IsNotNull(method, "KCCPlatform.RecordTickState not found — the history ring was renamed.");
			method.Invoke(platform, new object[] { tick, velocity, position });
		}


		/// <summary>
		/// A platform with its goals installed directly, sidestepping Awake entirely: edit mode
		/// does not run Awake for plain MonoBehaviours, and FishNet's IL post-processing makes
		/// reflecting it unreliable. <c>Step</c> reads only (position, goals, goalIndex, delta),
		/// so identical goal lists on both twins is all the determinism test needs.
		/// </summary>
		private KCCPlatform MakePlatform(string name)
		{
			GameObject go = new GameObject(name);
			spawned.Add(go);
			KCCPlatform platform = go.AddComponent<KCCPlatform>();
			SetPrivateField(platform, "goals", new List<Vector3>
			{
				new Vector3(0f, 0f, 5f),
				new Vector3(0f, 0f, -5f),
			});
			return platform;
		}

		private static void SetPrivateField<T>(object instance, string fieldName, T value)
		{
			FieldInfo field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
			Assert.IsNotNull(field, $"Private field '{fieldName}' not found on {instance.GetType().Name}.");
			field.SetValue(instance, value);
		}

		[Test]
		public void RiderVolume_AlwaysQueriesThePlayerLayer()
		{
			/* Riding regression, reported live 2026-09-01: players stood on platforms (solid
			 * collision fine) while the deck slid out from under them. The rider-detection
			 * NetworkCollision was scene-authored to query Default only, and BaseCharacter.Awake
			 * moves every character to the Player layer at runtime — so OnEnter never fired,
			 * SetPlatform never ran, and SetPlatformVelocity stayed zero. The fix forces the
			 * Player bit into the volume's query layers at Awake, because the requirement is
			 * intrinsic to being a platform and the failure is silent. */
			string platformSource = ReadSource(
				"Assets/Scripts/Shared/Implementation/Entity/Prediction/KCC/KCCPlatform.cs");
			LogAssert.IsTrue(platformSource.Contains("platformCollider.QueryLayers |= required"),
				"KCCPlatform.Awake must force the Player layer into its rider volume's query " +
				"layers — scene data authored without it silently breaks platform riding.");
			LogAssert.IsTrue(platformSource.Contains("Constants.Layers.Index.Player"),
				"The forced bit must come from the Player layer constant, not a hardcoded index.");
		}

		[Test]
		public void QueryLayers_IsExposedOnNetworkColliderBase_AndRoundTrips()
		{
			/* The setter is a tagged FISHMMO EDIT inside FishNet's NetworkColliderBase. A FishNet
			 * upgrade that wipes it makes KCCPlatform.Awake stop compiling loudly — but this test
			 * documents WHY the edit exists so it is re-applied rather than deleted: game code
			 * must be able to guarantee its query layers (see the riding regression above). */
			GameObject go = new GameObject("QueryLayersProbe");
			try
			{
				FishNet.Component.Prediction.NetworkCollision collision =
					go.AddComponent<FishNet.Component.Prediction.NetworkCollision>();
				collision.QueryLayers = (LayerMask)1;
				collision.QueryLayers |= (LayerMask)(1 << 6);
				LogAssert.AreEqual(65, (int)collision.QueryLayers,
					"QueryLayers must read back exactly what was composed into it.");
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(go);
			}
		}

		private static T GetPrivateField<T>(object instance, string fieldName)
		{
			FieldInfo field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
			Assert.IsNotNull(field, $"Private field '{fieldName}' not found on {instance.GetType().Name}.");
			return (T)field.GetValue(instance);
		}

		private static string ReadSource(string relativePath)
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), relativePath);
			LogAssert.IsTrue(File.Exists(path), $"{relativePath} not found at {path}.");
			return File.ReadAllText(path);
		}
	}
}
