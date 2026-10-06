using System;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// The one world clock: a single row (<see cref="ID"/> = 1) that every scene server adopts on
	/// its next pulse and that admins write, in game (<c>/admin time</c>) or from the Control Panel.
	/// </summary>
	/// <remarks>
	/// <para>
	/// World time is "world milliseconds since the calendar epoch", and it is never stored as a
	/// running value. The row is an anchor: at database time <see cref="BaseReferenceMs"/> the world
	/// read <see cref="BaseWorldMs"/>, and it has advanced at <see cref="Rate"/> world seconds per
	/// real second since. World ms now = base_world_ms + rate × (db_now_ms − base_reference_ms),
	/// computed in SQL against the database clock, so every reader agrees on it whatever its own
	/// clock says. See <c>WorldClockControlService</c>.
	/// </para>
	/// <para>
	/// Every write re-anchors at the moment it is made and bumps <see cref="Revision"/>. A scene
	/// server that reads a new revision treats it as an instant jump rather than smoothing towards
	/// it — that is what an admin setting the time asked for.
	/// </para>
	/// <para>
	/// No <c>xmin</c> token: every write is one raw <c>UPDATE … RETURNING</c> that computes from the
	/// row it locks, so there is no read-modify-write for a token to protect.
	/// </para>
	/// </remarks>
	public class WorldClockControlEntity
	{
		/// <summary>Always 1. A check constraint keeps it the only row.</summary>
		public int ID { get; set; }

		/// <summary>World milliseconds since the calendar epoch at <see cref="BaseReferenceMs"/>.</summary>
		public long BaseWorldMs { get; set; }

		/// <summary>
		/// The DATABASE clock, in Unix milliseconds, when <see cref="BaseWorldMs"/> was written.
		/// </summary>
		public long BaseReferenceMs { get; set; }

		/// <summary>World seconds per real second: 0 held, 1 real time, above 1 raced.</summary>
		public double Rate { get; set; }

		/// <summary>The pace a resume returns to. Always above zero.</summary>
		public double ResumeRate { get; set; }

		/// <summary>
		/// The calendar epoch the scene servers use, in Unix seconds. Stored so a reader without the
		/// Unity calendar asset (the Control Panel) can write and read world timestamps.
		/// </summary>
		public long EpochUnixSeconds { get; set; }

		/// <summary>Incremented on every write.</summary>
		public long Revision { get; set; }

		/// <summary>Account that made the last write, or null for the seed.</summary>
		public string? UpdatedBy { get; set; }

		/// <summary>UTC time of the last write, by the database clock.</summary>
		public DateTime? UpdatedAt { get; set; }

		/// <summary>
		/// The idempotency key of the last write. A retried write whose first attempt committed
		/// but lost its reply finds its own key here and is not applied twice — which matters for
		/// a relative shift, where a second application would move the world twice as far.
		/// </summary>
		public Guid? RequestKey { get; set; }
	}
}
