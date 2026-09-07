using System.Collections.Generic;
using System.IO;
using System.Reflection;
using FishMMO.Shared;
using NUnit.Framework;
using UnityEngine;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Guards the moving-platform prediction contract as it stands after issue #228: the platform
	/// is on FishNet's own forwarded model, its step is deterministic so a reconcile replay
	/// reproduces the server's walk, the motor actually conveys a rider, and the rider consults the
	/// platform's velocity ring only in its own tick domain.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Three platform reports in a week ("standing on platforms but not moving with them",
	/// "falling through the moving platform", "still") came down to two defects this fixture now
	/// pins. The rider was never carried: <c>SetPlatformVelocity</c> fed <c>BaseVelocity</c>, which
	/// the controller overwrites before the move. And the client platform was dead-reckoned
	/// open-loop from a one-shot payload — seeded behind the server and never rolled back — so a
	/// reconcile replay probed a deck up to a round trip away from where the rider stood.
	/// </para>
	/// <para>
	/// The fixture keeps its historical name; the catch-up it was written for is gone, replaced by
	/// the model it should have had: forwarding on, FishNet reconciling and replaying the platform.
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
		/// A twin that starts from the server's tick-W state and steps K more ticks lands exactly
		/// where the server lands after W + K ticks — across several direction reversals. This is
		/// the determinism a reconcile replay relies on: rollback to the server's state, replay the
		/// same steps, arrive at the same deck.
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

			// ...the reconcile carries its pose and goal index...
			client.transform.position = server.transform.position;
			SetPrivateField(client, "goalIndex", GetPrivateField<byte>(server, "goalIndex"));

			// ...and both sides then advance the same number of ticks: the server live, the
			// client as replay. The two walks must be the same walk.
			for (int i = 0; i < transitTicks; ++i)
			{
				server.Step(tickDelta);
				client.Step(tickDelta);
			}

			LogAssert.IsTrue((server.transform.position - client.transform.position).sqrMagnitude < 1e-10f,
				"Rollback-then-step must reproduce the server's walk exactly — this is the determinism a " +
				"reconcile replay relies on. A drift here means Step reads something outside (position, " +
				"goalIndex, delta) and the platform phase can never be trusted.");
			LogAssert.AreEqual(GetPrivateField<byte>(server, "goalIndex"), GetPrivateField<byte>(client, "goalIndex"),
				"Including the corner snap: both walks must agree which waypoint they are heading for.");
		}

		/// <summary>
		/// SOURCE — the platform runs FishNet's demo tick model on every peer: replicate plus
		/// reconcile from <c>TimeManager_OnTick</c>, no server-only branch, no client-side stepping,
		/// no payload catch-up, no hand-rolled rollback.
		/// </summary>
		/// <remarks>
		/// Each of those absences is a thing the previous model needed because forwarding was off.
		/// Their return would mean forwarding was switched off again (see
		/// <c>InterestManagementWiringTests.OnlyPlatforms_ShipWithStateForwardingOn</c>) and the
		/// open-loop client platform came back with it.
		/// </remarks>
		[Test]
		public void Platform_RunsTheUpstreamTickModel_OnEveryPeer()
		{
			string source = ReadSource("Assets/Scripts/Shared/Implementation/Entity/Prediction/KCC/KCCPlatform.cs");
			int onTick = source.IndexOf("protected override void TimeManager_OnTick()", System.StringComparison.Ordinal);
			int next = source.IndexOf("public override void CreateReconcile()", onTick, System.StringComparison.Ordinal);
			LogAssert.IsTrue(onTick >= 0 && next > onTick, "TimeManager_OnTick and CreateReconcile must both exist, in that order.");
			string body = source.Substring(onTick, next - onTick);

			LogAssert.IsTrue(body.Contains("PerformReplicate(default);") && body.Contains("CreateReconcile();"),
				"OnTick must run the replicate and build the reconcile on EVERY peer — the client's " +
				"PerformReplicate is what FishNet routes through Replicate_NonAuthoritative to move the " +
				"platform ahead of the server, and the reconcile is the client's fallback state.");
			LogAssert.IsFalse(body.Contains("IsServerStarted") || body.Contains("Step((float)"),
				"OnTick must not branch on the server or step the platform by hand: that was the " +
				"forwarding-off workaround, and a client platform stepped outside the replicate is " +
				"never rolled back by a reconcile.");
			LogAssert.IsFalse(source.Contains("ComputeObserverFastForwardTicks") || source.Contains("serverTickAtWrite"),
				"No payload catch-up. The reconcile places the platform; a fast-forward at spawn aligned " +
				"it to the interpolated observer frame, BEHIND the server, the opposite of what a deck " +
				"the owner stands on needs.");
			LogAssert.IsFalse(source.Contains("OnPreReplicateReplay") || source.Contains("TryGetPositionForTick"),
				"No hand-rolled geometry rewind. FishNet rolls a forwarded object back itself; a second " +
				"rewind on top of it would fight the reconcile.");
			LogAssert.IsTrue(source.Contains("PredictionManager.ClientReplayTick, LastCompletedTickVelocity"),
				"A replayed tick must refresh the velocity ring under the client replay tick, so a rider " +
				"replaying the same tick reads the replayed value rather than the original prediction's.");
		}

		/// <summary>
		/// SOURCE — the motor conveys the platform velocity through KCC's attached-rigidbody seam,
		/// which the controller cannot overwrite, and never through <c>BaseVelocity</c>.
		/// </summary>
		/// <remarks>
		/// The behavioural half lives in <c>PlatformSimPlayModeTests</c> (a rider standing still
		/// on the ferry must move exactly with it); this half names the seam so the reason is
		/// findable from the code. <c>BaseVelocity += _platformVelocity</c> is the exact line that
		/// made every build before issue #228 drop riders off moving decks: UpdateVelocity replaces
		/// BaseVelocity on the same tick, and with the shipped sharpness (10000) the Lerp clamps
		/// to its target and keeps nothing of what was added.
		/// </remarks>
		[Test]
		public void Motor_ConveysPlatformVelocity_WhereTheControllerCannotEraseIt()
		{
			string motor = ReadSource("Assets/Plugins/KinematicCharacterController/Core/KinematicCharacterMotor.cs");
			LogAssert.IsFalse(motor.Contains("BaseVelocity += _platformVelocity"),
				"The platform velocity must never be added to BaseVelocity: CharacterController.UpdateVelocity " +
				"rewrites BaseVelocity before the move and the carry is lost on the same tick.");
			LogAssert.IsTrue(motor.Contains("_attachedRigidbodyVelocity = platformCarry;") &&
				motor.Contains("InternalCharacterMove(ref _attachedRigidbodyVelocity, deltaTime);"),
				"The carry must ride _attachedRigidbodyVelocity and be moved by InternalCharacterMove — " +
				"the same path KCC uses for a PhysicsMover, applied after UpdateVelocity has run.");
			LogAssert.IsTrue(motor.Contains("platformCarry != Vector3.zero && _attachedRigidbody == null"),
				"A real attached rigidbody must keep precedence; the platform seam fills in only when " +
				"KCC found nothing to attach to.");
		}

		/// <summary>
		/// SOURCE — the rider consults the platform's velocity ring on the client only.
		/// </summary>
		/// <remarks>
		/// The ring is keyed in the client's tick domain (LocalTick live, ClientReplayTick during a
		/// replay). A replicate's tick is the owning client's unsynchronised counter, so on the
		/// server the same lookup against a server-keyed ring is an arbitrary hit or a miss — and
		/// the server never replays, so its live value is the correct one.
		/// </remarks>
		[Test]
		public void Rider_ReadsThePlatformRing_OnlyOnTheClient()
		{
			string player = ReadSource("Assets/Scripts/Shared/Implementation/Entity/Prediction/KCC/KCCPlayer.cs");
			LogAssert.IsTrue(player.Contains("if (base.IsServerStarted ||") &&
				player.Contains("!currentPlatform.TryGetVelocityForTick(input.GetTick(), out platformVelocity))"),
				"KCCPlayer must take LastCompletedTickVelocity on the server and consult the ring only on " +
				"the client, where the input tick and the ring share a domain.");
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
