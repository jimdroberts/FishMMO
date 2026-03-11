using System;
using System.Collections.Concurrent;
using System.Threading;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FishMMO.DiscordBot.Services
{
	/// <summary>
	/// Per-user sliding window rate limiter to prevent abuse of the Discord-to-game chat bridge.
	/// Uses a circular buffer of timestamps per user for O(1) per-check cost.
	/// </summary>
	public sealed class RateLimiterService : IDisposable
	{
		private readonly int maxMessages;
		private readonly TimeSpan window;
		private readonly ConcurrentDictionary<ulong, UserRateState> states = new();
		private readonly ILogger<RateLimiterService> logger;
		private readonly Timer cleanupTimer;
		private int disposed;

		/// <summary>
		/// Initializes a new instance of the <see cref="RateLimiterService"/> class.
		/// Reads rate limit settings from the "RateLimiting" configuration section.
		/// </summary>
		/// <param name="configuration">Application configuration.</param>
		/// <param name="logger">Logger instance.</param>
		public RateLimiterService(IConfiguration configuration, ILogger<RateLimiterService> logger)
		{
			this.logger = logger;

			var section = configuration.GetSection("RateLimiting");

			if (!int.TryParse(section["MaxMessagesPerWindow"], out int parsedMax) || parsedMax <= 0)
			{
				parsedMax = 5;
			}
			if (!int.TryParse(section["WindowSeconds"], out int parsedWindow) || parsedWindow <= 0)
			{
				parsedWindow = 10;
			}

			maxMessages = parsedMax;
			window = TimeSpan.FromSeconds(parsedWindow);

			cleanupTimer = new Timer(CleanupExpired, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));

			logger.LogInformation(
				"RateLimiterService initialized: {MaxMessages} messages per {WindowSeconds}s window.",
				maxMessages, parsedWindow);
		}

		/// <summary>
		/// Checks whether the specified user is rate-limited. If not, records the current attempt.
		/// </summary>
		/// <param name="userId">The Discord user snowflake.</param>
		/// <returns><c>true</c> if the user has exceeded the rate limit; otherwise <c>false</c>.</returns>
		public bool IsRateLimited(ulong userId)
		{
			var state = states.GetOrAdd(userId, _ => new UserRateState(maxMessages));
			return !state.TryConsume(window);
		}

		/// <summary>
		/// Returns the number of users currently tracked by the rate limiter.
		/// </summary>
		internal int TrackedUserCount => states.Count;

		/// <summary>
		/// Periodically removes user rate states that have been idle for twice the window duration.
		/// </summary>
		private void CleanupExpired(object? state)
		{
			DateTime cutoff = DateTime.UtcNow.Subtract(window + window);
			int removed = 0;
			foreach (var kvp in states)
			{
				if (kvp.Value.LastActivity < cutoff)
				{
					if (states.TryRemove(kvp.Key, out _))
					{
						removed++;
					}
				}
			}
			if (removed > 0)
			{
				logger.LogDebug("RateLimiter cleanup removed {Count} expired user entries.", removed);
			}
		}

		/// <inheritdoc />
		public void Dispose()
		{
			if (Interlocked.Exchange(ref disposed, 1) == 0)
			{
				cleanupTimer.Dispose();
			}
		}

		/// <summary>
		/// Tracks message timestamps for a single user using a lock-protected circular buffer.
		/// When the buffer is full, the oldest timestamp is checked against the window boundary.
		/// </summary>
		private sealed class UserRateState
		{
			private readonly long[] timestamps;
			private int index;
			private readonly object syncRoot = new();

			/// <summary>
			/// UTC time of the last successful consume call. Used for cleanup.
			/// </summary>
			internal DateTime LastActivity { get; private set; }

			/// <summary>
			/// Initializes a new instance with the specified capacity.
			/// </summary>
			/// <param name="capacity">Number of messages allowed within one window.</param>
			internal UserRateState(int capacity)
			{
				timestamps = new long[capacity];
				LastActivity = DateTime.UtcNow;
			}

			/// <summary>
			/// Attempts to consume a rate limit token. Returns <c>true</c> if the message is allowed.
			/// </summary>
			/// <param name="window">The sliding window duration.</param>
			/// <returns><c>true</c> if under the limit; <c>false</c> if rate-limited.</returns>
			internal bool TryConsume(TimeSpan window)
			{
				lock (syncRoot)
				{
					long now = DateTime.UtcNow.Ticks;
					long windowTicks = window.Ticks;
					long oldest = timestamps[index];

					if (oldest != 0 && (now - oldest) < windowTicks)
					{
						return false;
					}

					timestamps[index] = now;
					index = (index + 1) % timestamps.Length;
					LastActivity = DateTime.UtcNow;
					return true;
				}
			}
		}
	}
}