using System;
using System.IO;
using NUnit.Framework;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Proofs for how a player pins and unpins a target, held against the controller's source.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The behaviour these pin is an ordering and a wiring, not arithmetic, so there is no value to
	/// feed a rule the way <see cref="PinnedTargetRulesTests"/> does. What the issue behind this
	/// file turned on was not that a rule was wrong but that the two halves of the key were
	/// arranged so the second was unreachable: the pin release sat behind a test on what the
	/// pointer happened to be over, so a player with an enemy under the crosshair could not let go
	/// of a pin at all.
	/// </para>
	/// <para>
	/// That arrangement is one edit away from coming back, and no unit test on a
	/// <see cref="FishMMO.Shared.TargetController"/> can catch it — the component needs a spawned
	/// network object, an owner and a camera before any of these methods do anything. Source
	/// assertions are the weaker tool and are used here on purpose: they are what this project
	/// already reaches for when the defect is a contract rather than a calculation.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class PinnedTargetInteractionTests
	{
		private const string TargetControllerPath =
			"Assets/Scripts/Shared/Implementation/Entity/Target/TargetController.cs";
		private const string InputControllerPath =
			"Assets/Scripts/Client/Input/PlayerInputController.cs";

		private static string TargetController => ReadSource(TargetControllerPath);
		private static string InputController => ReadSource(InputControllerPath);

		/// <summary>The text of a source file, with its line endings normalised to \n.</summary>
		/// <remarks>
		/// A proof below matches a pattern spanning a line break. Whether a working tree stores a
		/// file LF or CRLF is decided by git on checkout and by each developer's core.autocrlf, so
		/// a bound written with \n silently stops matching on a Windows checkout and the assertion
		/// then reports the code as missing while it sits there unchanged.
		/// </remarks>
		private static string ReadSource(string relativePath)
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), relativePath);
			LogAssert.IsTrue(File.Exists(path), $"{relativePath} not found at {path}.");
			return File.ReadAllText(path).Replace("\r\n", "\n");
		}

		/// <summary>
		/// The body of a method, from its signature to its matching closing brace.
		/// </summary>
		/// <param name="source">The file's source text.</param>
		/// <param name="signature">A signature fragment that occurs exactly once in the file.</param>
		/// <returns>The text of the method, signature included.</returns>
		/// <remarks>
		/// Brace counting is enough here and is why this is not a regex: the methods it is pointed
		/// at contain string literals, comments and nested blocks, and a pattern loose enough to
		/// match them all is loose enough to match the next method too.
		/// </remarks>
		private static string MethodBody(string source, string signature)
		{
			int start = source.IndexOf(signature, StringComparison.Ordinal);
			LogAssert.IsTrue(start >= 0, $"'{signature}' must exist in the target controller.");

			int open = source.IndexOf('{', start);
			LogAssert.IsTrue(open >= 0, $"'{signature}' must open a body.");

			int depth = 0;
			for (int i = open; i < source.Length; ++i)
			{
				if (source[i] == '{')
				{
					++depth;
				}
				else if (source[i] == '}')
				{
					if (--depth == 0)
					{
						return source.Substring(start, i - start + 1);
					}
				}
			}

			Assert.Fail($"'{signature}' has no matching closing brace.");
			return null;
		}

		/// <summary>
		/// The pin key releases before it pins, and does not ask what is under the pointer first.
		/// </summary>
		/// <remarks>
		/// This is the defect. The key used to read the hovered transform first and take the
		/// release branch only when that transform was null or was the pinned character itself —
		/// so in a fight, where the pointer is on an enemy essentially always, the release branch
		/// was unreachable and pressing the key on a second enemy silently moved the pin. Ordering
		/// is the whole contract, which is why it is asserted rather than merely commented.
		/// </remarks>
		[Test]
		public void PinKey_ReleasesBeforeItReadsThePointer()
		{
			string body = MethodBody(TargetController, "public bool TogglePinnedTarget()");

			int release = body.IndexOf("ClearPinnedTarget();", StringComparison.Ordinal);
			int readPointer = body.IndexOf("Current.Target", StringComparison.Ordinal);
			int pin = body.IndexOf("TryPinTarget(hovered)", StringComparison.Ordinal);

			LogAssert.IsTrue(release >= 0, "The key must be able to release a pin.");
			LogAssert.IsTrue(pin >= 0, "The key must still be able to pin a hovered character.");
			LogAssert.IsTrue(readPointer >= 0, "The key pins what the pointer is on.");

			LogAssert.IsTrue(release < readPointer,
				"A held pin is released whatever the pointer is on: the release must be decided before the hovered target is even read.");
			LogAssert.IsTrue(release < pin,
				"Release comes first, or a player aiming at a second enemy can never let go of the first.");
		}

		/// <summary>
		/// The release is decided on reference identity, so a target destroyed since the last
		/// trace tick still takes the release branch rather than falling through to a pin.
		/// </summary>
		[Test]
		public void PinKey_TestsThePinByReferenceIdentity()
		{
			string body = MethodBody(TargetController, "public bool TogglePinnedTarget()");

			LogAssert.IsTrue(body.Contains("ReferenceEquals(pinnedTarget, null)"),
				"Unity's overloaded == reports a destroyed transform as null, which would read as 'nothing pinned' and skip the release.");
		}

		/// <summary>
		/// The pinned target is released when the player holding it dies.
		/// </summary>
		/// <remarks>
		/// The rule itself is proven in <see cref="PinnedTargetRulesTests"/>; what is checked here
		/// is that the controller actually asks. A rule with no caller is the shape this whole
		/// issue takes — <c>ClearPinnedTarget</c> and <c>TryPinTarget</c> both sat on the public
		/// interface with nothing calling them.
		/// </remarks>
		[Test]
		public void PinRelease_ConsultsTheHoldersLife()
		{
			string body = MethodBody(TargetController, "private void ValidatePinnedTarget()");

			LogAssert.IsTrue(body.Contains("ShouldRelease("),
				"The trace tick must still defer the decision to the shared rule.");
			LogAssert.IsTrue(body.Contains("IsOwnerAlive"),
				"The holder's own death must be one of the facts handed to it.");
		}

		/// <summary>
		/// The holder's life is read from health, never from immortality.
		/// </summary>
		/// <remarks>
		/// A player is briefly immortal across a teleport. Reading that as death would drop the
		/// pin on every scene transfer and every re-seat — the same class of mistake as the
		/// immortal-NPC short-circuit, which is sound only because it is scoped to NPCs.
		/// </remarks>
		[Test]
		public void HoldersLife_IsHealthNotImmortality()
		{
			string body = MethodBody(TargetController, "private bool IsOwnerAlive");

			LogAssert.IsTrue(body.Contains("IsAlive"),
				"IsAlive is health above zero, which is what 'the player is dead' means.");
			LogAssert.IsFalse(body.Contains("Immortal"),
				"Immortal is true for a teleporting player; a pin must ride through a teleport, not be dropped by one.");
		}

		/// <summary>
		/// Despawn releases the pin through the event, so the frame comes down with it.
		/// </summary>
		/// <remarks>
		/// <c>ForgetPinnedTarget</c> drops the pin silently, for the teardown paths that are
		/// clearing the subscribers as well. A despawn is not always one of those: it is also how
		/// a player leaves a scene, and the target frame outlives that on the login path. Dropping
		/// the pin without raising left a pinned card on screen with nothing behind it and no
		/// event coming to take it down.
		/// </remarks>
		[Test]
		public void TeardownRelease_RaisesTheUnpinEvent()
		{
			string body = MethodBody(TargetController, "public override void ResetState(bool asServer)");

			LogAssert.IsTrue(body.Contains("ClearPinnedTarget();"),
				"ResetState must release through the raising path.");
			LogAssert.IsFalse(body.Contains("ForgetPinnedTarget();"),
				"A silent drop here orphans whichever card is still framing the pin.");
		}

		/// <summary>
		/// The pin key is reachable at all.
		/// </summary>
		/// <remarks>
		/// The one wire that would make every proof above moot. What the key is bound to, and
		/// whether that key belongs to anything else, is a question about the input asset and is
		/// answered in <see cref="ShippedKeyBindingTests"/>; this only insists the handler exists,
		/// so a rename cannot leave the action bound to a method that is no longer there.
		/// </remarks>
		[Test]
		public void PinKey_IsHandled()
		{
			string input = ReadSource(InputControllerPath);

			LogAssert.IsTrue(input.Contains("Controls.Player.PinTarget.performed += OnPinTargetPerformed;"),
				"The pin action must be subscribed where every other Player action is.");
			LogAssert.IsTrue(input.Contains("Controls.Player.PinTarget.performed -= OnPinTargetPerformed;"),
				"and unsubscribed again, or the callback outlives the controller that owns it.");
		}

		/// <summary>
		/// The pin key is not swallowed by the state the player is most often in when they reach
		/// for it — a free cursor, pointing at a character.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The pin handler used to gate on <c>CanUpdateInput()</c>, which requires the cursor to be
		/// locked. Aiming the mouse at a character can only be done with the cursor free, and the
		/// hover frame follows that free pointer, so the one state in which a player can see what
		/// they are about to pin was also the state in which the key did nothing.
		/// </para>
		/// <para>
		/// This is asserted on the source because the gate is a property of the handler and not of
		/// any rule: <c>OnPinTargetPerformed</c> is private, takes an input callback, and needs a
		/// local character to reach the controller at all.
		/// </para>
		/// </remarks>
		[Test]
		public void PinKey_ActsWithAFreeCursor()
		{
			string body = MethodBody(ReadSource(InputControllerPath), "private void OnPinTargetPerformed(InputAction.CallbackContext context)");

			LogAssert.IsFalse(body.Contains("CanUpdateInput"),
				"The pin changes what the HUD shows, not what the character does, so it must not require the gameplay input state.");
			LogAssert.IsTrue(body.Contains("TypingIntoField"),
				"Typing is still a reason to ignore the key.");
			LogAssert.IsTrue(body.Contains("TogglePinnedTarget()"),
				"and the handler must still reach the toggle.");
		}
	}
}
