/*
 * QUERY PERFORMANCE TRACKING - INTEGRATION EXAMPLE
 * 
 * This file demonstrates how to integrate QueryPerformanceTracker into database services.
 * Copy these patterns into your actual service implementations.
 * 
 * NOTE: This file is excluded from compilation (see #if false directive below).
 * It serves as a reference guide for implementing query performance tracking.
 */

#if false  // This is example code, not meant to be compiled

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using FishMMO.Database.Data;
using FishMMO.Database.Exceptions;

namespace FishMMO.Database.Npgsql.Monitoring.Diagnostics
{
	// EXAMPLE 1: Basic Integration Pattern
	// =====================================
	// Add performance tracking to a simple query operation
	public class ExampleService_BasicPattern
	{
		private readonly INpgsqlDbContextFactory dbContextFactory;
		private readonly QueryPerformanceTracker performanceTracker;

		public ExampleService_BasicPattern(INpgsqlDbContextFactory dbContextFactory)
		{
			this.dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
			
			// Get the tracker from the factory (automatically configured from appsettings.json)
			this.performanceTracker = dbContextFactory.PerformanceTracker;
		}

		public async Task<DatabaseResult<string>> GetPlayerNameAsync(long playerId, CancellationToken cancellationToken = default)
		{
			const string operationName = "GetPlayerName";
			var stopwatch = Stopwatch.StartNew();
			bool success = false;

			try
			{
				await using var dbContext = dbContextFactory.CreateDbContext();

				var playerName = await dbContext.Characters
					.AsNoTracking()
					.Where(c => c.Id == playerId)
					.Select(c => c.Name)
					.FirstOrDefaultAsync(cancellationToken);

				if (playerName == null)
				{
					return DatabaseResult<string>.FromException(
						new DatabaseEntityNotFoundException("Character", "by ID", "Player not found."));
				}

				success = true;
				return DatabaseResult<string>.Success(playerName);
			}
			catch (Exception ex)
			{
				return DatabaseResult<string>.FromException(
					new DatabaseQueryException(operationName, "Failed to retrieve player name.", ex.Message, false, ex));
			}
			finally
			{
				// Record the query performance (automatically respects tracking level and sampling)
				stopwatch.Stop();
				performanceTracker?.RecordQuery(operationName, stopwatch.ElapsedMilliseconds, success);
			}
		}
	}

	// EXAMPLE 2: Advanced Integration with Multiple Operations
	// ==========================================================
	// Track different operations within the same service
	public class ExampleService_MultipleOperations
	{
		private readonly INpgsqlDbContextFactory dbContextFactory;
		private readonly QueryPerformanceTracker performanceTracker;

		public ExampleService_MultipleOperations(INpgsqlDbContextFactory dbContextFactory)
		{
			this.dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
			this.performanceTracker = dbContextFactory.PerformanceTracker;
		}

		// Track a read operation
		public async Task<DatabaseResult<int>> GetPlayerLevelAsync(long playerId, CancellationToken cancellationToken = default)
		{
			const string operationName = "Character.GetLevel";
			var stopwatch = Stopwatch.StartNew();
			bool success = false;

			try
			{
				await using var dbContext = dbContextFactory.CreateDbContext();

				var level = await dbContext.Characters
					.AsNoTracking()
					.Where(c => c.Id == playerId)
					.Select(c => c.Level)
					.FirstOrDefaultAsync(cancellationToken);

				success = true;
				return DatabaseResult<int>.Success(level);
			}
			catch (Exception ex)
			{
				return DatabaseResult<int>.FromException(
					new DatabaseQueryException(operationName, "Failed to retrieve player level.", ex.Message, false, ex));
			}
			finally
			{
				stopwatch.Stop();
				performanceTracker?.RecordQuery(operationName, stopwatch.ElapsedMilliseconds, success);
			}
		}

