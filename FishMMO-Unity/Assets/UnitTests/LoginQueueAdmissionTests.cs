using NUnit.Framework;
using FishMMO.Server.Implementation.LoginServer;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// <see cref="LoginQueueSystem.AccrueAdmissionCredit"/>: the rule that decides how many
	/// queued clients are admitted each frame.
	/// </summary>
	[TestFixture]
	public class LoginQueueAdmissionTests
	{
		private const float Frame60 = 1f / 60f;

		/// <summary>Admissions a sequence of frames pays for, spending each whole credit as it accrues.</summary>
		private static int AdmitOver(int frames, float deltaTime, float rate, ref float credit)
		{
			int admitted = 0;
			for (int i = 0; i < frames; i++)
			{
				credit = LoginQueueSystem.AccrueAdmissionCredit(credit, deltaTime, rate, queueEmpty: false);
				while (credit >= 1f)
				{
					credit -= 1f;
					admitted++;
				}
			}
			return admitted;
		}

		[Test]
		public void ARateAboveTheFrameRate_IsHonoured()
		{
			// The countdown this replaced admitted one client per frame at most, so 100/s at
			// 60 fps silently became 60/s.
			float credit = 0f;
			int admitted = AdmitOver(60, Frame60, 100f, ref credit);
			LogAssert.IsTrue(admitted >= 99 && admitted <= 100, $"one second at 100/s admits ~100, not 60 (admitted {admitted})");
		}

		[Test]
		public void ARateBelowTheFrameRate_IsHonoured()
		{
			float credit = 0f;
			int admitted = AdmitOver(600, Frame60, 5f, ref credit);
			LogAssert.IsTrue(admitted >= 49 && admitted <= 50, $"ten seconds at 5/s admits ~50 (admitted {admitted})");
		}

		[Test]
		public void AFrameHitch_MakesUpAtMostOneSecond()
		{
			float credit = LoginQueueSystem.AccrueAdmissionCredit(0f, 10f, 20f, queueEmpty: false);
			LogAssert.AreEqual(20f, credit, "a ten-second frame at 20/s banks one second's worth, not ten");
		}

		[Test]
		public void AnEmptyQueue_RefillsToOneAdmissionAndNoMore()
		{
			float credit = LoginQueueSystem.AccrueAdmissionCredit(0f, 60f, 20f, queueEmpty: true);
			LogAssert.AreEqual(1f, credit, "idle time banks exactly one admission: the first client after idle goes straight through");

			credit = LoginQueueSystem.AccrueAdmissionCredit(15f, Frame60, 20f, queueEmpty: true);
			LogAssert.AreEqual(1f, credit, "credit left when the queue empties does not carry into the next burst");
		}

		[Test]
		public void ASlowRate_NeverCapsBelowOneAdmission()
		{
			float credit = LoginQueueSystem.AccrueAdmissionCredit(0f, 30f, 0.1f, queueEmpty: false);
			LogAssert.AreEqual(1f, credit, "at 0.1/s the cap is still one whole admission, or nobody would ever be admitted");
		}
	}
}
