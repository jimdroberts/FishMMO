using System;
using FishMMO.Shared;

namespace FishMMO.Client
{
	/// <summary>
	/// Which of the connection pipeline's two queues <see cref="UITKWorldQueueDisplay"/> is
	/// showing.
	/// </summary>
	public enum QueueKind
	{
		/// <summary>The world server's scene-routing queue: waiting for a place in the world.</summary>
		World = 0,

		/// <summary>The login server's admission queue: waiting for a turn to authenticate.</summary>
		Login = 1,
	}

	/// <summary>
	/// What <see cref="UITKWorldQueueDisplay"/> says about a wait in either queue — the world
	/// server's scene routing or the login server's admission — as pure functions.
	/// </summary>
	/// <remarks>
	/// The panel only moves these strings and numbers into its elements. Keeping the wording and
	/// the arithmetic here lets a test pin every queue, every reason, every estimate and every
	/// edge of the progress bar without mounting a panel or a network.
	/// </remarks>
	public static class WorldQueuePresentation
	{
		/// <summary>Header title while the client is waiting in the world queue.</summary>
		public const string WaitingTitle = "ENTERING THE WORLD";

		/// <summary>Header title once the world server has given up on the wait.</summary>
		public const string EndedTitle = "COULD NOT ENTER THE WORLD";

		/// <summary>Header title while the client is waiting in the login queue.</summary>
		/// <remarks>
		/// "Connecting" rather than "signing in": the login server queues any handshake over its
		/// capacity, and creating an account handshakes too.
		/// </remarks>
		public const string LoginWaitingTitle = "CONNECTING";

		/// <summary>Header title once the login server has given up on the wait.</summary>
		public const string LoginEndedTitle = "COULD NOT CONNECT";

		/// <summary>What the client is waiting for in the login queue.</summary>
		public const string LoginHeadline = "The login server is busy right now.";

		/// <summary>The line under <see cref="LoginHeadline"/>.</summary>
		/// <remarks>
		/// The login server admits from the front of its line at a fixed rate, so "a few at a
		/// time" is the honest description; the estimate beside it is that rate applied.
		/// </remarks>
		public const string LoginDetail = "You are in line to connect. Players are let in a few at a time, in the order they arrived.";

		/// <summary>The headline once the login server has given up on the wait.</summary>
		public const string LoginEndedHeadline = "Your wait for the login server ended.";

		/// <summary>
		/// What the player can do about it. The login server does not say which of its two reasons
		/// ended the wait, so this names both rather than guessing.
		/// </summary>
		/// <remarks>
		/// Unlike the world queue, the login queue keeps nothing across connections: a new
		/// attempt is a new arrival, so this one does say "from the back".
		/// </remarks>
		public const string LoginEndedDetail = "The login server stopped holding your place: the wait ran too long, or the server is restarting. Sign in again to rejoin the queue from the back.";

		/// <summary>The panel's title for a queue, waiting or ended.</summary>
		public static string Title(QueueKind kind, bool ended)
		{
			return kind == QueueKind.Login
				? (ended ? LoginEndedTitle : LoginWaitingTitle)
				: (ended ? EndedTitle : WaitingTitle);
		}

		/// <summary>The headline for a queue. The login queue has one wait, so the reason is the world's alone.</summary>
		public static string Headline(QueueKind kind, WorldSceneQueueReason reason)
		{
			return kind == QueueKind.Login ? LoginHeadline : Headline(reason);
		}

		/// <summary>The line under <see cref="Headline(QueueKind, WorldSceneQueueReason)"/>.</summary>
		public static string Detail(QueueKind kind, WorldSceneQueueReason reason)
		{
			return kind == QueueKind.Login ? LoginDetail : Detail(reason);
		}

		/// <summary>The headline once a queue's wait has been abandoned.</summary>
		public static string EndedHeadline(QueueKind kind, WorldSceneQueueReason reason)
		{
			return kind == QueueKind.Login ? LoginEndedHeadline : EndedHeadline(reason);
		}

