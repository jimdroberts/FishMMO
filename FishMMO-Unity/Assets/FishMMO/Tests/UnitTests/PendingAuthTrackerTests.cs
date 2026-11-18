using System.Collections.Generic;
using NUnit.Framework;
using FishMMO.Auth.Core;
using FishMMO.Auth.Core.Collections;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// <see cref="PendingAuthRules"/> and <see cref="PendingAuthTracker{TConnection}"/>: the time
	/// limits on a connection between a completed handshake and authentication.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The defect behind these: a player with two-factor authentication who took more than about
	/// fifteen seconds to type their code was disconnected. Two limits did it, a millisecond or so
	/// apart — the host's handshake timeout, which ran from connect until authentication, and the
	/// core's fifteen-second progress TTL, which was the only limit a pending connection had, so
	/// the prompt inherited the timeout meant for a stalled SRP exchange.
	/// </para>
	/// <para>
	/// Every call takes its (monotonic, seconds) clock from the test, and the durations are the
	/// production ones, so each assertion is about a moment a player would live through.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class PendingAuthTrackerTests
	{
		private const double Ttl = PendingAuthRules.ProgressTtlSeconds;
		private const double Cap = PendingAuthRules.AuthenticatingCapSeconds;
		private const double Window = PendingAuthRules.DefaultTwoFactorWindowSeconds;
		/// <summary>A monotonic reading; only differences matter.</summary>
		private const double T0 = 5000.0;
		private const double Epsilon = 0.001;

		/// <summary>A reference-type connection, so identity means what it means for FishNet's.</summary>
		private sealed class Conn
		{
			public readonly int Id;
			public Conn(int id) { Id = id; }
			public override string ToString() => $"conn {Id}";
		}

		private PendingAuthTracker<Conn> tracker;
		/// <summary>What the last sweep took, as the tracker reports it: each connection with its phase.</summary>
		private readonly List<(Conn Connection, PendingAuthPhase Phase)> sweptWithPhase = new List<(Conn Connection, PendingAuthPhase Phase)>();
		/// <summary>The connections alone, in the order taken.</summary>
		private readonly List<Conn> swept = new List<Conn>();

		[SetUp]
		public void SetUp()
		{
			tracker = new PendingAuthTracker<Conn>(Ttl, Cap, Window);
			sweptWithPhase.Clear();
			swept.Clear();
		}

		private int Sweep(double now, int max = 64) => Sweep(now, out _, max);

		private int Sweep(double now, out int twoFactorExpired, int max = 64)
		{
			sweptWithPhase.Clear();
			swept.Clear();
			int taken = tracker.SweepOverdue(now, max, sweptWithPhase);
			twoFactorExpired = 0;
			foreach ((Conn connection, PendingAuthPhase phase) in sweptWithPhase)
			{
				swept.Add(connection);
				if (phase == PendingAuthPhase.AwaitingTwoFactor)
				{
					twoFactorExpired++;
				}
			}
			LogAssert.AreEqual(taken, sweptWithPhase.Count, "the count returned is the number handed back");
			return taken;
		}

		private Conn Start(int id, double now)
		{
			Conn conn = new Conn(id);
			LogAssert.IsTrue(tracker.TryStart(id, conn, now, int.MaxValue), $"fixture: {conn} starts");
			return conn;
		}

		// ── The rules ───────────────────────────────────────────────────────

		[Test]
		public void Rules_Authenticating_IsOverdueOneTtlAfterItsLastProgress_WhateverItsAge()
		{
			LogAssert.IsFalse(PendingAuthRules.IsOverdue(PendingAuthPhase.Authenticating, T0, T0 + 40, T0 + 40 + Ttl - Epsilon, Ttl, Window),
				"in time a millisecond before the TTL runs out");
			LogAssert.IsTrue(PendingAuthRules.IsOverdue(PendingAuthPhase.Authenticating, T0, T0 + 40, T0 + 40 + Ttl, Ttl, Window),
				"overdue exactly one TTL after the last progress");
		}

		[Test]
		public void Rules_AwaitingTwoFactor_IsOverdueOneWindowAfterThePrompt_AndNoSooner()
		{
			LogAssert.IsFalse(PendingAuthRules.IsOverdue(PendingAuthPhase.AwaitingTwoFactor, T0, T0, T0 + Ttl, Ttl, Window),
				"the progress TTL does not apply to a player at the prompt — this is the fifteen-second disconnect");
			LogAssert.IsFalse(PendingAuthRules.IsOverdue(PendingAuthPhase.AwaitingTwoFactor, T0, T0, T0 + Window - Epsilon, Ttl, Window),
				"in time until the window is up");
			LogAssert.IsTrue(PendingAuthRules.IsOverdue(PendingAuthPhase.AwaitingTwoFactor, T0, T0, T0 + Window, Ttl, Window),
				"overdue exactly one window after the prompt");
		}

		[Test]
		public void Rules_OnlyMachineWorkInsideItsCap_MayBeExtended()
		{
			LogAssert.IsTrue(PendingAuthRules.MayExtend(PendingAuthPhase.Authenticating, T0, T0 + Cap - Epsilon, Cap),
				"progress counts until the cap");
			LogAssert.IsFalse(PendingAuthRules.MayExtend(PendingAuthPhase.Authenticating, T0, T0 + Cap, Cap),
				"and not from the cap on, so a stall cannot be paced forever");
			LogAssert.IsFalse(PendingAuthRules.MayExtend(PendingAuthPhase.AwaitingTwoFactor, T0, T0 + 1, Cap),
				"a player at the prompt makes no progress the server can extend");
		}

		[Test]
		public void Rules_TheWindow_IsClampedToWhatAPersonCanUse()
		{
			LogAssert.AreEqual(120.0, PendingAuthRules.DefaultTwoFactorWindowSeconds, "the default is two minutes");
			LogAssert.AreEqual(PendingAuthRules.MinTwoFactorWindowSeconds, PendingAuthRules.ClampTwoFactorWindow(5), "shorter than one TOTP step is raised");
			LogAssert.AreEqual(PendingAuthRules.MinTwoFactorWindowSeconds, PendingAuthRules.ClampTwoFactorWindow(-1), "there is no off switch");
			LogAssert.AreEqual(PendingAuthRules.MaxTwoFactorWindowSeconds, PendingAuthRules.ClampTwoFactorWindow(86400), "an abandoned prompt cannot hold a slot for a day");
			LogAssert.AreEqual(90.0, PendingAuthRules.ClampTwoFactorWindow(90), "a value in range is kept");
			LogAssert.AreEqual(PendingAuthRules.DefaultTwoFactorWindowSeconds, PendingAuthRules.ClampTwoFactorWindow(double.NaN), "NaN reads as the default");
			LogAssert.IsTrue(PendingAuthRules.MinTwoFactorWindowSeconds > PendingAuthRules.ProgressTtlSeconds,
				"even the shortest window is longer than the TTL the prompt used to inherit");
		}

		// ── Authenticating ──────────────────────────────────────────────────

		[Test]
		public void AStalledExchange_IsStillDroppedOneTtlAfterItsLastProgress()
		{
			Conn conn = Start(1, T0);
			LogAssert.IsTrue(tracker.ReportProgress(1, T0 + 5), "the verify step reports progress");

			LogAssert.AreEqual(0, Sweep(T0 + 5 + Ttl - Epsilon), "in time just before the TTL");
			LogAssert.AreEqual(1, Sweep(T0 + 5 + Ttl), "dropped at the TTL: a stall mid-SRP is still bounded");
			LogAssert.IsTrue(ReferenceEquals(conn, swept[0]), "and the connection is handed back to be purged");
			LogAssert.AreEqual(0, tracker.Count, "it leaves the tracker");
		}

		[Test]
		public void Progress_StopsExtendingAtTheCap_SoAPacedStallIsBoundedByCapPlusTtl()
		{
			Start(1, T0);
			for (double t = T0 + 10; t < T0 + Cap; t += 10)
			{
				LogAssert.IsTrue(tracker.ReportProgress(1, t), $"progress at +{t - T0}s counts");
			}
			LogAssert.IsFalse(tracker.ReportProgress(1, T0 + Cap), "progress at the cap does not");

			LogAssert.AreEqual(0, Sweep(T0 + 50 + Ttl - Epsilon), "the last counted progress (+50s) still holds");
			LogAssert.AreEqual(1, Sweep(T0 + 50 + Ttl), "and then the connection is dropped");
		}

		// ── The two-factor prompt ───────────────────────────────────────────

		[Test]
		public void ATwoFactorPrompt_GetsTheWholeWindow_NotTheFifteenSecondsItUsedToInherit()
		{
			Conn conn = Start(1, T0);
			LogAssert.IsTrue(tracker.ReportProgress(1, T0 + 0.4), "the proof worker's last progress");
			LogAssert.IsTrue(tracker.BeginAwaitingTwoFactor(1, conn, T0 + 0.5), "TwoFactorRequired puts the connection on its window");

			LogAssert.AreEqual(0, Sweep(T0 + 0.4 + Ttl + 5), "twenty seconds in, reaching for the phone, still connected");
			LogAssert.AreEqual(0, Sweep(T0 + 0.5 + Window - Epsilon), "and until the window is up");
			LogAssert.AreEqual(1, Sweep(T0 + 0.5 + Window, out int expired), "an unanswered prompt is dropped when it is");
			LogAssert.AreEqual(1, expired, "and reported as an expired prompt, not a stall");
		}

		[Test]
		public void ProgressReports_DoNotExtendAPrompt()
		{
			Conn conn = Start(1, T0);
			tracker.BeginAwaitingTwoFactor(1, conn, T0);

			LogAssert.IsFalse(tracker.ReportProgress(1, T0 + 100), "a late verification finishing must not take the connection off its window");
			LogAssert.IsTrue(tracker.TryGetPhase(1, conn, out PendingAuthPhase phase) && phase == PendingAuthPhase.AwaitingTwoFactor, "it is still at the prompt");
			LogAssert.AreEqual(1, Sweep(T0 + Window), "and the window ends when it always would have");
		}

		[Test]
		public void ACodeArriving_IsMachineWorkAgain_AndARepromptGetsAFreshWindow()
		{
			Conn conn = Start(1, T0);
			tracker.BeginAwaitingTwoFactor(1, conn, T0);

			LogAssert.IsTrue(tracker.ResumeAuthenticating(1, conn, T0 + 110), "a code arrives 110 s in");
			LogAssert.IsTrue(tracker.TryGetPhase(1, conn, out PendingAuthPhase phase) && phase == PendingAuthPhase.Authenticating, "checking it is machine work");
			LogAssert.IsTrue(tracker.ReportProgress(1, T0 + 111), "the verification reports progress like any other");
			LogAssert.AreEqual(0, Sweep(T0 + Window + 1), "the prompt's window no longer applies");
			LogAssert.AreEqual(1, Sweep(T0 + 111 + Ttl, out int expired), "the check itself is bounded by the TTL");
			LogAssert.AreEqual(0, expired, "as a stall, not as an expired prompt");

			Conn again = Start(2, T0 + 200);
			tracker.BeginAwaitingTwoFactor(2, again, T0 + 200);
			tracker.ResumeAuthenticating(2, again, T0 + 210);
			LogAssert.IsTrue(tracker.BeginAwaitingTwoFactor(2, again, T0 + 212), "the code was wrong: re-prompt");
			LogAssert.AreEqual(0, Sweep(T0 + 210 + Ttl), "the re-prompt is on a window, not the TTL");
			LogAssert.AreEqual(0, Sweep(T0 + 212 + Window - Epsilon), "and it is a whole window, not what was left of the first");
			LogAssert.AreEqual(1, Sweep(T0 + 212 + Window), "which ends like the first");
		}

		[Test]
		public void ChangingTheWindow_MovesEveryPromptTogether()
		{
			Conn a = Start(1, T0);
			Conn b = Start(2, T0);
			tracker.BeginAwaitingTwoFactor(1, a, T0);
			tracker.BeginAwaitingTwoFactor(2, b, T0 + 10);

			tracker.TwoFactorWindowSeconds = 60;
			LogAssert.AreEqual(1, Sweep(T0 + 60), "a prompt already on screen takes the new window");
			LogAssert.IsTrue(ReferenceEquals(a, swept[0]), "oldest first");
			LogAssert.AreEqual(1, Sweep(T0 + 70), "and the next is still in order behind it");
		}

		// ── The sweep ───────────────────────────────────────────────────────

		[Test]
		public void TheSweep_ReadsEachPhasesHead_SoAPromptDoesNotHoldBackAStall_OrTheReverse()
		{
			Conn prompt = Start(1, T0);
			tracker.BeginAwaitingTwoFactor(1, prompt, T0);   // due at T0 + 120
			Conn stall = Start(2, T0 + 1);                    // due at T0 + 16

			LogAssert.AreEqual(1, Sweep(T0 + 20), "the stall behind an earlier, still-open prompt is dropped on time");
			LogAssert.IsTrue(ReferenceEquals(stall, swept[0]), "and it is the stall");

			Conn live = Start(3, T0 + 110);                    // due at T0 + 125
			LogAssert.AreEqual(1, Sweep(T0 + Window), "the prompt is dropped at its window although a live exchange heads the other list");
			LogAssert.IsTrue(ReferenceEquals(prompt, swept[0]), "and it is the prompt");
			LogAssert.IsTrue(tracker.TryGetPhase(3, live, out _), "the live exchange is untouched");
		}

		[Test]
		public void TheSweep_IsBoundedPerCall_AndTheRestWaitAtTheHead()
		{
			for (int id = 1; id <= 5; id++)
			{
				Start(id, T0 + id * 0.1);
			}

			LogAssert.AreEqual(2, Sweep(T0 + 60, max: 2), "at most two per call");
			LogAssert.AreEqual(1, swept[0].Id, "oldest first");
			LogAssert.AreEqual(2, swept[1].Id, "then the next");
			LogAssert.AreEqual(2, Sweep(T0 + 60, max: 2), "the rest are still at the head");
			LogAssert.AreEqual(1, Sweep(T0 + 60, max: 2), "until none are left");
			LogAssert.AreEqual(0, Sweep(T0 + 60, max: 2), "and a sweep with nothing due takes nothing");
		}

		[Test]
		public void Stamps_NeverGoBackwards_SoALateLockHolderCannotReorderTheList()
		{
			Conn a = Start(1, T0 + 10);
			Conn b = Start(2, T0 + 5);   // read the clock first, reached the lock second

			LogAssert.AreEqual(0, Sweep(T0 + 5 + Ttl), "the second is stamped no earlier than the first, so neither is due yet");
			LogAssert.AreEqual(2, Sweep(T0 + 10 + Ttl), "both fall due together");
			LogAssert.IsTrue(ReferenceEquals(a, swept[0]) && ReferenceEquals(b, swept[1]), "in the order they were stamped");
		}

		/// <summary>
		/// The sweep hands back the phase each connection ran out of time in, because the two are
		/// answered differently: a stall is dropped without a word, an unanswered two-factor prompt
		/// is told TwoFactorExpired first.
		/// </summary>
		[Test]
		public void TheSweep_ReportsThePhaseEachConnectionRanOutIn()
		{
			Conn stall = Start(1, T0);
			Conn prompt = Start(2, T0);
			tracker.BeginAwaitingTwoFactor(2, prompt, T0);

			LogAssert.AreEqual(2, Sweep(T0 + Window), "both are overdue by the end of the window");
			LogAssert.AreEqual(2, sweptWithPhase.Count, "and both are handed back");
			LogAssert.IsTrue(ReferenceEquals(stall, sweptWithPhase[0].Connection) && sweptWithPhase[0].Phase == PendingAuthPhase.Authenticating,
				"the stall, as machine work");
			LogAssert.IsTrue(ReferenceEquals(prompt, sweptWithPhase[1].Connection) && sweptWithPhase[1].Phase == PendingAuthPhase.AwaitingTwoFactor,
				"the prompt, as a prompt the player is owed an answer about");
		}

		// ── Slots and identity ──────────────────────────────────────────────

		[Test]
		public void TheCap_CountsMachineWorkOnly_PromptsHaveACeiling_AndARestartKeepsItsSlot()
		{
			Conn a = new Conn(1);
			Conn b = new Conn(2);
			LogAssert.IsTrue(tracker.TryStart(1, a, T0, 2), "one of two");
			LogAssert.IsTrue(tracker.TryStart(2, b, T0, 2), "two of two");
			LogAssert.IsFalse(tracker.TryStart(3, new Conn(3), T0, 2), "two authenticating fill a cap of two");

			tracker.BeginAwaitingTwoFactor(1, a, T0);
			LogAssert.IsTrue(tracker.TryStart(3, new Conn(3), T0, 2), "a player at the prompt frees their slot for machine work");

			Conn b2 = new Conn(2);
			LogAssert.IsTrue(tracker.TryStart(2, b2, T0 + 1, 2), "a fresh handshake on a tracked ID restarts it without a new slot");
			LogAssert.IsTrue(tracker.TryGetPhase(2, b2, out PendingAuthPhase phase) && phase == PendingAuthPhase.Authenticating, "from the beginning");
			LogAssert.AreEqual(3, tracker.Count, "three pending: one prompt, two authenticating");

			// Prompts are bounded too: the whole set stops at PendingCeilingMultiplier times the cap.
			var ceilingTracker = new PendingAuthTracker<Conn>(Ttl, Cap, Window);
			int ceiling = 1 * PendingAuthRules.PendingCeilingMultiplier;
			for (int id = 1; id <= ceiling; id++)
			{
				Conn c = new Conn(id);
				LogAssert.IsTrue(ceilingTracker.TryStart(id, c, T0, 1), $"connection {id} starts while under the ceiling");
				ceilingTracker.BeginAwaitingTwoFactor(id, c, T0);
			}
			LogAssert.IsFalse(ceilingTracker.TryStart(ceiling + 1, new Conn(ceiling + 1), T0, 1), "the ceiling refuses even with no machine work pending");
		}

		[Test]
		public void Rules_AdmitsNewPending_IsTheCapOnMachineWorkAndTheCeilingOnAll()
		{
			LogAssert.IsTrue(PendingAuthRules.AdmitsNewPending(0, 0, 1000), "empty");
			LogAssert.IsFalse(PendingAuthRules.AdmitsNewPending(1000, 1000, 1000), "cap reached by machine work");
			LogAssert.IsTrue(PendingAuthRules.AdmitsNewPending(999, 5000, 1000), "prompts over the cap do not refuse");
			LogAssert.IsFalse(PendingAuthRules.AdmitsNewPending(0, 10000, 1000), "the ceiling does");
			LogAssert.IsTrue(PendingAuthRules.AdmitsNewPending(0, 0, int.MaxValue), "an unbounded cap does not overflow");
		}

		[Test]
		public void AConnectionSpecificCall_NeverTouchesTheSuccessorOnARecycledId()
		{
			Conn old = new Conn(7);
			Conn successor = new Conn(7);
			tracker.TryStart(7, successor, T0, int.MaxValue);

			LogAssert.IsFalse(tracker.BeginAwaitingTwoFactor(7, old, T0 + 1), "a late prompt for the old connection does nothing");
			LogAssert.IsFalse(tracker.ResumeAuthenticating(7, old, T0 + 1), "nor does a late code");
			LogAssert.IsFalse(tracker.TryGetPhase(7, old, out _), "the old connection is not pending");
			LogAssert.IsFalse(tracker.Remove(7, old), "and cannot end its successor's tracking");
			LogAssert.IsTrue(tracker.TryGetPhase(7, successor, out _), "the successor is untouched");
			LogAssert.IsTrue(tracker.Remove(7), "an ID-wide removal (the transport's stop) still works");
			LogAssert.AreEqual(0, tracker.Count, "and empties it");
		}

		[Test]
		public void AnUntrackedConnection_IsNeitherPromptedNorResumed()
		{
			Conn gone = new Conn(4);
			LogAssert.IsFalse(tracker.BeginAwaitingTwoFactor(4, gone, T0), "a connection already dropped is not put back");
			LogAssert.IsFalse(tracker.ResumeAuthenticating(4, gone, T0), "by either transition");
			LogAssert.IsFalse(tracker.ReportProgress(4, T0), "or by progress");
			LogAssert.AreEqual(0, tracker.Count, "nothing was created");
		}
	}
}
