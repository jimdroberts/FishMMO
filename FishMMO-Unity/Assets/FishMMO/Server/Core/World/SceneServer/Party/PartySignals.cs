using System;
using System.Collections.Generic;
using FishMMO.Shared.Core;

namespace FishMMO.Server.Core.World.SceneServer
{
	/// <summary>
	/// Server-to-server messages addressed to a party, carried by the party chat pump (issue #241, Call to Arms): a module posts
	/// one; every scene server hosting a member of the party receives it, and no client is shown it.
	/// </summary>
	/// <remarks>
	/// <para><b>Why chat.</b> Scene servers talk only through the database, and the chat pump already delivers party rows to
	/// exactly the servers that host the party's members. A signal is a party row whose text, after the party id, starts with
	/// <see cref="Marker"/>, then its kind and a space and its payload. No new table, column or channel value, so the Discord
	/// relay (which never republishes party lines) and the chat search need nothing; the row stays as the audit record.</para>
	/// <para><b>Seams on both sides.</b> The Chat module installs <see cref="Poster"/> and calls <see cref="TryDispatch"/> for
	/// every pumped party line; the module that sends registers a handler per kind. Without the Chat module
	/// <see cref="Post"/> returns false and only the sender's own server can act.</para>
	/// <para><b>Forgery.</b> The chat system drops any line a PLAYER types that starts with the marker
	/// (<see cref="IsSignal"/>), so a signal only ever comes from a server. Payloads that move characters should still be
	/// signed (Call to Arms sends a signed travel ticket).</para>
	/// <para>The poster's own server does not receive its signal back (the pump skips a server's own party rows): deliver
	/// locally first.</para>
	/// </remarks>
	public static class PartySignals
	{
		/// <summary>The first character of a signal's text: U+0001 (start of heading), which no keyboard types and which .NET does not count as white space (U+001C-U+001F it does, and a Trim would eat them).</summary>
		public const char Marker = '\u0001';

		/// <summary>Longest kind accepted, in characters.</summary>
		public const int MaxKindLength = 16;

		/// <summary>
		/// Persists a party row (sender, party id, text after the id). Installed by the Chat module; null without it.
		/// </summary>
		public static Func<IPlayerCharacter, long, string, bool> Poster;

		private static readonly Dictionary<string, Action<long, string>> handlers = new Dictionary<string, Action<long, string>>(StringComparer.Ordinal);

		/// <summary>Whether a party line's text (after the party id) is a signal rather than something said.</summary>
		public static bool IsSignal(string text)
		{
			return !string.IsNullOrEmpty(text) && text[0] == Marker;
		}

		/// <summary>The text of a <paramref name="kind"/> signal carrying <paramref name="payload"/>.</summary>
		public static string Format(string kind, string payload)
		{
			return Marker + kind + " " + (payload ?? string.Empty);
		}

		/// <summary>Splits a signal's text into its kind and payload; false when it is not one.</summary>
		public static bool TryParse(string text, out string kind, out string payload)
		{
			kind = null;
			payload = null;
			if (!IsSignal(text))
			{
				return false;
			}
			int space = text.IndexOf(' ', 1);
			if (space <= 1 || space - 1 > MaxKindLength)
			{
				return false;
			}
			kind = text.Substring(1, space - 1);
			payload = text.Substring(space + 1);
			return true;
		}

		/// <summary>Registers the handler for <paramref name="kind"/> (party id, payload). Main thread.</summary>
		public static void Register(string kind, Action<long, string> handler)
		{
			if (!string.IsNullOrEmpty(kind) && handler != null)
			{
				handlers[kind] = handler;
			}
		}

		/// <summary>Removes the handler for <paramref name="kind"/>, if it is <paramref name="handler"/>.</summary>
		public static void Unregister(string kind, Action<long, string> handler)
		{
			if (!string.IsNullOrEmpty(kind) && handlers.TryGetValue(kind, out Action<long, string> current) && current == handler)
			{
				handlers.Remove(kind);
			}
		}

		/// <summary>Posts a signal to <paramref name="partyID"/>'s members on every other scene server. False without a poster.</summary>
		public static bool Post(IPlayerCharacter sender, long partyID, string kind, string payload)
		{
			Func<IPlayerCharacter, long, string, bool> poster = Poster;
			if (poster == null || sender == null || partyID < 1 || string.IsNullOrEmpty(kind) || kind.Length > MaxKindLength || kind.IndexOf(' ') >= 0)
			{
				return false;
			}
			return poster(sender, partyID, Format(kind, payload));
		}

		/// <summary>
		/// Hands a pumped party line to its handler when it is a signal. True when it was one (handled or not), so the caller
		/// shows it to nobody.
		/// </summary>
		public static bool TryDispatch(long partyID, string text)
		{
			if (!TryParse(text, out string kind, out string payload))
			{
				return IsSignal(text);
			}
			if (handlers.TryGetValue(kind, out Action<long, string> handler))
			{
				handler(partyID, payload);
			}
			return true;
		}

		/// <summary>Clears the handlers and the poster (server shutdown, tests).</summary>
		public static void Reset()
		{
			handlers.Clear();
			Poster = null;
		}
	}
}