		/// <summary>What the player can do once a queue's wait has been abandoned.</summary>
		public static string EndedDetail(QueueKind kind, WorldSceneQueueReason reason)
		{
			return kind == QueueKind.Login ? LoginEndedDetail : EndedDetail(reason);
		}

		/// <summary>
		/// The small print under a live wait: what leaving costs.
		/// </summary>
		/// <remarks>
		/// Leaving the world queue gives the place up for good, where a dropped connection keeps
		/// it for a while; that difference is worth one line. The login queue keeps nothing, so
		/// its note only says where leaving goes.
		/// </remarks>
		public static string Note(QueueKind kind)
		{
			return kind == QueueKind.Login
				? "Leaving the queue returns you to the login screen."
				: "Leaving the queue gives up your place and returns you to the login screen.";
		}

		/// <summary>
		/// Whether an ended wait offers "Try again". Only the world queue can be rejoined from
		/// the panel: the session is still held. Rejoining the login queue means signing in again.
		/// </summary>
		public static bool OffersRetry(QueueKind kind) => kind == QueueKind.World;

		/// <summary>
		/// The way-out button once a wait has ended. The world queue's goes back to the login
		/// screen; the login queue's ended wait is already there, and the button only closes it.
		/// </summary>
		public static string EndedLeaveLabel(QueueKind kind)
		{
			return kind == QueueKind.Login ? "Close" : "Return to login";
		}

		/// <summary>Shown in place of an estimate the server could not make.</summary>
		/// <remarks>
		/// The server sends 0 when the queue is not draining at all, so any number here, even
		/// "0s", would be a promise nobody is keeping.
		/// </remarks>
		public const string UnknownEstimate = "Unknown";

		/// <summary>
		/// The one-line statement of what the client is waiting for.
		/// </summary>
		/// <remarks>
		/// The three waits look the same from outside and mean different things: the world being
		/// full, a zone starting up, and the player's own body still standing in a fight they
		/// disconnected from. Only the last is self-inflicted, and a player who is not told about
		/// it cannot understand why they wait while the world is visibly not busy.
		/// </remarks>
		public static string Headline(WorldSceneQueueReason reason)
		{
			switch (reason)
			{
				case WorldSceneQueueReason.SceneLoading:
					return "Preparing your zone.";
				case WorldSceneQueueReason.CombatLogoutBody:
					return "Your character is still in combat where you left it.";
				case WorldSceneQueueReason.Capacity:
				default:
					return "The world is full right now.";
			}
		}

		/// <summary>
		/// The line under <see cref="Headline"/>: what happens next, and why the order is fair.
		/// </summary>
		public static string Detail(WorldSceneQueueReason reason)
		{
			switch (reason)
			{
				case WorldSceneQueueReason.SceneLoading:
					return "A new copy of your zone is starting on a server. You will be sent in as soon as it is ready.";
				case WorldSceneQueueReason.CombatLogoutBody:
					return "Only the zone holding your character can hand it back. You will be sent there once it is released.";
				case WorldSceneQueueReason.Capacity:
				default:
					return "You are in line for a place. Places are given in the order players arrived, as they open.";
			}
		}

		/// <summary>
		/// "Position of total", or the position alone when the total says nothing more.
		/// </summary>
		/// <param name="position">1-based position. Values below 1 read as 1.</param>
		/// <param name="totalQueued">Size of the group being waited in.</param>
		public static string PositionText(int position, int totalQueued)
		{
			int shown = Math.Max(1, position);
			return totalQueued >= shown ? $"{shown} of {totalQueued}" : shown.ToString();
		}

