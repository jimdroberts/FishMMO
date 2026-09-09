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
	/// while the observed buff list is replayed from <c>OnSpawnServer</c>. These tests fail if
	/// that replay is removed.
	/// </para>
	/// <para>
	/// The second guard covers the vendored runtime forwarding switch, which no runtime code calls
	/// any more (state forwarding is authored OFF on every prefab and stays off): reconcile deltas
	/// are encoded against a baseline only the owner has been receiving while forwarding was off,
	/// so if anything ever turned forwarding on it must force the next reconcile to be an absolute
	/// snapshot. <c>NetworkObject.SetStateForwarding</c> does that by stamping
	/// <c>ObserverAddedTick</c>, asserted at source level because the branch needs a spawned
	/// server object.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class LateJoinerReplayTests
	{
		/// <summary>
		/// Returns true when <paramref name="type"/> itself declares an override of a
		/// <see cref="NetworkConnection"/>-taking method rather than inheriting the base one.
		/// </summary>
		private static bool DeclaresConnectionMethod(Type type, string name)
		{
			MethodInfo method = type.GetMethod(
				name,
				BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly,
				binder: null,
				types: name == "WritePayload"
					? new[] { typeof(NetworkConnection), typeof(FishNet.Serializing.Writer) }
					: new[] { typeof(NetworkConnection) },
				modifiers: null);
			return method != null;
		}

		/// <summary>
		/// A late-joining observer must receive the current visible buff list at spawn.
		/// </summary>
		/// <remarks>
		/// <para>
		/// It arrives in the SPAWN PAYLOAD, which is the one copy of this list a new observer gets.
		/// There was a second: an <c>OnSpawnServer</c> override broadcasting the same full set to
		/// the same connection. FishNet raises both from one event — <c>WriteSpawn</c> writes the
		/// payload for a connection and <c>OnSpawnServer</c> fires for it — so every observer
		/// entering range was sent the whole strip twice, and it is removed. This test is what
		/// stops the remaining copy from being removed too.
		/// </para>
		/// <para>
		/// The payload is the copy worth keeping: length-framed, ordered with the rest of the
		/// character's state rather than racing it, and incapable of arriving before the object it
		/// describes exists.
		/// </para>
		/// </remarks>
		[Test]
		public void BuffController_ReplaysObservedBuffs_ToLateJoiners()
		{
			LogAssert.IsTrue(DeclaresConnectionMethod(typeof(BuffController), "WritePayload"),
				"BuffController must override WritePayload(NetworkConnection, Writer): the spawn " +
				"payload is what carries the current observed buff list to a client that starts " +
				"observing after the last change. Without it, a targeted character shows an empty " +
				"buff bar until its next buff event — the regression the old " +
				"ObserversRpc(BufferLast) masked.");

			string source = ReadSource(
				"Assets/Scripts/Shared/Implementation/Entity/Prediction/Buff/BuffController.cs");

			int writePayload = source.IndexOf("public override void WritePayload", StringComparison.Ordinal);
			LogAssert.IsTrue(writePayload >= 0, "WritePayload must still be declared.");
			string body = source.Substring(writePayload);
			int bodyEnd = body.IndexOf("\n\t\t/// <summary>", StringComparison.Ordinal);
			if (bodyEnd > 0)
			{
				body = body.Substring(0, bodyEnd);
			}

			LogAssert.IsTrue(body.Contains("BUFF_PAYLOAD_SHAPE_OBSERVED"),
				"The payload must carry an observed shape for a non-owner connection.");
			LogAssert.IsTrue(body.Contains("BuildObservedBuffEntries()"),
				"and it must build that shape from the same method the change broadcast uses, so a " +
				"character looks identical whether you were watching when the buff landed or " +
				"walked up afterwards.");

			LogAssert.IsFalse(DeclaresConnectionMethod(typeof(BuffController), "OnSpawnServer"),
				"BuffController must NOT also broadcast the full strip from OnSpawnServer. FishNet " +
				"raises that from the same event as the payload write, so the observer would be " +
				"sent the entire buff list twice on every entry into range.");
		}

		/// <summary>Reads a repository source file for a source-level assertion.</summary>
		private static string ReadSource(string relativePath)
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), relativePath);
			LogAssert.IsTrue(File.Exists(path), $"{relativePath} not found at {path}.");
			return File.ReadAllText(path).Replace("\r\n", "\n");
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
