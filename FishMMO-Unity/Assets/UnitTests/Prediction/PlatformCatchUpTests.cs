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

		// ── Helpers ──────────────────────────────────────────────────────────────────

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
