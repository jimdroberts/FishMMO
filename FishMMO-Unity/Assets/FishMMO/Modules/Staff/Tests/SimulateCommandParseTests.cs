using NUnit.Framework;
using FishMMO.Server.Implementation.World.SceneServer;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;
using Activity = FishMMO.Server.Implementation.World.SceneServer.StaffSystem.SimulatedActivity;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// <c>/admin simulate &lt;count&gt; [seconds] [walk|combat] [radius] [chat]</c>: after the count, up to two numbers (the
	/// run's seconds, 0 untimed, then the radius) and the mode words, in any order.
	/// </summary>
	[TestFixture]
	public class SimulateCommandParseTests
	{
		private static StaffSystem.SimulatedSettings Parse(string text, out int seconds)
		{
			string[] words = text.Split(' ');
			LogAssert.IsTrue(StaffSystem.TryParseSimulationOptions(words, out seconds, out StaffSystem.SimulatedSettings settings, out string problem),
				$"'{text}' parses ({problem})");
			return settings;
		}

		private static void AssertRefused(string text)
		{
			LogAssert.IsFalse(StaffSystem.TryParseSimulationOptions(text.Split(' '), out _, out _, out string problem), $"'{text}' is refused");
			LogAssert.IsFalse(string.IsNullOrEmpty(problem), $"and '{text}' says why");
		}

		[Test]
		public void CountAlone_IsAnUntimedWalkNearby()
		{
			StaffSystem.SimulatedSettings settings = Parse("50", out int seconds);
			LogAssert.AreEqual(0, seconds);
			LogAssert.AreEqual(Activity.Walk, settings.Activity);
			LogAssert.AreEqual(25f, settings.Radius);
			LogAssert.IsFalse(settings.Chat);
		}

		[Test]
		public void TheOldTimedForm_StillWorks()
		{
			Parse("50 120", out int seconds);
			LogAssert.AreEqual(120, seconds);
		}

		[Test]
		public void EverythingTogether_InAnyOrder()
		{
			StaffSystem.SimulatedSettings settings = Parse("100 120 combat 150 chat", out int seconds);
			LogAssert.AreEqual(120, seconds);
			LogAssert.AreEqual(Activity.Combat, settings.Activity);
			LogAssert.AreEqual(150f, settings.Radius);
			LogAssert.IsTrue(settings.Chat);

			settings = Parse("100 chat 0 FIGHT 300", out seconds);
			LogAssert.AreEqual(0, seconds, "0 is untimed, so a radius can follow without a run");
			LogAssert.AreEqual(Activity.Combat, settings.Activity);
			LogAssert.AreEqual(300f, settings.Radius);
		}

		[Test]
		public void OutOfRange_OrUnknown_IsRefused()
		{
			AssertRefused("50 5000");
			AssertRefused("50 -1");
			AssertRefused("50 60 2");
			AssertRefused("50 60 5000");
			AssertRefused("50 60 100 7");
			AssertRefused("50 dance");
		}
	}
}
