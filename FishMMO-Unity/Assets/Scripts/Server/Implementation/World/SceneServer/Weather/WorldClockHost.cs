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
	/// Checks run every <see cref="VerifiedIntervalSeconds"/>, or every
	/// <see cref="UnverifiedIntervalSeconds"/> while the database has not answered yet. A
	/// correction above a quarter second is eased in over ten seconds; nothing ever jumps.
	/// </para>
	/// </remarks>
	public sealed class WorldClockHost
	{
		public const float VerifiedIntervalSeconds = 600f;
		public const float UnverifiedIntervalSeconds = 30f;
		private const string ClockQuery = "SELECT (extract(epoch from clock_timestamp()) * 1000)::bigint";

		private readonly NetworkManager networkManager;
		private readonly IDatabase database;
		private readonly long epochUnixSeconds;
		private readonly CancellationTokenSource cancellation = new CancellationTokenSource();

		private float untilCheck;
		private Task<Measurement> inflight;

		private struct Measurement
		{
			public bool Success;
			public long UnixMilliseconds;
			public long QueryStart;
			public long QueryEnd;
			public string Error;
		}

		public WorldClockHost(NetworkManager networkManager, IDatabase database, long epochUnixSeconds)
		{
			this.networkManager = networkManager;
			this.database = database;
			this.epochUnixSeconds = epochUnixSeconds;
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
			inflight = Task.Run(() => MeasureAsync(db, token), token);
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
			// The database value is best at the middle of the round trip; age it to "now".
			double roundTrip = (measurement.QueryEnd - measurement.QueryStart) / (double)Stopwatch.Frequency;
			double sinceEnd = (Stopwatch.GetTimestamp() - measurement.QueryEnd) / (double)Stopwatch.Frequency;
			double reference = WorldClock.WorldSecondsFromUnixMilliseconds(measurement.UnixMilliseconds, epochUnixSeconds) + roundTrip * 0.5 + sinceEnd;

			TimeManager tm = networkManager.TimeManager;
			double preciseTick = tm.Tick + tm.GetTickPercentAsDouble();
			// Propose at the whole tick: shift the reference back by the fraction already elapsed.
			reference -= (preciseTick - tm.Tick) * tm.TickDelta;
			bool published = clock.Propose(tm.Tick, reference, verified: true);
			if (Math.Abs(clock.LastMeasuredError) > WorldClock.WarnThresholdSeconds)
			{
				_ = Log.Warning("WorldClockHost", $"World clock was {clock.LastMeasuredError:0.0}s off the database clock; easing it in. Is the host clock (NTP) healthy?");
			}
			else if (published)
			{
				_ = Log.Debug("WorldClockHost", $"World clock anchored to the database clock (error {clock.LastMeasuredError * 1000.0:0} ms, round trip {roundTrip * 1000.0:0} ms).");
			}
			untilCheck = VerifiedIntervalSeconds;
		}

		private static async Task<Measurement> MeasureAsync(IDatabase db, CancellationToken token)
		{
			try
			{
				using (var context = db.DbContextFactory.CreateDbContext())
				{
					DbConnection connection = context.Database.GetDbConnection();
					await connection.OpenAsync(token).ConfigureAwait(false);
					try
					{
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