		// Track a write operation
		public async Task<DatabaseResult<bool>> UpdatePlayerLevelAsync(long playerId, int newLevel, CancellationToken cancellationToken = default)
		{
			const string operationName = "Character.UpdateLevel";
			var stopwatch = Stopwatch.StartNew();
			bool success = false;

			try
			{
				await using var dbContext = dbContextFactory.CreateDbContext();

				var rowsAffected = await dbContext.Database
					.ExecuteSqlInterpolatedAsync(
						$"UPDATE character SET level = {newLevel} WHERE id = {playerId}",
						cancellationToken);

				if (rowsAffected == 0)
				{
					return DatabaseResult<bool>.FromException(
						new DatabaseEntityNotFoundException("Character", "by ID", "Player not found."));
				}

				success = true;
				return DatabaseResult<bool>.Success(true);
			}
			catch (Exception ex)
			{
				return DatabaseResult<bool>.FromException(
					new DatabaseQueryException(operationName, "Failed to update player level.", ex.Message, false, ex));
			}
			finally
			{
				stopwatch.Stop();
				performanceTracker?.RecordQuery(operationName, stopwatch.ElapsedMilliseconds, success);
			}
		}

		// Track a complex operation
		public async Task<DatabaseResult<bool>> BatchUpdateInventoryAsync(long playerId, int[] itemIds, CancellationToken cancellationToken = default)
		{
			const string operationName = "Inventory.BatchUpdate";
			var stopwatch = Stopwatch.StartNew();
			bool success = false;

			try
			{
				await using var dbContext = dbContextFactory.CreateDbContext();
				
				// Complex operation that might take longer
				// ... implementation ...

				success = true;
				return DatabaseResult<bool>.Success(true);
			}
			catch (Exception ex)
			{
				return DatabaseResult<bool>.FromException(
					new DatabaseQueryException(operationName, "Failed to batch update inventory.", ex.Message, false, ex));
			}
			finally
			{
				stopwatch.Stop();
				performanceTracker?.RecordQuery(operationName, stopwatch.ElapsedMilliseconds, success);
			}
		}
	}

	// EXAMPLE 3: Using Slow Query Events
	// ===================================
	// Subscribe to slow query notifications for real-time alerting
	public class PerformanceMonitoringService
	{
		private readonly INpgsqlDbContextFactory dbContextFactory;
		private readonly QueryPerformanceTracker performanceTracker;

		public PerformanceMonitoringService(INpgsqlDbContextFactory dbContextFactory)
		{
			this.dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
			this.performanceTracker = dbContextFactory.PerformanceTracker;

			// Subscribe to slow query events
			if (performanceTracker != null)
			{
				performanceTracker.SlowQueryDetected += OnSlowQueryDetected;
			}
		}

		private void OnSlowQueryDetected(object sender, SlowQueryEventArgs e)
		{
			// Log the slow query for investigation
			Console.WriteLine($"[SLOW QUERY DETECTED]");
			Console.WriteLine($"  Operation: {e.OperationName}");
			Console.WriteLine($"  Duration: {e.DurationMs:F2}ms");
			Console.WriteLine($"  Threshold: {e.ThresholdMs:F2}ms");
			Console.WriteLine($"  Timestamp: {e.Timestamp:yyyy-MM-dd HH:mm:ss}");

			// In production, you might:
			// - Send an alert to monitoring system (e.g., Datadog, New Relic)
			// - Write to a dedicated slow query log
			// - Increment a metric counter
			// - Trigger automated performance analysis
		}

		public void PrintPerformanceReport()
		{
			if (performanceTracker == null)
			{
				Console.WriteLine("Query performance tracking is disabled.");
				return;
			}

			Console.WriteLine("\n=== QUERY PERFORMANCE REPORT ===");
			Console.WriteLine($"Tracking Level: {performanceTracker.Configuration.Level}");
			Console.WriteLine($"Total Operations Tracked: {performanceTracker.TotalOperationsTracked}");
			Console.WriteLine($"Unique Operations: {performanceTracker.UniqueOperationCount}");
			Console.WriteLine();

			// Get the slowest operations
			var slowestOps = performanceTracker.GetSlowestOperations(10);
			Console.WriteLine("Top 10 Slowest Operations (by Average):");
			foreach (var (opName, metrics) in slowestOps)
			{
				Console.WriteLine($"  {opName}:");
				Console.WriteLine($"    Executions: {metrics.TotalExecutions}");
				Console.WriteLine($"    Success Rate: {metrics.SuccessRate:P2}");
				Console.WriteLine($"    Avg: {metrics.AverageMs:F2}ms | Min: {metrics.MinMs:F2}ms | Max: {metrics.MaxMs:F2}ms");
				Console.WriteLine($"    P95: {metrics.P95Ms:F2}ms | P99: {metrics.P99Ms:F2}ms");
				Console.WriteLine($"    Slow Queries: {metrics.SlowQueryCount}");
			}

			Console.WriteLine();

			// Get operations with most slow queries
			var mostSlowQueries = performanceTracker.GetMostSlowQueries(10);
			Console.WriteLine("Top 10 Operations with Most Slow Queries:");
			foreach (var (opName, metrics) in mostSlowQueries)
			{
				Console.WriteLine($"  {opName}: {metrics.SlowQueryCount} slow queries ({metrics.SlowQueryCount * 100.0 / metrics.TotalExecutions:F1}%)");
			}

			Console.WriteLine("\n================================\n");
		}

