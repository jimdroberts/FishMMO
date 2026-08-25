namespace FishMMO.Database
{
	/// <summary>
	/// What a bulk write actually did, as distinct from whether it errored.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A batched, version-gated write has a richer outcome than success or failure, and collapsing
	/// it to a boolean loses the part callers need. Rows go missing between the caller's list and
	/// the database for two entirely different reasons, and only one of them is benign:
	/// </para>
	/// <list type="bullet">
	/// <item><description>
	/// <see cref="Filtered"/> — the service refused to attempt the row at all: the character is
	/// deleted, the template is unresolvable, or two rows in the batch collided on the same key.
	/// The caller asked for something that could not be done, and nothing about the database's
	/// state explains it. This is worth surfacing.
	/// </description></item>
	/// <item><description>
	/// <see cref="Superseded"/> — the row was attempted and lost the version race, because the
	/// database already holds a version at least as new. Nothing is lost: the stored value is the
	/// more recent of the two. Routine under concurrency, and not a failure.
	/// </description></item>
	/// </list>
	/// <para>
	/// The distinction matters because the caller cannot recover it on its own. The service
	/// deduplicates and filters before writing, so comparing <see cref="Applied"/> against the
	/// length of the list the caller passed in would attribute both causes to whichever the caller
	/// happened to assume.
	/// </para>
	/// </remarks>
	public readonly struct BulkWriteResult
	{
		/// <summary>Rows the caller handed to the service.</summary>
		public int Supplied { get; }

		/// <summary>
		/// Rows the statement actually tried, after the service dropped duplicates and anything
		/// belonging to a character or template it could not resolve.
		/// </summary>
		public int Attempted { get; }

		/// <summary>Rows inserted or updated.</summary>
		public int Applied { get; }

		/// <summary>
		/// Rows the service declined to attempt. Non-zero means the caller's batch contained
		/// something it could not act on — see the remarks on <see cref="BulkWriteResult"/>.
		/// </summary>
		public int Filtered => Supplied - Attempted;

		/// <summary>
		/// Rows attempted but not written, because the database already held a version at least as
		/// new. Benign.
		/// </summary>
		public int Superseded => Attempted - Applied;

		/// <summary>True when every supplied row was written.</summary>
		public bool IsComplete => Applied == Supplied;

		/// <summary>An outcome with nothing to do — an empty or fully filtered batch.</summary>
		public static BulkWriteResult Empty => new BulkWriteResult(0, 0, 0);

		/// <summary>
		/// Initializes a new instance of the <see cref="BulkWriteResult"/> struct.
		/// </summary>
		/// <param name="supplied">Rows the caller handed over.</param>
		/// <param name="attempted">Rows the statement tried.</param>
		/// <param name="applied">Rows written.</param>
		public BulkWriteResult(int supplied, int attempted, int applied)
		{
			Supplied = supplied;
			Attempted = attempted;
			Applied = applied;
		}

		/// <summary>
		/// Adds two outcomes, for a service that writes its batch in more than one statement.
		/// </summary>
		/// <param name="left">First outcome.</param>
		/// <param name="right">Second outcome.</param>
		/// <returns>The combined outcome.</returns>
		public static BulkWriteResult operator +(BulkWriteResult left, BulkWriteResult right)
		{
			return new BulkWriteResult(
				left.Supplied + right.Supplied,
				left.Attempted + right.Attempted,
				left.Applied + right.Applied);
		}

		/// <summary>
		/// Returns a short description of the outcome, suitable for a log line.
		/// </summary>
		/// <returns>Formatted counts.</returns>
		public override string ToString()
		{
			if (IsComplete)
			{
				return $"{Applied}/{Supplied} written";
			}
			return $"{Applied}/{Supplied} written ({Filtered} filtered, {Superseded} superseded)";
		}
	}
}
