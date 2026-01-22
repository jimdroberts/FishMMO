using System;

namespace FishMMO.Database.Npgsql.Monitoring.Health
{
	/// <summary>
	/// Contains the result of a database health check operation.
	/// Provides detailed information about database connectivity, performance, and any errors encountered.
	/// </summary>
	public sealed class HealthCheckResult
	{
		/// <summary>
		/// Gets or sets the overall health status of the database.
		/// </summary>
		public HealthStatus Status { get; set; }

		/// <summary>
		/// Gets or sets a human-readable message describing the health check result.
		/// </summary>
		public string Message { get; set; }

		/// <summary>
		/// Gets or sets whether the database connection was successfully established.
		/// </summary>
		public bool IsConnected { get; set; }

		/// <summary>
		/// Gets or sets the response time in milliseconds for the health check query.
		/// </summary>
		public double ResponseTimeMs { get; set; }

		/// <summary>
		/// Gets or sets the name of the database that was checked.
		/// </summary>
		public string DatabaseName { get; set; }

		/// <summary>
		/// Gets or sets the server address of the database.
		/// </summary>
		public string ServerAddress { get; set; }

		/// <summary>
		/// Gets or sets information about the connection pool configuration.
		/// </summary>
		public string PoolInfo { get; set; }

		/// <summary>
		/// Gets or sets the active connection count.
		/// </summary>
		public long ActiveConnections { get; set; }

		/// <summary>
		/// Gets or sets the peak connection count.
		/// </summary>
		public long PeakConnections { get; set; }

		/// <summary>
		/// Gets or sets the total connections created.
		/// </summary>
		public long TotalConnectionsCreated { get; set; }

		/// <summary>
		/// Gets or sets the pool utilization percentage (0-100).
		/// </summary>
		public double PoolUtilizationPercent { get; set; }

		/// <summary>
		/// Gets or sets the connection error count.
		/// </summary>
		public long ConnectionErrors { get; set; }

		/// <summary>
		/// Gets or sets the pool exhaustion count.
		/// </summary>
		public long PoolExhaustionCount { get; set; }

		/// <summary>
		/// Gets or sets the pool health status based on utilization and error metrics.
		/// </summary>
		public PoolHealthStatus PoolHealthStatus { get; set; }

		/// <summary>
		/// Gets or sets the pool health message.
		/// </summary>
		public string PoolHealthMessage { get; set; }

		/// <summary>
		/// Gets or sets whether the pool health requires immediate action.
		/// </summary>
		public bool PoolRequiresAction { get; set; }

		/// <summary>
		/// Gets or sets the error code if an error occurred during the health check.
		/// </summary>
		public string ErrorCode { get; set; }

		/// <summary>
		/// Gets or sets whether the health check passed but with warnings (e.g., slow response time).
		/// </summary>
		public bool HasWarning { get; set; }

		/// <summary>
		/// Gets or sets the exception that occurred during the health check, if any.
		/// </summary>
		public Exception Exception { get; set; }

		/// <summary>
		/// Initializes a new instance of the <see cref="HealthCheckResult"/> class with default values.
		/// </summary>
		public HealthCheckResult()
		{
			Status = HealthStatus.Unknown;
			Message = string.Empty;
			IsConnected = false;
			ResponseTimeMs = 0;
			DatabaseName = string.Empty;
			ServerAddress = string.Empty;
			PoolInfo = string.Empty;
			ActiveConnections = 0;
			PeakConnections = 0;
			TotalConnectionsCreated = 0;
			PoolUtilizationPercent = 0;
			ConnectionErrors = 0;
			PoolExhaustionCount = 0;
			PoolHealthStatus = PoolHealthStatus.Unknown;
			PoolHealthMessage = string.Empty;
			PoolRequiresAction = false;
			ErrorCode = string.Empty;
			HasWarning = false;
			Exception = null;
		}

		/// <summary>
		/// Returns a string representation of the health check result.
		/// </summary>
		/// <returns>A formatted string containing the key health check information.</returns>
		public override string ToString()
		{
			return $"[{Status}] {Message} | Connected: {IsConnected}, Response: {ResponseTimeMs:F2}ms";
		}

		/// <summary>
		/// Creates a sanitized copy of the health check result suitable for external exposure.
		/// Redacts sensitive infrastructure details including database names, server addresses,
		/// connection pool details, and exception information.
		/// </summary>
		/// <returns>A new HealthCheckResult with sensitive information redacted.</returns>
		/// <remarks>
		/// <para><b>Security:</b> This method should be used when exposing health check results
		/// via public APIs, logging to external systems, or any scenario where the data may be
		/// accessible to unauthorized users.</para>
		/// <para><b>Redacted Fields:</b>
		/// - DatabaseName → "***"
		/// - ServerAddress → "***"
		/// - PoolInfo → Summary text without connection details
		/// - ErrorCode → "***" (if present)
		/// - Exception → null
		/// </para>
		/// <para><b>Preserved Fields:</b>
		/// - Status, Message, IsConnected, ResponseTimeMs
		/// - Connection metrics (counts and percentages)
		/// - Pool health indicators
		/// - HasWarning flag
		/// </para>
		/// </remarks>
		public HealthCheckResult Sanitize()
		{
			return new HealthCheckResult
			{
				// Preserve health status and metrics
				Status = this.Status,
				Message = this.Message,
				IsConnected = this.IsConnected,
				ResponseTimeMs = this.ResponseTimeMs,
				HasWarning = this.HasWarning,

				// Redact sensitive infrastructure details
				DatabaseName = "***",
				ServerAddress = "***",
				PoolInfo = this.PoolUtilizationPercent > 0 
					? $"Utilization: {this.PoolUtilizationPercent:F1}%" 
					: string.Empty,
				ErrorCode = string.IsNullOrEmpty(this.ErrorCode) ? string.Empty : "***",

				// Preserve non-sensitive metrics
				ActiveConnections = this.ActiveConnections,
				PeakConnections = this.PeakConnections,
				TotalConnectionsCreated = this.TotalConnectionsCreated,
				PoolUtilizationPercent = this.PoolUtilizationPercent,
				ConnectionErrors = this.ConnectionErrors,
				PoolExhaustionCount = this.PoolExhaustionCount,
				PoolHealthStatus = this.PoolHealthStatus,
				PoolHealthMessage = this.PoolHealthMessage,
				PoolRequiresAction = this.PoolRequiresAction,

				// Redact exception details
				Exception = null
			};
		}
	}
}