using System;
using System.Data.Common;
using FishMMO.Database.Data;

namespace FishMMO.Database.Npgsql.Services
{
	/// <summary>
	/// How the world and scene server services read a server row's operator state, and schedule a
	/// shutdown on it by delay, so every read measures the deadline and every delay is added to it
	/// by the same clock: the database's.
	/// </summary>
	/// <remarks>
	/// Four statements return a <see cref="ServerControlState"/>: both pulses, the world
	/// registration and the scene servers' read of a world's row. They share one column list and
	/// one mapper here because the part that matters is easy to get subtly wrong in one copy: the
	/// time left before the shutdown is taken by the database clock inside the statement. See
	/// <see cref="ServerControlState.ShutdownInSeconds"/>.
	/// </remarks>
	internal static class ServerControlSql
	{
		/// <summary>
		/// The three control columns, in the order <see cref="Read"/> maps them: the lock, the
		/// scheduled instant, and the seconds until it by the database clock.
		/// </summary>
		/// <param name="alias">Alias the server table is reachable by, or null when it is unaliased.</param>
		/// <remarks>
		/// <c>clock_timestamp()</c>, not <c>now()</c>: <c>now()</c> is the start of the transaction,
		/// and the reader anchors this value on its own clock when the reply arrives, so the
		/// measurement should be as late as the statement allows. <c>shutdown_at_utc</c> is a UTC
		/// <c>timestamp without time zone</c>, like every stamp the database writes, so it is
		/// compared with the database's UTC wall time. A null deadline yields a null here.
		/// </remarks>
		internal static string Columns(string alias)
		{
			string p = string.IsNullOrEmpty(alias) ? string.Empty : alias + ".";
			return $"{p}locked, {p}shutdown_at_utc, " +
				$"EXTRACT(EPOCH FROM ({p}shutdown_at_utc - (clock_timestamp() AT TIME ZONE 'UTC')))::double precision";
		}

		/// <summary>
		/// Schedules a server's shutdown a delay from now by the database clock and locks it, in
		/// one statement: <c>{0}</c> is the delay in seconds (bound as <c>double precision</c>),
		/// <c>{1}</c> the row id. Returns the deadline written.
		/// </summary>
		/// <param name="table">The world or scene server table, from the model.</param>
		/// <remarks>
		/// The delay is added to the database's time, not the requester's: every server counts the
		/// deadline down against the database clock (<see cref="Columns"/>), so an instant built
		/// from the requester's <c>DateTime.UtcNow</c> moved the shutdown by that host's skew — an
		/// operator asking for five minutes on a panel two minutes slow gave players three.
		/// <c>clock_timestamp()</c> rather than <c>now()</c> so a write inside a longer
		/// transaction still counts from the moment it is made.
		/// </remarks>
		internal static string ScheduleIn(string table)
		{
			return $@"UPDATE {table}
				SET shutdown_at_utc = (clock_timestamp() AT TIME ZONE 'UTC') + make_interval(secs => {{0}}),
				    locked = true
				WHERE id = {{1}}
				RETURNING shutdown_at_utc";
		}

		/// <summary>Maps the one column <see cref="ScheduleIn"/> returns, as a UTC instant.</summary>
		internal static DateTime? ReadScheduled(DbDataReader reader)
		{
			return DateTime.SpecifyKind(reader.GetDateTime(0), DateTimeKind.Utc);
		}

		/// <summary>
		/// Maps the three columns of <see cref="Columns"/>, starting at <paramref name="ordinal"/>.
		/// </summary>
		internal static ServerControlState Read(DbDataReader reader, int ordinal)
		{
			bool locked = reader.GetBoolean(ordinal);
			DateTime? shutdownAtUtc = reader.IsDBNull(ordinal + 1)
				? (DateTime?)null
				: DateTime.SpecifyKind(reader.GetDateTime(ordinal + 1), DateTimeKind.Utc);
			double? shutdownInSeconds = reader.IsDBNull(ordinal + 2)
				? (double?)null
				: reader.GetDouble(ordinal + 2);
			return new ServerControlState(locked, shutdownAtUtc, shutdownInSeconds);
		}
	}
}
