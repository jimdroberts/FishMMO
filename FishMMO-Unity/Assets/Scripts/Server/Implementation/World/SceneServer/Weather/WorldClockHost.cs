using System;
using System.Data.Common;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Managing.Timing;
using FishNet.Transporting;
using Microsoft.EntityFrameworkCore;
using FishMMO.Database;
using FishMMO.Logging;
using FishMMO.Shared;
using FishMMO.Shared.Celestial;

namespace FishMMO.Server.Implementation.World.SceneServer.Weather
{
	/// <summary>
	/// Keeps this scene server's <see cref="WorldClock"/> anchored to the database server's clock
	/// and tells clients the anchor.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The database clock is the single reference: every scene server already talks to it, so they
	/// all agree even when their own host clocks are wrong. The host clock is only used to anchor
	/// immediately at startup (marked unverified) and to measure the query's round trip.
	/// </para>
	/// <para>
	/// <b>The world clock control.</b> An admin may set the world's time to the millisecond, hold it
	/// or race it (<c>/admin time</c>, the Control Panel). That is one database row,
	/// <c>world_clock_control</c>: world time = base + rate × (database clock − reference). It is read
	/// with the database clock in one round trip every <see cref="ControlPollSeconds"/>; a new
	/// revision is adopted at once (<see cref="WorldClock.Override"/>) at the tick it is read on, and
	/// broadcast, so every scene server and client jumps to it within a poll. Between revisions only
	/// the clock's own drift is corrected, eased in over ten seconds as before.
	/// </para>
	/// <para>
	/// Without the table (the migration not yet applied) it runs as it always did: world time is the
	/// database clock less the epoch, at real time.
	/// </para>
	/// </remarks>
	public sealed class WorldClockHost
	{
		public const float VerifiedIntervalSeconds = 600f;
		public const float UnverifiedIntervalSeconds = 30f;
		/// <summary>How often the control row is read, s: how soon an admin's change reaches this server.</summary>
		public const float ControlPollSeconds = 2f;
		private const string ClockQuery = "SELECT (extract(epoch from clock_timestamp()) * 1000)::bigint";
		/// <summary>The database clock and the control row, in one round trip; the row's columns are null without one.</summary>
		private const string ControlQuery =
			"SELECT (extract(epoch from clock_timestamp()) * 1000)::bigint, c.base_world_ms, c.base_reference_ms, c.rate, c.revision " +
			"FROM (SELECT 1) AS one LEFT JOIN world_clock_control AS c ON c.id = 1";

		private readonly NetworkManager networkManager;
		private readonly IDatabase database;
		private readonly long epochUnixSeconds;
		private readonly CancellationTokenSource cancellation = new CancellationTokenSource();

		private readonly Func<Task<bool>> seedControl;
		private float untilCheck;
		private Task<Measurement> inflight;
		/// <summary>False once the control table was found missing; asked again after <see cref="VerifiedIntervalSeconds"/>.</summary>
		private bool controlAvailable = true;
		private float untilControlRetry;
		private long adoptedRevision = long.MinValue;
		private bool seedRequested;

		private struct Measurement
		{
			public bool Success;
			public long UnixMilliseconds;
			public long QueryStart;
			public long QueryEnd;
			public string Error;
			/// <summary>The control table exists (whether or not it has its row).</summary>
			public bool ControlTable;
			/// <summary>The control row was there.</summary>
			public bool HasControl;
			public long BaseWorldMs;
			public long BaseReferenceMs;
			public double Rate;
			public long Revision;
		}

		/// <param name="seedControl">Writes the control row if there is none (the world clock service), true when one exists after.</param>
		public WorldClockHost(NetworkManager networkManager, IDatabase database, long epochUnixSeconds, Func<Task<bool>> seedControl = null)
		{
			this.networkManager = networkManager;
			this.database = database;
			this.epochUnixSeconds = epochUnixSeconds;
			this.seedControl = seedControl;
		}

		/// <summary>The revision of the control row this server runs on; long.MinValue before one is read.</summary>
		public long AdoptedRevision => adoptedRevision;

		/// <summary>Reads the control row at the next update, instead of at the next poll: after an admin on this server wrote it.</summary>
		public void CheckNow()
		{
			if (inflight == null)
			{
				untilCheck = 0f;
			}
		}

		/// <summary>Seconds until the next database check. Diagnostics only.</summary>
		public float SecondsUntilCheck => untilCheck;