		/// <summary>
		/// The server's estimate, rounded the way a person would say it, or
		/// <see cref="UnknownEstimate"/>.
		/// </summary>
		/// <param name="estimatedWaitSeconds">Server estimate; 0 or less means it has none.</param>
		public static string EstimateText(int estimatedWaitSeconds)
		{
			if (estimatedWaitSeconds <= 0)
			{
				return UnknownEstimate;
			}
			if (estimatedWaitSeconds < 60)
			{
				// Seconds are a cycle-sized guess; say "under a minute" at the resolution it has.
				return estimatedWaitSeconds <= 10 ? "A few seconds" : $"About {RoundUp(estimatedWaitSeconds, 5)}s";
			}
			int minutes = (estimatedWaitSeconds + 59) / 60;
			return minutes == 1 ? "About 1 minute" : $"About {minutes} minutes";
		}

		/// <summary>
		/// Elapsed wait as m:ss (or h:mm:ss past an hour).
		/// </summary>
		/// <param name="elapsedSeconds">Seconds waited. Negative and NaN read as 0.</param>
		public static string ElapsedText(double elapsedSeconds)
		{
			long total = elapsedSeconds > 0.0 ? (long)elapsedSeconds : 0L;
			long hours = total / 3600;
			long minutes = (total / 60) % 60;
			long seconds = total % 60;
			return hours > 0
				? $"{hours}:{minutes:00}:{seconds:00}"
				: $"{minutes}:{seconds:00}";
		}

		/// <summary>
		/// How much of the line that was ahead of this client when it started waiting has
		/// cleared, from 0 to 1.
		/// </summary>
		/// <param name="startPosition">The highest position this wait has reported.</param>
		/// <param name="position">The position just reported.</param>
		/// <remarks>
		/// Measured against the start of this wait rather than against the whole queue: the queue
		/// behind the player keeps growing, and a bar that shrank as people joined after them
		/// would read as going backwards while they moved forwards. At the front of the line the
		/// bar is full, including a wait that started there.
		/// </remarks>
		public static float Progress(int startPosition, int position)
		{
			int start = Math.Max(1, Math.Max(startPosition, position));
			int current = Math.Max(1, position);
			if (start <= 1)
			{
				return 1f;
			}
			float cleared = (start - current) / (float)(start - 1);
			return cleared < 0f ? 0f : (cleared > 1f ? 1f : cleared);
		}

		/// <summary>
		/// The headline once the world server has abandoned the wait, naming which wait failed.
		/// </summary>
		public static string EndedHeadline(WorldSceneQueueReason reason)
		{
			switch (reason)
			{
				case WorldSceneQueueReason.SceneLoading:
					return "Your zone did not start in time.";
				case WorldSceneQueueReason.CombatLogoutBody:
					return "Your character was not released in time.";
				case WorldSceneQueueReason.Capacity:
				default:
					return "No place opened in time.";
			}
		}

		/// <summary>
		/// What the player can do about it.
		/// </summary>
		/// <remarks>
		/// A full world's ended wait says plainly that trying again soon keeps the place. The world
		/// server purges a capacity wait only when the line has stopped moving, and it holds the
		/// account's place for a grace window after the purge, so "Try again" resumes it. It used
		/// to say the retry queued from the back, which was true until the place was held.
		/// </remarks>
		public static string EndedDetail(WorldSceneQueueReason reason)
		{
			switch (reason)
			{
				case WorldSceneQueueReason.SceneLoading:
					return "The server could not bring your zone up. Try again, or return to the login screen and come back later.";
				case WorldSceneQueueReason.CombatLogoutBody:
					return "The zone holding your character did not hand it back. Try again to be placed normally, or return to the login screen.";
				case WorldSceneQueueReason.Capacity:
				default:
					return "The world stayed full and the line stopped moving. Try again soon to take your place back in line, or return to the login screen.";
			}
		}

		/// <summary>Rounds a positive value up to a multiple of <paramref name="step"/>.</summary>
		private static int RoundUp(int value, int step)
		{
			return ((value + step - 1) / step) * step;
		}
	}
}
