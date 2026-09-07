using System.Reflection;
using NUnit.Framework;
using FishMMO.Client;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The name the Options panel prints on a key-binding button.
	/// </summary>
	/// <remarks>
	/// Reported as "some bound keys show up as squares". The device's name for a key is what the
	/// operating system reports, and for Escape that is the ESC control character, which the UI
	/// font can only draw as a box. These pin the choice between the device's name and the
	/// layout's name so a key always reads as a word or a visible character.
	/// </remarks>
	[TestFixture]
	public class KeyBindingDisplayNameTests
	{
		private static string Readable(string display, string layoutName)
		{
			MethodInfo method = typeof(UITKOptions).GetMethod("ReadableKeyName", BindingFlags.Static | BindingFlags.NonPublic);
			LogAssert.IsNotNull(method, "UITKOptions must still declare ReadableKeyName");
			return (string)method.Invoke(null, new object[] { display, layoutName });
		}

		[Test]
		public void AControlCharacter_FallsBackToTheLayoutName()
		{
			LogAssert.AreEqual("Escape", Readable("", "Escape"), "ESC is not a name");
		}

		[Test]
		public void WhitespaceAlone_FallsBackToTheLayoutName()
		{
			LogAssert.AreEqual("Space", Readable(" ", "Space"), "a blank button says nothing");
			LogAssert.AreEqual("Tab", Readable("\t", "Tab"), "nor does a tab");
		}

		[Test]
		public void APrintableDeviceName_IsKeptAsIs()
		{
			LogAssert.AreEqual("Q", Readable("Q", "Q"), "a plain key");
			LogAssert.AreEqual("ö", Readable("ö", "Semicolon"), "the player's own layout wins over the US layout's word");
			LogAssert.AreEqual("Left Shift", Readable("Left Shift", "Left Shift"), "a multi-word name");
			LogAssert.AreEqual("B", Readable("B", "Button East"), "and a gamepad face button");
		}

		[Test]
		public void NothingAnywhere_ReadsUnbound()
		{
			LogAssert.AreEqual("Unbound", Readable("", ""), "no path at all");
			LogAssert.AreEqual("Unbound", Readable(null, null), "or nulls");
			LogAssert.AreEqual("Unbound", Readable("", ""), "a control character with no layout name to fall back on");
		}

		[Test]
		public void AnUnknownGlyph_InThePrivateUseArea_IsNotAName()
		{
			/* Some device names are private-use glyphs meant for a vendor font the UI does not
			 * ship. They would draw as the same box, so they fall through as well. */
			LogAssert.AreEqual("Escape", Readable("", "Escape"), "private-use glyph");
		}
	}
}