		public void Start()
		{
			WorldClock clock = WorldClock.Shared;
			clock.Reset();
			clock.TickDelta = networkManager.TimeManager.TickDelta;
			clock.OnAnchorChanged += Clock_OnAnchorChanged;
			// Anchor on the host clock at once so time exists from the first tick; the database check replaces it.
			clock.Propose(networkManager.TimeManager.Tick, WorldClock.WorldSecondsFromUtc(DateTime.UtcNow, epochUnixSeconds), verified: false);
			untilCheck = 0f;
		}

		public void Stop()
		{
			cancellation.Cancel();
			WorldClock.Shared.OnAnchorChanged -= Clock_OnAnchorChanged;
		}

		public void Tick(float deltaTime)
		{
			if (inflight != null)
			{
				if (!inflight.IsCompleted)
				{
					return;
				}
				Adopt(inflight.IsFaulted || inflight.IsCanceled
					? new Measurement { Error = inflight.Exception?.GetBaseException().Message ?? "cancelled" }
					: inflight.Result);
				inflight = null;
				return;
			}
			untilCheck -= deltaTime;
			if (!controlAvailable)
			{
				untilControlRetry -= deltaTime;
				if (untilControlRetry <= 0f)
				{
					controlAvailable = true;
				}
			}
			if (untilCheck > 0f)
			{
				return;
			}
			untilCheck = UnverifiedIntervalSeconds;
			if (database?.DbContextFactory == null)
			{
				return;
			}
			CancellationToken token = cancellation.Token;
			IDatabase db = database;
			bool withControl = controlAvailable;
			inflight = Task.Run(() => MeasureAsync(db, withControl, token), token);
		}

		/// <summary>Sends the current anchor to one client, on arrival.</summary>
		public void SendTo(NetworkConnection connection)
		{
			if (connection == null || !connection.IsActive || !WorldClock.Shared.HasAnchor)
			{
				return;
			}
			networkManager.ServerManager.Broadcast(connection, CurrentBroadcast(), true, Channel.Reliable);
		}

		private static WorldClockBroadcast CurrentBroadcast()
		{
			WorldClock clock = WorldClock.Shared;
			return new WorldClockBroadcast
			{
				Anchor = clock.Current,
				Previous = clock.Previous,
				HasPrevious = clock.HasPrevious,
				SlewTicks = clock.SlewTicks,
			};
		}

		private void Clock_OnAnchorChanged(WorldClock clock)
		{
			if (networkManager.IsServerStarted)
			{
				networkManager.ServerManager.Broadcast(CurrentBroadcast(), true, Channel.Reliable);
			}
		}

		private void Adopt(Measurement measurement)
		{
			WorldClock clock = WorldClock.Shared;
			if (!measurement.Success)
			{
				_ = Log.Warning("WorldClockHost", $"Database clock unavailable ({measurement.Error}); running on the host clock, retrying in {UnverifiedIntervalSeconds:0}s.");
				untilCheck = UnverifiedIntervalSeconds;
				return;
			}
			if (!measurement.ControlTable && controlAvailable)
			{
				controlAvailable = false;
				untilControlRetry = VerifiedIntervalSeconds;
				_ = Log.Warning("WorldClockHost", "No world_clock_control table: the world clock runs at real time from the database clock and cannot be set. Apply the database migration to enable /admin time.");
			}
			else if (measurement.ControlTable && !measurement.HasControl && !seedRequested && seedControl != null)
			{
				// The first scene server to run writes the row; until then this one keeps the database's real-time clock.
				seedRequested = true;
				_ = SeedAsync();
			}

			// The database value is best at the middle of the round trip; age it to "now", at the world's pace.
			double rate = measurement.HasControl ? Math.Max(0.0, measurement.Rate) : 1.0;
			double roundTrip = (measurement.QueryEnd - measurement.QueryStart) / (double)Stopwatch.Frequency;
			double sinceEnd = (Stopwatch.GetTimestamp() - measurement.QueryEnd) / (double)Stopwatch.Frequency;
			double atDatabase = measurement.HasControl
				? (measurement.BaseWorldMs + rate * (measurement.UnixMilliseconds - measurement.BaseReferenceMs)) / 1000.0
				: WorldClock.WorldSecondsFromUnixMilliseconds(measurement.UnixMilliseconds, epochUnixSeconds);
			double reference = atDatabase + (roundTrip * 0.5 + sinceEnd) * rate;

			TimeManager tm = networkManager.TimeManager;
			double preciseTick = tm.Tick + tm.GetTickPercentAsDouble();
			// At the whole tick: shift the reference back by the fraction already elapsed, at the world's pace.
			reference -= (preciseTick - tm.Tick) * tm.TickDelta * rate;

			if (measurement.HasControl && (measurement.Revision != adoptedRevision || Math.Abs(rate - clock.Rate) > 1e-9))
			{
				// A decision, not a drift: taken at once, exact, and broadcast (OnAnchorChanged).
				bool first = adoptedRevision == long.MinValue;
				clock.Override(tm.Tick, reference, rate, verified: true);
				adoptedRevision = measurement.Revision;
				if (!first)
				{
					_ = Log.Info("WorldClockHost", $"World clock set (revision {measurement.Revision}): {WorldTimeText.Write(reference, epochUnixSeconds)}, {WorldTimeText.Pace(rate)}.");
				}
			}
			else
			{
				bool published = clock.Propose(tm.Tick, reference, verified: true);
				if (Math.Abs(clock.LastMeasuredError) > WorldClock.WarnThresholdSeconds)
				{
					_ = Log.Warning("WorldClockHost", $"World clock was {clock.LastMeasuredError:0.0}s off the database clock; easing it in. Is the host clock (NTP) healthy?");
				}
				else if (published)
				{
					_ = Log.Debug("WorldClockHost", $"World clock anchored to the database clock (error {clock.LastMeasuredError * 1000.0:0} ms, round trip {roundTrip * 1000.0:0} ms).");
				}
			}
			// With the control table, polled often enough that an admin's change is seen within seconds.
			untilCheck = measurement.ControlTable ? ControlPollSeconds : VerifiedIntervalSeconds;
		}

