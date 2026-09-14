using System;

namespace FishMMO.Database.Data
{
	/// <summary>
	/// A character's staff lock, as stored.
	/// </summary>
	/// <remarks>
	/// The lock is in force only while <see cref="LockedUntil"/> is in the future. Nothing sweeps a
	/// lapsed one, so every reader asks <see cref="IsLocked"/> with its own clock rather than trusting
	/// that a non-null end means a lock.
	/// </remarks>
	public readonly struct CharacterLockState
	{
		/// <summary>When the lock lapses (UTC), or null when none was placed.</summary>
		public readonly DateTime? LockedUntil;

		/// <summary>The staff account that placed it.</summary>
		public readonly string LockedBy;

		/// <summary>Why it was placed.</summary>
		public readonly string LockReason;

		public CharacterLockState(DateTime? lockedUntil, string lockedBy, string lockReason)
		{
			LockedUntil = lockedUntil;
			LockedBy = lockedBy;
			LockReason = lockReason;
		}

		/// <summary>Whether the lock is in force at <paramref name="nowUtc"/>.</summary>
		public bool IsLocked(DateTime nowUtc) => LockedUntil.HasValue && LockedUntil.Value > nowUtc;
	}
}
