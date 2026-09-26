using System;

namespace FishMMO.Auth.Core
{
	/// <summary>
	/// What a connection that has completed its handshake, and not yet authenticated, is waiting on.
	/// </summary>
	/// <remarks>
	/// The two need different limits. Machine work either makes progress within seconds or has
	/// stalled; a person reading a code off their phone makes no progress the server can see until
	/// the code arrives. One progress timer used to cover both, so the two-factor prompt had the
	/// fifteen seconds meant for a stalled SRP exchange, and a player still reaching for their
	/// authenticator app was disconnected.
	/// </remarks>
	public enum PendingAuthPhase : byte
	{
		/// <summary>
		/// The servers are working: SRP verify and proof, a token check, a two-factor code being
		/// verified. Bounded by <see cref="PendingAuthRules.ProgressTtlSeconds"/> since the last
		/// progress and by <see cref="PendingAuthRules.AuthenticatingCapSeconds"/> of extensions.
		/// </summary>
		Authenticating = 0,

		/// <summary>
		/// The two-factor prompt is on the player's screen. Bounded by one fixed window per prompt;
		/// nothing the server does in the meantime extends it.
		/// </summary>
		AwaitingTwoFactor = 1,
	}

	/// <summary>
	/// The time limits on a pending authentication: the truth table behind the authenticator's
	/// stale sweep, as pure functions of monotonic seconds.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A connection's life before authentication is covered by exactly one limit at a time. From
	/// connect until the handshake completes, the host's handshake timeout applies (the game's
	/// <c>BaseServerAuthenticator.authHandshakeTimeoutSeconds</c>). The handshake completing is
	/// what starts pending-authentication tracking, and from then on these rules apply until the
	/// connection authenticates, is refused, or disconnects.
	/// </para>
	/// <para>
	/// <b>Authenticating</b> is bounded by progress: overdue <see cref="ProgressTtlSeconds"/> after
	/// the last progress report, and progress stops extending it once the phase has run for
	/// <see cref="AuthenticatingCapSeconds"/>, so a stall mid-SRP is dropped within
	/// cap + TTL at most, however it is paced.
	/// </para>
	/// <para>
	/// <b>AwaitingTwoFactor</b> is bounded by the window alone: overdue a fixed window after the
	/// prompt. Each prompt gets the full window — the first, and each re-prompt after a counted
	/// wrong code — and the per-connection attempt cap bounds how many prompts one sign-in can
	/// have, so the phase can never be extended without spending an attempt. A code arriving
	/// moves the connection back to Authenticating for its verification.
	/// </para>
	/// </remarks>
	public static class PendingAuthRules
	{
		/// <summary>
		/// Seconds an authenticating connection may go without progress before it is dropped.
		/// </summary>
		public const double ProgressTtlSeconds = 15;

		/// <summary>
		/// Seconds into one authenticating phase after which progress no longer extends it.
		/// </summary>
		public const double AuthenticatingCapSeconds = 60;

		/// <summary>
		/// Default seconds a player has to answer one two-factor prompt.
		/// </summary>
		/// <remarks>
		/// Two minutes. Answering takes unlocking a phone, opening the authenticator app, finding
		/// the account and typing six digits — commonly 15 to 40 seconds, which is exactly why the
		/// fifteen seconds the prompt used to inherit failed people. A recovery code written on
		/// paper takes longer to find. Two minutes covers both with room to spare and spans four
		/// 30-second TOTP steps, so a code that rolls over while it is being typed still leaves a
		/// fresh one to read; beyond that the slot is being held for someone who has walked away.
		/// </remarks>
		public const double DefaultTwoFactorWindowSeconds = 120;

		/// <summary>Shortest configurable two-factor window: one TOTP step.</summary>
		public const double MinTwoFactorWindowSeconds = 30;

		/// <summary>Longest configurable two-factor window.</summary>
		public const double MaxTwoFactorWindowSeconds = 600;

		/// <summary>Most requests one SRP channel, verify or proof, may be configured to hold.</summary>
		public const int MaxSrpChannelCapacity = 10000;

		/// <summary>
		/// A configured SRP channel capacity brought into [1, <see cref="MaxSrpChannelCapacity"/>], as
		/// the Login Server builds the channel.
		/// </summary>
		/// <param name="capacity">The configured capacity.</param>
		/// <returns>The capacity the channel is built with.</returns>
		public static int ClampSrpChannelCapacity(int capacity) => Math.Max(1, Math.Min(capacity, MaxSrpChannelCapacity));

