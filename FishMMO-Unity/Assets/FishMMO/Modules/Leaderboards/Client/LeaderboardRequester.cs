using System;
using FishMMO.Shared;

namespace FishMMO.Client
{
	/// <summary>
	/// Paces one panel's leaderboard requests: at most one outstanding, clicks made while waiting
	/// coalesced into the next request, and a bounded re-ask when a reply never comes.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The server allows one board request in flight per connection and drops, silently, anything
	/// sent inside its debounce or while one is in flight. A panel that sent a request per click
	/// would therefore lose the second click of a quick double "Next" and show "Loading…" for a
	/// page it never asked successfully. So the panel says what it WANTS (<see cref="Want"/>) and
	/// this decides when to send (<see cref="Tick"/>): never while a request is owed a reply, never
	/// faster than <see cref="MinIntervalSeconds"/>, and again after <see cref="RetrySeconds"/> if
	/// the reply was lost — to another panel's request holding the guard, say.
	/// </para>
	/// <para>
	/// Shared by the Leaderboards panel and the arena board's leaderboard view rather than written
	/// into each. Pure: the clock and the send are passed in, so it is tested without a client.
	/// </para>
	/// </remarks>
	public sealed class LeaderboardRequester
	{
		/// <summary>Minimum seconds between sends. Above the server's debounce, so a send is never refused for it.</summary>
		public const float MinIntervalSeconds = 0.25f;

		/// <summary>Seconds to wait for a reply before asking again.</summary>
		public const float RetrySeconds = 3.0f;

		/// <summary>Sends for one wanted page before giving up and reporting the board unavailable.</summary>
		public const int MaxAttempts = 3;

		private bool hasWanted;
		private int wantedTemplateID;
		private int wantedPage;
		private bool answered;
		private int attempts;

		private bool outstanding;
		private int sentTemplateID;
		private int sentPage;
		private float sentAt = float.NegativeInfinity;

		/// <summary>The board the panel wants to show. 0 when none.</summary>
		public int WantedTemplateID => hasWanted ? wantedTemplateID : 0;

		/// <summary>The page the panel wants to show.</summary>
		public int WantedPage => hasWanted ? wantedPage : 0;

		/// <summary>True while the wanted page has not been answered and has not been given up on.</summary>
		public bool Waiting => hasWanted && !answered && !GaveUp;

		/// <summary>True when <see cref="MaxAttempts"/> sends for the wanted page all went unanswered.</summary>
		public bool GaveUp { get; private set; }

		/// <summary>
		/// Asks for a board and page. Nothing is sent until <see cref="Tick"/>. Asking again for
		/// what is already wanted does nothing; use <see cref="Refresh"/> to re-read it.
		/// </summary>
		public void Want(int templateID, int page)
		{
			if (hasWanted && templateID == wantedTemplateID && page == wantedPage && !GaveUp)
			{
				return;
			}
			hasWanted = templateID != 0;
			wantedTemplateID = templateID;
			wantedPage = page;
			answered = false;
			attempts = 0;
			GaveUp = false;
		}

		/// <summary>Asks for the wanted page again, e.g. when the panel is reopened.</summary>
		public void Refresh()
		{
			if (!hasWanted)
			{
				return;
			}
			answered = false;
			attempts = 0;
			GaveUp = false;
		}

		/// <summary>Forgets everything: nothing wanted, nothing owed.</summary>
		public void Clear()
		{
			hasWanted = false;
			wantedTemplateID = 0;
			wantedPage = 0;
			answered = false;
			attempts = 0;
			GaveUp = false;
			outstanding = false;
			sentAt = float.NegativeInfinity;
		}

		/// <summary>
		/// Sends the wanted request if it is time to. Call every frame the panel is showing.
		/// </summary>
		/// <param name="now">Seconds, monotonic (e.g. <c>Time.unscaledTime</c>).</param>
		/// <param name="send">Sends a request for a board and page.</param>
		public void Tick(float now, Action<int, int> send)
		{
			if (!hasWanted || answered || GaveUp || send == null)
			{
				return;
			}
			// A reply is owed: wait for it, unless it has had long enough to be presumed lost.
			if (outstanding && now - sentAt < RetrySeconds)
			{
				return;
			}
			if (now - sentAt < MinIntervalSeconds)
			{
				return;
			}
			if (attempts >= MaxAttempts)
			{
				outstanding = false;
				GaveUp = true;
				return;
			}

			sentTemplateID = wantedTemplateID;
			sentPage = wantedPage;
			outstanding = true;
			sentAt = now;
			++attempts;
			send(sentTemplateID, sentPage);
		}

		/// <summary>
		/// Offers a reply. Returns true when it answers the page the panel wants, which is the only
		/// case in which the panel should show it.
		/// </summary>
		/// <remarks>
		/// A reply to the request this panel sent frees it to send the next one, even when the
		/// panel has since moved on to another page; a reply to some other panel's request for the
		/// same page is accepted too, since the rows are the same for everyone.
		/// </remarks>
		public bool Accept(in LeaderboardPageBroadcast reply)
		{
			if (outstanding && Answers(reply, sentTemplateID, sentPage))
			{
				outstanding = false;
			}
			if (hasWanted && Answers(reply, wantedTemplateID, wantedPage))
			{
				answered = true;
				return true;
			}
			return false;
		}

		/// <summary>An unavailable reply answers any page of its board: the board as a whole could not be read.</summary>
		private static bool Answers(in LeaderboardPageBroadcast reply, int templateID, int page)
		{
			return reply.TemplateID == templateID && (reply.Unavailable || reply.Page == page);
		}
	}
}
