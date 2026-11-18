using System;
using System.Collections.Generic;

namespace FishMMO.Server.Core.World.SceneServer
{
	/// <summary>
	/// A connection waiting on a name lookup, and the form it asked in.
	/// </summary>
	/// <remarks>
	/// A lookup in flight is shared by everyone who asks for the same ID, whichever request carried
	/// the ask. A client that sent the batched request understands the batched answer; one that sent
	/// the single-ID request (an older client) does not, and would never hear back if it were
	/// answered in the batched form. So the waiter carries how it asked, and is answered the same way.
	/// </remarks>
	/// <typeparam name="TConnection">The connection type.</typeparam>
	public readonly struct NamingWaiter<TConnection> : IEquatable<NamingWaiter<TConnection>>
	{
		/// <summary>Who is waiting.</summary>
		public readonly TConnection Connection;

		/// <summary>True if it asked with the batched request and is answered with the batched reply.</summary>
		public readonly bool Batched;

		/// <summary>
		/// Creates a waiter.
		/// </summary>
		public NamingWaiter(TConnection connection, bool batched)
		{
			Connection = connection;
			Batched = batched;
		}

		/// <inheritdoc/>
		public bool Equals(NamingWaiter<TConnection> other)
		{
			return Batched == other.Batched && EqualityComparer<TConnection>.Default.Equals(Connection, other.Connection);
		}

		/// <inheritdoc/>
		public override bool Equals(object obj) => obj is NamingWaiter<TConnection> other && Equals(other);

		/// <inheritdoc/>
		public override int GetHashCode()
		{
			unchecked
			{
				return (EqualityComparer<TConnection>.Default.GetHashCode(Connection) * 397) ^ (Batched ? 1 : 0);
			}
		}
	}

	/// <summary>
	/// Whether a waiter is answered when its lookup finishes, and with what. Pure.
	/// </summary>
	/// <remarks>
	/// <list type="table">
	/// <item><term>Found, either form</term><description>answered with the name.</description></item>
	/// <item><term>Not found, batched</term><description>answered with an empty name: the client
	/// drops what was waiting on it rather than hold it for the session.</description></item>
	/// <item><term>Not found, single</term><description>not answered. The single-ID reply never had
	/// a not-found form, and the client that asked in it asks again.</description></item>
	/// </list>
	/// A lookup that could not run (the database did not answer) answers nobody, in either form;
	/// that is decided before this rule is asked.
	/// </remarks>
	public static class NamingAnswerRule
	{
		/// <summary>
		/// Decides one waiter's answer.
		/// </summary>
		/// <param name="batched">True if the waiter asked with the batched request.</param>
		/// <param name="name">The name found, or null or empty when there is no such entity.</param>
		/// <param name="reply">The name to send, or empty for "no such entity".</param>
		/// <returns>True if the waiter is sent an answer.</returns>
		public static bool TryGetReply(bool batched, string name, out string reply)
		{
			if (!string.IsNullOrEmpty(name))
			{
				reply = name;
				return true;
			}
			reply = string.Empty;
			return batched;
		}
	}

	/// <summary>
	/// Reads the IDs out of a batched name request. Pure.
	/// </summary>
	public static class NamingBatchRequest
	{
		/// <summary>
		/// The distinct positive IDs among the first <paramref name="limit"/> entries, in order.
		/// </summary>
		/// <remarks>
		/// <paramref name="limit"/> is what the request was admitted for: the message's own cap, and
		/// then the connection's budget, are both charged per entry before anything is read, so an
		/// entry past the limit is not looked at at all. A repeated ID is charged and not looked up
		/// twice; an honest client never repeats one.
		/// </remarks>
		/// <param name="ids">The request's IDs. Null reads as empty.</param>
		/// <param name="limit">How many leading entries to read.</param>
		/// <param name="seen">Scratch set. Cleared here.</param>
		/// <param name="into">Receives the IDs. Not cleared first.</param>
		/// <returns>The number of IDs added.</returns>
		public static int SelectIds(long[] ids, int limit, HashSet<long> seen, List<long> into)
		{
			seen.Clear();
			if (ids == null)
			{
				return 0;
			}

			int added = 0;
			int end = Math.Min(ids.Length, Math.Max(0, limit));
			for (int i = 0; i < end; i++)
			{
				long id = ids[i];
				if (id > 0 && seen.Add(id))
				{
					into.Add(id);
					++added;
				}
			}
			seen.Clear();
			return added;
		}
	}

	/// <summary>
	/// Batched name answers grouped by the connection they go to, then split into replies of at
	/// most a given size. Reusable; main thread only.
	/// </summary>
	/// <remarks>
	/// One lookup can finish for many connections at once, and one connection can be waiting on
	/// many of its IDs. Each connection is sent one reply per <c>maxEntries</c> answers, not one
	/// message per ID.
	/// </remarks>
	/// <typeparam name="TConnection">The connection type.</typeparam>
	public sealed class NamingReplyBatches<TConnection>
	{
		private sealed class Reply
		{
			public readonly List<long> IDs = new List<long>();
			public readonly List<string> Names = new List<string>();
		}

		private readonly Dictionary<TConnection, Reply> replies = new Dictionary<TConnection, Reply>();
		private readonly List<TConnection> order = new List<TConnection>();
		private readonly Stack<Reply> pool = new Stack<Reply>();

		/// <summary>Connections with at least one answer queued.</summary>
		public int ConnectionCount => order.Count;

		/// <summary>
		/// Queues one answer for a connection.
		/// </summary>
		/// <param name="connection">Who receives it.</param>
		/// <param name="id">The ID answered.</param>
		/// <param name="name">Its name, or empty for "no such entity".</param>
		public void Add(TConnection connection, long id, string name)
		{
			if (!replies.TryGetValue(connection, out Reply reply))
			{
				reply = pool.Count > 0 ? pool.Pop() : new Reply();
				replies.Add(connection, reply);
				order.Add(connection);
			}
			reply.IDs.Add(id);
			reply.Names.Add(name ?? string.Empty);
		}

		/// <summary>
		/// Hands every queued answer to <paramref name="send"/>, at most <paramref name="maxEntries"/>
		/// per call, connection by connection in the order they were first added; then empties.
		/// </summary>
		/// <param name="maxEntries">Most answers in one reply.</param>
		/// <param name="send">Sends one reply: the connection, its IDs, and the parallel names.</param>
		public void Drain(int maxEntries, Action<TConnection, long[], string[]> send)
		{
			int chunk = Math.Max(1, maxEntries);
			try
			{
				for (int c = 0; c < order.Count; c++)
				{
					Reply reply = replies[order[c]];
					for (int offset = 0; offset < reply.IDs.Count; offset += chunk)
					{
						int count = Math.Min(chunk, reply.IDs.Count - offset);
						var ids = new long[count];
						var names = new string[count];
						reply.IDs.CopyTo(offset, ids, 0, count);
						reply.Names.CopyTo(offset, names, 0, count);
						send?.Invoke(order[c], ids, names);
					}
				}
			}
			finally
			{
				Clear();
			}
		}

		/// <summary>
		/// Drops every queued answer.
		/// </summary>
		public void Clear()
		{
			for (int c = 0; c < order.Count; c++)
			{
				Reply reply = replies[order[c]];
				reply.IDs.Clear();
				reply.Names.Clear();
				pool.Push(reply);
			}
			replies.Clear();
			order.Clear();
		}
	}
}