		/// <summary>
		/// The Login Server's pending-authentication cap when none is configured: as many connections
		/// as its SRP verify and proof channels hold between them — 1,000 with the default 500 + 500.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The cap is where the login queue engages, so it should sit where a queue position starts to
		/// serve a player better than being let in. A connection counted here is waiting on the client
		/// (between the handshake and its verify request, or between the verify response and its
		/// proof), on a person (the two-factor prompt), or in one of the two SRP channels — and the
		/// channels are where a storm piles up. Each drops a request it has no room for and the player
		/// is answered ServerBusy: refused, told to try again, place lost. So letting in more
		/// connections than the channels can hold together buys nothing but the chance of that
		/// refusal, while held back in the queue the same connections keep their place and are told
		/// it. Past verify + proof capacity, the queue is the better answer.
		/// </para>
		/// <para>
		/// The old default of 10,000 meant the queue never engaged. Pending connections number about
		/// their arrival rate times how long they stay, and the global handshake limit (500 a second)
		/// and a residence of a second or two keep that far below 10,000; it was reachable only by a
		/// stalled pipeline, which the channels had already been answering with ServerBusy.
		/// </para>
		/// <para>
		/// Derived rather than a constant, so an operator who enlarges the channels moves the queue
		/// threshold with them. A player at the two-factor prompt occupies no channel and does not
		/// count against it; prompts have their own, looser ceiling (see <see cref="AdmitsNewPending"/>).
		/// </para>
		/// </remarks>
		/// <param name="verifyChannelCapacity">The configured SRP verify channel capacity.</param>
		/// <param name="proofChannelCapacity">The configured SRP proof channel capacity.</param>
		/// <returns>The default cap.</returns>
		public static int DefaultLoginPendingCap(int verifyChannelCapacity, int proofChannelCapacity) =>
			ClampSrpChannelCapacity(verifyChannelCapacity) + ClampSrpChannelCapacity(proofChannelCapacity);

		/// <summary>
		/// How many times the pending cap the whole set, two-factor prompts included, may reach.
		/// </summary>
		public const int PendingCeilingMultiplier = 10;

		/// <summary>
		/// Whether a connection that has just completed its handshake may start authenticating.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The cap counts machine work only: connections authenticating, which are what fill the SRP
		/// channels the cap is sized from. A player at the two-factor prompt has already proven their
		/// password and is waiting on themselves, not on the pipeline. Counted against the cap, a
		/// launch where many players type codes filled it and turned new sign-ins into the queue
		/// while the channels sat idle.
		/// </para>
		/// <para>
		/// Prompts still cost memory, so the whole set has a ceiling of
		/// <see cref="PendingCeilingMultiplier"/> times the cap — at the Login Server's default 1,000,
		/// the 10,000 the cap used to be. Reaching it needs that many correct passwords at the prompt
		/// at once, each of which leaves after one window or its attempts.
		/// </para>
		/// </remarks>
		/// <param name="authenticating">Connections pending in <see cref="PendingAuthPhase.Authenticating"/>.</param>
		/// <param name="total">All pending connections, prompts included.</param>
		/// <param name="capacity">The pending cap.</param>
		/// <returns>True when both the cap and the ceiling have room.</returns>
		public static bool AdmitsNewPending(int authenticating, int total, int capacity)
		{
			long ceiling = (long)capacity * PendingCeilingMultiplier;
			return authenticating < capacity && total < ceiling;
		}

		/// <summary>
		/// Whether a pending authentication has run out of time.
		/// </summary>
		/// <param name="phase">What it is waiting on.</param>
		/// <param name="phaseStartedSeconds">When the current phase began: the prompt, for <see cref="PendingAuthPhase.AwaitingTwoFactor"/>.</param>
		/// <param name="lastProgressSeconds">The last progress report (or the phase start).</param>
		/// <param name="nowSeconds">The current time.</param>
		/// <param name="progressTtlSeconds">See <see cref="ProgressTtlSeconds"/>.</param>
		/// <param name="twoFactorWindowSeconds">The two-factor window.</param>
		/// <returns><c>true</c> when the connection must be dropped.</returns>
		public static bool IsOverdue(PendingAuthPhase phase, double phaseStartedSeconds, double lastProgressSeconds, double nowSeconds,
			double progressTtlSeconds, double twoFactorWindowSeconds)
		{
			return phase == PendingAuthPhase.AwaitingTwoFactor
				? nowSeconds - phaseStartedSeconds >= twoFactorWindowSeconds
				: nowSeconds - lastProgressSeconds >= progressTtlSeconds;
		}

		/// <summary>
		/// Whether a progress report may extend a pending authentication.
		/// </summary>
		/// <remarks>
		/// Only machine work reports progress. A report that lands while the player is being
		/// prompted — a verification of an earlier code finishing late — must not pull the
		/// connection off its window.
		/// </remarks>
		/// <param name="phase">What it is waiting on.</param>
		/// <param name="phaseStartedSeconds">When the current phase began.</param>
		/// <param name="nowSeconds">The current time.</param>
		/// <param name="authenticatingCapSeconds">See <see cref="AuthenticatingCapSeconds"/>.</param>
		/// <returns><c>true</c> when the report counts.</returns>
		public static bool MayExtend(PendingAuthPhase phase, double phaseStartedSeconds, double nowSeconds, double authenticatingCapSeconds)
		{
			return phase == PendingAuthPhase.Authenticating &&
				nowSeconds - phaseStartedSeconds < authenticatingCapSeconds;
		}

		/// <summary>
		/// A configured two-factor window brought into
		/// [<see cref="MinTwoFactorWindowSeconds"/>, <see cref="MaxTwoFactorWindowSeconds"/>].
		/// </summary>
		/// <remarks>
		/// There is no "off": a window shorter than a person can use is the defect this exists to
		/// fix, and an unbounded one lets an abandoned prompt hold a pending slot forever. NaN, which
		/// no comparison clamps, reads as the default.
		/// </remarks>
		/// <param name="seconds">The configured value.</param>
		/// <returns>The value the authenticator uses.</returns>
		public static double ClampTwoFactorWindow(double seconds)
		{
			if (double.IsNaN(seconds))
			{
				return DefaultTwoFactorWindowSeconds;
			}
			return Math.Max(MinTwoFactorWindowSeconds, Math.Min(MaxTwoFactorWindowSeconds, seconds));
		}
	}
}