		public void Dispose()
		{
			if (performanceTracker != null)
			{
				performanceTracker.SlowQueryDetected -= OnSlowQueryDetected;
			}
		}
	}

	// EXAMPLE 4: Configuration Management
	// ====================================
	// How to dynamically change tracking configuration
	public class ConfigurationExample
	{
		public static void DemonstrateConfigurationOptions()
		{
			// Default configuration (from appsettings.json)
			var config1 = new QueryPerformanceConfiguration();
			
			// Completely disabled (zero overhead)
			var config2 = new QueryPerformanceConfiguration
			{
				Enabled = false
			};

			// Basic tracking for production (minimal overhead)
			var config3 = new QueryPerformanceConfiguration
			{
				Enabled = true,
				Level = TrackingLevel.Basic,
				SlowQueryThresholdMs = 500.0,  // Alert on queries > 500ms
				SampleRate = 0.01  // Track 1% of queries (reduces overhead)
			};

			// Standard tracking for staging
			var config4 = new QueryPerformanceConfiguration
			{
				Enabled = true,
				Level = TrackingLevel.Standard,
				SlowQueryThresholdMs = 250.0,
				SampleRate = 0.1  // Track 10% of queries
			};

			// Full tracking for development/debugging
			var config5 = new QueryPerformanceConfiguration
			{
				Enabled = true,
				Level = TrackingLevel.Full,
				SlowQueryThresholdMs = 100.0,
				SampleRate = 1.0,  // Track 100% of queries
				MaxTrackedOperations = 5000  // Increase limit for detailed analysis
			};

			// Create tracker with custom configuration
			var tracker = new QueryPerformanceTracker(config5);

			// You can also change configuration at runtime (creates new tracker)
			// This is useful for temporarily enabling detailed tracking during investigations
		}
	}
}

/*
 * CONFIGURATION REFERENCE (appsettings.json)
 * ==========================================
 * 
 * {
 *   "QueryPerformanceTracking": {
 *     "Enabled": false,                    // Toggle on/off (zero overhead when false)
 *     "Level": "None",                     // None, Basic, Standard, Detailed, Full
 *     "SlowQueryThresholdMs": 1000,        // Threshold for slow query detection (ms)
 *     "SampleRate": 0.1,                   // Sample rate (0.0 to 1.0, default 0.1 = 10%)
 *     "MaxTrackedOperations": 1000         // Maximum unique operations to track
 *   }
 * }
 * 
 * TRACKING LEVELS:
 * - None: No tracking (equivalent to Enabled=false)
 * - Basic: Track only execution count and success rate (minimal overhead)
 * - Standard: + Track average execution time (slight overhead)
 * - Detailed: + Track min/max times (moderate overhead)
 * - Full: + Track P95/P99 percentiles, slow query detection (higher overhead)
 * 
 * RECOMMENDED SETTINGS BY ENVIRONMENT:
 * 
 * Production (Normal):
 *   Enabled: false
 *   Level: None
 * 
 * Production (Investigation):
 *   Enabled: true
 *   Level: Basic or Standard
 *   SampleRate: 0.01 to 0.1 (1-10%)
 * 
 * Staging:
 *   Enabled: true
 *   Level: Standard
 *   SampleRate: 0.1 (10%)
 * 
 * Development:
 *   Enabled: true
 *   Level: Full
 *   SampleRate: 1.0 (100%)
 */

#endif  // End of example code
