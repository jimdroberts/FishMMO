namespace FishMMO.Database.Npgsql
{
	/// <summary>
	/// Configuration for retry policy in database operations.
	/// </summary>
	public sealed class RetryPolicyConfiguration
	{
		/// <summary>
		/// Gets or sets the maximum number of retry attempts for transient failures.
		/// Default is 3.
		/// </summary>
		public int MaxRetries { get; set; } = 3;

		/// <summary>
		/// Gets or sets the base delay in milliseconds between retry attempts.
		/// The actual delay is calculated as (BaseDelayMs * attemptNumber) + jitter.
		/// Default is 20ms.
		/// </summary>
		public int BaseDelayMs { get; set; } = 20;

		/// <summary>
		/// Gets or sets the maximum jitter in milliseconds added to retry delays.
		/// Jitter helps prevent thundering herd scenarios.
		/// Default is 10ms.
		/// </summary>
		public int MaxJitterMs { get; set; } = 10;
	}
}
