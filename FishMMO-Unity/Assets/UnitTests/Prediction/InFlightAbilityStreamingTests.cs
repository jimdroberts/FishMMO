using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using FishMMO.Shared;
using NUnit.Framework;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Pins the audience of the spawn payload's in-flight ability block.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <c>AbilityController.WritePayload</c> writes up to
	/// <c>MAX_PAYLOAD_IN_FLIGHT_OBJECTS</c> live ability objects into every receiver's spawn
	/// payload, and <c>ReadPayload</c> parks them in <c>pendingInFlightObjects</c> for a later
	/// hook to reproduce. The block exists for the OBSERVER case — the window where somebody
	/// walks into range while a fireball is already crossing the sky and sees nothing until the
	/// damage lands — so the hook that drains it has to be one that runs on observers.
	/// </para>
	/// <para>
	/// It used to be <c>OnStartCharacter</c>, which is not such a hook.
	/// <c>PlayerCharacter.TryInitializeLocalClient</c> is the only thing in the project that fans
	/// that callback out over a character's behaviours, and it returns immediately unless
	/// <c>base.IsOwner</c> — so the drain ran only on the caster's own client, the one receiver
	/// that least needs it, while every observer read the bytes and dropped them. The failure was
	/// invisible: no error, no log, just a projectile that was never drawn.
	/// </para>
	/// <para>
	/// Asserted on the source and on the method table rather than by spawning a character,
	/// because reproducing it live needs a server, an owning connection, a second observing
	/// connection and a live ability mid-flight, none of which an EditMode test can build.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class InFlightAbilityStreamingTests
	{
		private const string ControllerPath =
			"Assets/Scripts/Shared/Implementation/Entity/Prediction/Ability/AbilityController.Networking.cs";

		private const string PlayerCharacterPath =
			"Assets/Scripts/Shared/Implementation/Entity/PlayerCharacter.cs";

		/// <summary>
		/// The in-flight drain must not hang off an owner-only hook.
		/// </summary>
		[Test]
		public void InFlightDrain_IsNotReachedOnlyFromTheOwnerOnlyHook()
		{
			string source = ReadSource(ControllerPath);

			string onStartCharacter = BodyOf(source, "OnStartCharacter");
			LogAssert.IsNotNull(onStartCharacter,
				"AbilityController.Networking.cs no longer declares OnStartCharacter; this test needs updating.");
			LogAssert.IsFalse(onStartCharacter.Contains("MaterializePendingInFlightObjects"),
				"MaterializePendingInFlightObjects must not be driven from OnStartCharacter. That callback is " +
				"fanned out only by PlayerCharacter.TryInitializeLocalClient, which returns unless base.IsOwner " +
				"— so an observer never reproduces the ability objects the server put in its spawn payload, and " +
				"walks into a fight seeing an empty sky while projectiles are already in the air.");

			string onStartClient = BodyOf(source, "OnStartClient");
			LogAssert.IsNotNull(onStartClient,
				"AbilityController must override OnStartClient: it is the callback that runs once per CLIENT " +
				"per spawn — owner and observers alike — after FishNet has read every behaviour's payload.");
			LogAssert.IsTrue(onStartClient.Contains("MaterializePendingInFlightObjects"),
				"OnStartClient must drain pendingInFlightObjects. It is the only hook on this controller that " +
				"reaches an observer, which is the receiver the in-flight payload block was written for.");
		}

		/// <summary>
		/// The override actually exists on the type, not just in the file this test read.
		/// </summary>
		[Test]
		public void AbilityController_OverridesOnStartClient()
		{
			MethodInfo method = typeof(AbilityController).GetMethod(
				"OnStartClient",
				BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);

			LogAssert.IsNotNull(method,
				"AbilityController must declare its own OnStartClient. Without it the in-flight ability block " +
				"in the spawn payload is read by every observer and then discarded.");
		}

		/// <summary>
		/// The premise the fix rests on: OnStartCharacter is owner-only, project wide.
		/// </summary>
		/// <remarks>
		/// If this ever stops being true the reasoning above changes, and the drain could legitimately
		/// move back. Pinned so that becomes a visible decision rather than a silent one.
		/// </remarks>
		[Test]
		public void OnStartCharacter_IsFannedOutOnlyToTheOwner()
		{
			string source = ReadSource(PlayerCharacterPath);

			string initializer = BodyOf(source, "TryInitializeLocalClient");
			LogAssert.IsNotNull(initializer,
				"PlayerCharacter.TryInitializeLocalClient not found; it is the only fan-out of OnStartCharacter.");
			LogAssert.IsTrue(initializer.Contains("OnStartCharacter"),
				"TryInitializeLocalClient is expected to be the site that calls OnStartCharacter on each behaviour.");
			LogAssert.IsTrue(initializer.Contains("!base.IsOwner"),
				"TryInitializeLocalClient is expected to refuse a non-owner. Any behaviour placing observer-facing " +
				"work in OnStartCharacter is relying on a callback that never reaches an observer.");
		}

		// ── Helpers ──────────────────────────────────────────────────────────────────

		private static string ReadSource(string relativePath)
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), relativePath);
			LogAssert.IsTrue(File.Exists(path), $"{relativePath} not found at {path}.");
			return File.ReadAllText(path);
		}

		/// <summary>
		/// The brace-matched body of the first method whose declaration names
		/// <paramref name="methodName"/>, or null when there is none.
		/// </summary>
		/// <remarks>
		/// Brace matching rather than "the next N lines", so a body that grows or gains a nested
		/// block does not quietly fall outside what the assertion is reading. Crude enough to be
		/// fooled by a brace inside a string literal; none of the bodies it is pointed at has one.
		/// </remarks>
		private static string BodyOf(string source, string methodName)
		{
			Match declaration = Regex.Match(source, @"\b" + Regex.Escape(methodName) + @"\s*\([^)]*\)\s*\{");
			if (!declaration.Success)
			{
				return null;
			}

			int index = declaration.Index + declaration.Length - 1;
			int depth = 0;
			for (int i = index; i < source.Length; ++i)
			{
				if (source[i] == '{')
				{
					++depth;
				}
				else if (source[i] == '}')
				{
					--depth;
					if (depth == 0)
					{
						return source.Substring(index, i - index + 1);
					}
				}
			}
			return null;
		}
	}
}