		private async Task SeedAsync()
		{
			try
			{
				bool seeded = await seedControl().ConfigureAwait(false);
				if (!seeded)
				{
					seedRequested = false;
				}
			}
			catch (Exception ex)
			{
				seedRequested = false;
				_ = Log.Warning("WorldClockHost", $"Could not write the world clock control row: {ex.GetBaseException().Message}");
			}
		}

		private static async Task<Measurement> MeasureAsync(IDatabase db, bool withControl, CancellationToken token)
		{
			try
			{
				using (var context = db.DbContextFactory.CreateDbContext())
				{
					DbConnection connection = context.Database.GetDbConnection();
					await connection.OpenAsync(token).ConfigureAwait(false);
					try
					{
						if (withControl)
						{
							try
							{
								using (DbCommand command = connection.CreateCommand())
								{
									command.CommandText = ControlQuery;
									long start = Stopwatch.GetTimestamp();
									using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
									{
										long end = Stopwatch.GetTimestamp();
										if (!await reader.ReadAsync(token).ConfigureAwait(false) || reader.IsDBNull(0))
										{
											return new Measurement { Error = "no value", ControlTable = true };
										}
										bool hasRow = !reader.IsDBNull(1) && !reader.IsDBNull(2) && !reader.IsDBNull(3) && !reader.IsDBNull(4);
										return new Measurement
										{
											Success = true,
											UnixMilliseconds = Convert.ToInt64(reader.GetValue(0)),
											QueryStart = start,
											QueryEnd = end,
											ControlTable = true,
											HasControl = hasRow,
											BaseWorldMs = hasRow ? Convert.ToInt64(reader.GetValue(1)) : 0L,
											BaseReferenceMs = hasRow ? Convert.ToInt64(reader.GetValue(2)) : 0L,
											Rate = hasRow ? Convert.ToDouble(reader.GetValue(3)) : 1.0,
											Revision = hasRow ? Convert.ToInt64(reader.GetValue(4)) : 0L,
										};
									}
								}
							}
							catch (Exception ex) when (ex.GetBaseException().Message.Contains("world_clock_control"))
							{
								// The migration is not applied: fall through to the plain clock.
							}
						}
						using (DbCommand command = connection.CreateCommand())
						{
							command.CommandText = ClockQuery;
							long start = Stopwatch.GetTimestamp();
							object value = await command.ExecuteScalarAsync(token).ConfigureAwait(false);
							long end = Stopwatch.GetTimestamp();
							return new Measurement
							{
								Success = value != null && value != DBNull.Value,
								UnixMilliseconds = value != null && value != DBNull.Value ? Convert.ToInt64(value) : 0L,
								QueryStart = start,
								QueryEnd = end,
								Error = value == null || value == DBNull.Value ? "no value" : null,
							};
						}
					}
					finally
					{
						connection.Close();
					}
				}
			}
			catch (Exception ex)
			{
				return new Measurement { Error = ex.GetBaseException().Message };
			}
		}
	}
}
