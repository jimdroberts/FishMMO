using System;

namespace FishMMO.Database.Data
{
	/// <summary>
	/// The world clock row as one statement read it, with the database clock it was read against.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <see cref="WorldMsNow"/> was computed inside the statement, against the same database clock
	/// that stamped <see cref="BaseReferenceMs"/>, so it does not carry the reader's clock skew. A
	/// reader that wants to keep the clock moving locally anchors <see cref="WorldMsNow"/> on its own
	/// monotonic clock the moment the reply arrives and advances it at <see cref="Rate"/>; it must
	/// not subtract its own wall clock from <see cref="BaseReferenceMs"/>.
	/// </para>
	/// <para>
	/// When <see cref="Exists"/> is false, no scene server has seeded the row yet. Only
	/// <see cref="DbNowMs"/> is meaningful then; every other field is zero or null.
	/// </para>
	/// </remarks>
	public sealed class WorldClockReading
	{
		/// <summary>Whether the row exists. False until a scene server has seeded it.</summary>
		public bool Exists { get; }

		/// <summary>The database clock, Unix milliseconds, as the statement read it.</summary>
		public long DbNowMs { get; }

		/// <summary>World milliseconds since the calendar epoch at <see cref="BaseReferenceMs"/>.</summary>
		public long BaseWorldMs { get; }

		/// <summary>Database Unix milliseconds when the base was written.</summary>
		public long BaseReferenceMs { get; }

		/// <summary>World seconds per real second: 0 held, 1 real time, above 1 raced.</summary>
		public double Rate { get; }

		/// <summary>The pace a resume returns to.</summary>
		public double ResumeRate { get; }

		/// <summary>The calendar epoch, Unix seconds. World timestamp = this instant + world ms.</summary>
		public long EpochUnixSeconds { get; }

		/// <summary>Incremented on every write; a new revision is adopted as an instant jump.</summary>
		public long Revision { get; }

		/// <summary>Account that made the last write; null for the seed.</summary>
		public string? UpdatedBy { get; }

		/// <summary>UTC time of the last write, by the database clock; null for the seed.</summary>
		public DateTime? UpdatedAt { get; }

		/// <summary>
		/// World milliseconds since the epoch at <see cref="DbNowMs"/>:
		/// base_world_ms + rate × max(0, db_now_ms − base_reference_ms), floored.
		/// </summary>
		public long WorldMsNow { get; }

		/// <summary>
		/// On the reading a WRITE returns: the world time the write replaced, at the same database
		/// instant as <see cref="DbNowMs"/>. Null on a plain read, and on a write that was a replay
		/// (its first attempt committed, the reply was lost, and the retry found its own idempotency
		/// key on the row — the write happened exactly once, and what it replaced is no longer known).
		/// </summary>
		public long? ReplacedWorldMs { get; }

		/// <summary>On the reading a write returns: the pace the write replaced. Null as for <see cref="ReplacedWorldMs"/>.</summary>
		public double? ReplacedRate { get; }

		/// <summary>Whether the clock is held (pace zero).</summary>
		public bool IsHeld => Exists && Rate <= 0;

		/// <summary>Creates a reading. Used by the service; a caller has no reason to build one.</summary>
		public WorldClockReading(
			bool exists,
			long dbNowMs,
			long baseWorldMs,
			long baseReferenceMs,
			double rate,
			double resumeRate,
			long epochUnixSeconds,
			long revision,
			string? updatedBy,
			DateTime? updatedAt,
			long worldMsNow,
			long? replacedWorldMs = null,
			double? replacedRate = null)
		{
			Exists = exists;
			DbNowMs = dbNowMs;
			BaseWorldMs = baseWorldMs;
			BaseReferenceMs = baseReferenceMs;
			Rate = rate;
			ResumeRate = resumeRate;
			EpochUnixSeconds = epochUnixSeconds;
			Revision = revision;
			UpdatedBy = updatedBy;
			UpdatedAt = updatedAt.HasValue ? DateTime.SpecifyKind(updatedAt.Value, DateTimeKind.Utc) : (DateTime?)null;
			WorldMsNow = worldMsNow;
			ReplacedWorldMs = replacedWorldMs;
			ReplacedRate = replacedRate;
		}

		/// <summary>A reading of a database that has no world clock row yet.</summary>
		public static WorldClockReading Missing(long dbNowMs) =>
			new WorldClockReading(false, dbNowMs, 0, 0, 0, 0, 0, 0, null, null, 0);
	}
}
