using System;
using System.IO;
using NUnit.Framework;
using FishMMO.Shared;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// What an observer is told about another character's activation, and when.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Observers used to learn about a cast only through the ability OBJECT it spawned, so a
	/// self-buff, a pet summon, or any consumable happened in silence. The cast message is sent
	/// from the activation state machine instead of the spawn path, which is what makes it cover
	/// every activation whether or not anything spawns.
	/// </para>
	/// <para>
	/// The arithmetic below is the desync guard: a message that spent a network delay in flight
	/// must start the bar partway through, not restart it.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class CastVisibilityTests
	{
		private const string ActivationPath =
			"Assets/Scripts/Shared/Implementation/Entity/Prediction/Ability/AbilityController.Activation.cs";

		private const string DisplayPath =
			"Assets/Scripts/Client/World/ClientCastNameplateDisplay.cs";

		private static string ReadSource(string relativePath)
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), relativePath);
			LogAssert.IsTrue(File.Exists(path), $"{relativePath} not found at {path}.");
			return File.ReadAllText(path).Replace("\r\n", "\n");
		}

		[Test]
		public void ALateMessageStartsTheBarPartwayRatherThanRestartingIt()
		{
			/* The correction, exercised directly. An observer that received a 3s cast one second
			 * late must show two seconds remaining, not three — otherwise every observed cast
			 * finishes a round trip after the thing it was casting has already landed. */
			const uint interpolation = 2u;

			uint elapsed = AbilityController.ComputeObserverFastForwardTicks(
				estimatedServerTick: 132u, serverSpawnTick: 100u, interpolationTicks: interpolation);

			LogAssert.AreEqual(30u, elapsed,
				"elapsed is the tick difference less the interpolation the observer renders behind");
		}

		[Test]
		public void AMessageFromTheFutureClampsToZeroRatherThanRunningBackwards()
		{
			/* The observer's tick estimate can lag the server's stamp. A negative elapsed would
			 * make the cast appear to have started in the future and, unclamped, wrap. */
			uint elapsed = AbilityController.ComputeObserverFastForwardTicks(
				estimatedServerTick: 100u, serverSpawnTick: 132u, interpolationTicks: 2u);

			LogAssert.AreEqual(0u, elapsed, "an estimate behind the stamp clamps to zero");
		}

		[Test]
		public void TheCastMessageSendsOnlyWhatCannotBeDerived()
		{
			/* Animation is deliberately not implemented yet. The ability type travels anyway so
			 * adding an observer-side trigger later is a handler change and not a wire change. */
			string source = ReadSource("Assets/Scripts/Shared/Implementation/Network/Character/AbilityBroadcasts.cs");

			LogAssert.IsTrue(source.Contains("struct CharacterCastBroadcast"), "the cast message must exist");
			/* Deliberately NOT on the wire. Both are on the template, templates are immutable and
			 * identical on every peer, and the receiver resolves the ability from the caster's own
			 * known set — so sending them would cost per cast per observer for values already in
			 * memory. An animation hook reads the type from the same template. */
			LogAssert.IsFalse(source.Contains("public byte AbilityType"),
				"the ability type is derived from the template, not sent");
			LogAssert.IsFalse(source.Contains("public float Duration"),
				"the duration is derived from the template, not sent");
			LogAssert.IsTrue(source.Contains("public uint ServerTick"),
				"the start tick must travel, or a late message cannot be corrected");
			LogAssert.IsTrue(source.Contains("public long ReferenceID"),
				"an ability is named by its INSTANCE id, which observers hold in KnownAbilities, so the row names the crafted ability rather than its base template");
		}

		[Test]
		public void EveryActivationAnnouncesItselfIncludingOnesThatSpawnNothing()
		{
			/* The reported gap. BroadcastAbilityActivated is gated on SpawnsWorldObject, so a
			 * self-buff or a pet summon reaches observers through nothing at all; the cast message
			 * is sent from the start of the activation instead, where no such gate exists. */
			string source = ReadSource(ActivationPath);

			int abilityStart = source.IndexOf("private bool TryStartAbility", StringComparison.Ordinal);
			int consumableStart = source.IndexOf("private bool TryStartConsumable", StringComparison.Ordinal);
			int cancel = source.IndexOf("internal void Cancel(ReplicateState", StringComparison.Ordinal);
			LogAssert.IsTrue(abilityStart >= 0 && consumableStart >= 0 && cancel >= 0,
				"the three activation seams must still exist");

			LogAssert.IsTrue(Announces(source, abilityStart), "an ability start must announce itself");
			LogAssert.IsTrue(Announces(source, consumableStart), "a consumable start must announce itself");
			LogAssert.IsTrue(Announces(source, cancel), "and every ending must, whatever ended it");
		}

		/// <summary>Whether the method beginning at an offset sends a cast message.</summary>
		private static bool Announces(string source, int start)
		{
			int end = source.IndexOf("\n\t\t/// <summary>", start, StringComparison.Ordinal);
			string body = end > start ? source.Substring(start, end - start) : source.Substring(start);
			return body.Contains("BroadcastCastState(");
		}

		[Test]
		public void TheOwnerIsNeverSentItsOwnCast()
		{
			/* The owner predicts its own activation and draws it from that, which is ahead of
			 * anything the server can send. Feeding it this message would replace a correct local
			 * bar with one a round trip stale — the desync the tick stamp exists to prevent. */
			string source = ReadSource(ActivationPath);
			int sender = source.IndexOf("private void BroadcastCastState", StringComparison.Ordinal);
			LogAssert.IsTrue(sender >= 0, "the sender must still exist");

			string body = source.Substring(sender, Math.Min(1600, source.Length - sender));
			LogAssert.IsTrue(body.Contains("BroadcastToObserversExceptOwner"),
				"the cast message goes to observers only");
			LogAssert.IsTrue(body.Contains("state.ContainsReplayed()"),
				"a replayed tick must not re-announce a cast that is already running");

			string display = ReadSource(DisplayPath);
			LogAssert.IsTrue(display.Contains("caster.IsOwner"),
				"and the receiver refuses the local character as well");
		}

		[Test]
		public void ALostStopCannotStrandANameplate()
		{
			/* The stop is unreliable, so it can be lost. Without an expiry the row would read
			 * "Casting" until that character left view. */
			string display = ReadSource(DisplayPath);

			LogAssert.IsTrue(display.Contains("ExpiryGraceSeconds"),
				"a cast must expire on its own duration when no stop arrives");
			LogAssert.IsTrue(display.Contains("MinimumDwellSeconds"),
				"and an instant cast must stay up long enough to be seen");
		}
	}
}
