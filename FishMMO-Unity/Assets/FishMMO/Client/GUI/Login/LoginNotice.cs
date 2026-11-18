using System.Collections.Generic;
using FishMMO.Logging;

namespace FishMMO.Client
{
	/// <summary>
	/// A retrying queue for login-flow messages that must always reach the player.
	/// </summary>
	/// <remarks>
	/// <see cref="UITKDialogBox"/> now refuses rather than hijacks: an <c>Open</c> that arrives
	/// while another question is on screen returns false and leaves the live dialog alone. That is
	/// right for a question — answering the wrong one is worse than waiting — but it silently
	/// drops a one-way notice, and the login flow is full of notices that are the only explanation
	/// the player will ever get for being thrown back to the sign-in screen. "Account is banned",
	/// "your session has expired" and "the server closed the connection" are all raised on paths
	/// that also disconnect, so there is no second chance to say them.
	/// <para>
	/// Rather than teach every call site a retry, they go in here and the login panels pump the
	/// queue from their per-frame tick. A notice waits for the dialog to become free and is then
	/// shown exactly once. Consecutive duplicates collapse, so a disconnect that trips two
	/// handlers does not queue the same sentence twice.
	/// </para>
	/// <para>
	/// Bounded at <see cref="MaxQueued"/>. A notice storm is itself a fault, and a queue that grew
	/// without limit would make the player dismiss dialogs for as long as it lasted.
	/// </para>
	/// </remarks>
	public static class LoginNotice
	{
		/// <summary>
		/// Most notices held at once. Beyond this the oldest are dropped.
		/// </summary>
		private const int MaxQueued = 4;

		/// <summary>A queued notice: its message, and an optional link offered as the dialog's accept button.</summary>
		private readonly struct Entry
		{
			public readonly string Message;
			public readonly string LinkUrl;
			public readonly string LinkLabel;

			public Entry(string message, string linkUrl, string linkLabel)
			{
				Message = message;
				LinkUrl = linkUrl;
				LinkLabel = linkLabel;
			}
		}

		/// <summary>Messages waiting for the shared dialog to become free.</summary>
		private static readonly Queue<Entry> pending = new Queue<Entry>();

		/// <summary>
		/// Queues a message for display, or shows it immediately when the dialog is free.
		/// </summary>
		/// <param name="message">The message. Null or blank is ignored.</param>
		public static void Show(string message)
		{
			Show(message, null, null);
		}

		/// <summary>
		/// Queues a message whose accept button opens a link, or shows it immediately when the dialog is free.
		/// </summary>
		/// <param name="message">The message. Null or blank is ignored.</param>
		/// <param name="linkUrl">An http(s) link the accept button opens; null for a plain notice.</param>
		/// <param name="linkLabel">The accept button's label, such as "Get help".</param>
		public static void Show(string message, string linkUrl, string linkLabel)
		{
			if (string.IsNullOrWhiteSpace(message))
			{
				return;
			}

			// Collapse a repeat of whatever is already at the back of the queue.
			foreach (Entry queued in pending)
			{
				if (string.Equals(queued.Message, message, System.StringComparison.Ordinal))
				{
					return;
				}
			}

			pending.Enqueue(new Entry(message, linkUrl, linkLabel));
			while (pending.Count > MaxQueued)
			{
				pending.Dequeue();
			}

			Pump();
		}

		/// <summary>
		/// Hands the next queued message to the dialog box if it will take one.
		/// </summary>
		/// <remarks>
		/// Called from the login panels' <c>OnTick</c>. Cheap when the queue is empty, which is
		/// the overwhelmingly common case, because that is the first thing it checks.
		/// </remarks>
		public static void Pump()
		{
			if (pending.Count < 1)
			{
				return;
			}

			if (!UIManager.TryGetTK("UIDialogBox", out UITKDialogBox dialogBox))
			{
				/* No dialog panel in this scene. Log and drop rather than hold the queue
				 * forever — the message is still recoverable from the log, and a queue that
				 * can never drain would fire every one of its entries the moment a dialog
				 * panel did appear, minutes later and out of context. */
				while (pending.Count > 0)
				{
					Log.Warning("LoginNotice", pending.Dequeue().Message);
				}
				return;
			}

			// Peek, not dequeue: a refused Open must leave the message queued for the next tick.
			Entry entry = pending.Peek();
			bool opened;
			if (string.IsNullOrEmpty(entry.LinkUrl))
			{
				opened = dialogBox.Open(entry.Message);
			}
			else
			{
				/* The link is the accept button; dismissing is the cancel button, which needs a
				 * callback or the dialog hides it. The URL is opened through the launcher's link
				 * policy so only http(s) can ever reach the OS. */
				string url = entry.LinkUrl;
				opened = dialogBox.Open(entry.Message,
					() => LauncherLinkPolicy.OpenIfSafe(url),
					() => { },
					string.IsNullOrEmpty(entry.LinkLabel) ? "Open link" : entry.LinkLabel,
					"Close");
			}
			if (opened)
			{
				pending.Dequeue();
			}
		}

		/// <summary>
		/// Drops every queued message.
		/// </summary>
		/// <remarks>
		/// Used by the quit-to-login teardown. A notice describes one session; carrying it across
		/// a teardown surfaces a stale explanation for whatever the player does next.
		/// </remarks>
		public static void Clear()
		{
			pending.Clear();
		}
	}
}
