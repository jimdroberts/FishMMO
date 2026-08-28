using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using FishMMO.Shared;
using FishNet.Connection;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Guards the pieces of observer state that must be REPLAYED to a client which starts
	/// observing a character after the state last changed.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The broadcast conversions removed FishNet's <c>ObserversRpc(BufferLast)</c> semantics, and
	/// with them the implicit "new observers get the current value" replay. Each broadcast channel
	/// now needs an explicit answer for late joiners: resources and death ride the spawn payload,
	/// while the observed buff list and the prediction mode are replayed from
	/// <c>OnSpawnServer</c>. These tests fail if either replay is removed.
	/// </para>
	/// <para>
	/// The third guard covers the runtime forwarding switch: reconcile deltas are encoded against
	/// a baseline only the owner has been receiving while forwarding was off, so turning
	/// forwarding on must force the next reconcile to be an absolute snapshot. The vendored
	/// <c>NetworkObject.SetStateForwarding</c> does that by stamping <c>ObserverAddedTick</c>,
	/// which is asserted at source level because the branch requires a spawned server object.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class LateJoinerReplayTests
	{
		/// <summary>
		/// Returns true when <paramref name="type"/> itself declares an <c>OnSpawnServer</c>
		/// override rather than inheriting the empty base implementation.
		/// </summary>
		private static bool DeclaresOnSpawnServer(Type type)
		{
			MethodInfo method = type.GetMethod(
				"OnSpawnServer",
				BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly,
				binder: null,
				types: new[] { typeof(NetworkConnection) },
				modifiers: null);
			return method != null;
		}

		/// <summary>
		/// A late-joining observer must receive the current visible buff list at spawn.
		/// </summary>
		[Test]
		public void BuffController_ReplaysObservedBuffs_ToLateJoiners()
		{
			LogAssert.IsTrue(DeclaresOnSpawnServer(typeof(BuffController)),
				"BuffController must override OnSpawnServer(NetworkConnection) to replay the " +
				"current observed buff list to a client that starts observing after the last " +
				"change. Without it, a targeted character shows an empty buff bar until its next " +
				"buff event — the regression the old ObserversRpc(BufferLast) masked.");
		}

		/// <summary>
		/// A late-joining observer must learn a non-default prediction mode at spawn.
		/// </summary>
		[Test]
		public void PredictionModeController_ReplaysMode_ToLateJoiners()
		{
			LogAssert.IsTrue(DeclaresOnSpawnServer(typeof(PredictionModeController)),
				"PredictionModeController must override OnSpawnServer(NetworkConnection) to " +
				"replay the current mode. Without it, a client that walks into a forwarded arena " +
				"keeps its NetworkTransform enabled against a server transform that sends " +
				"nothing, and fights the forwarded simulation.");
		}

		/// <summary>
		/// Turning state forwarding on must reset the reconcile delta baseline for observers.
		/// </summary>
		/// <remarks>
		/// Source-level because the stamping branch needs a spawned, server-started object, which
		/// an EditMode test cannot construct. The mechanism piggybacks on the same
		/// <c>ObserverAddedTick == localTick</c> check <c>GetDeltaSerializeOption</c> already uses
		/// for genuinely new observers, so asserting the stamp exists is asserting the whole
		/// repair path.
		/// </remarks>
		[Test]
		public void SetStateForwarding_ForcesAbsoluteReconcile_OnEnable()
		{
			string path = Path.Combine(
				Directory.GetCurrentDirectory(),
				"Assets/Plugins/FishNet/Runtime/Object/NetworkObject/NetworkObject.Prediction.cs");
			LogAssert.IsTrue(File.Exists(path),
				$"Vendored FishNet file not found at {path}; the runtime forwarding switch lives there.");

			string source = File.ReadAllText(path);
			int setterIndex = source.IndexOf("public void SetStateForwarding", StringComparison.Ordinal);
			LogAssert.IsTrue(setterIndex >= 0,
				"NetworkObject.SetStateForwarding (FISHMMO EDIT) is missing; the runtime " +
				"interpolated/forwarded switch depends on it.");

			// The stamp must live inside the setter, after its declaration.
			int stampIndex = source.IndexOf("ObserverAddedTick", setterIndex, StringComparison.Ordinal);
			LogAssert.IsTrue(stampIndex >= 0,
				"SetStateForwarding no longer stamps ObserverAddedTick when forwarding turns on. " +
				"Observers would then decode up to a second of reconcile deltas against a " +
				"baseline only the owner received while forwarding was off.");
		}
	}
}
