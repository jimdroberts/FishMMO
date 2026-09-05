using System;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Ownership of one authored plot of land.
	/// </summary>
	/// <remarks>
	/// The row records who owns a foundation area, not where or how big it is. The geometry is part
	/// of the scene asset a designer authored it in, so it needs no storage and no synchronisation;
	/// what the cluster has to agree on is the answer to "who has claimed this", which is the only
	/// thing here that changes at runtime.
	///
	/// <para>Identified by <see cref="SceneName"/> and <see cref="PlotKey"/> together, deliberately
	/// not by any scene <em>instance</em>. Channels are several live copies of one scene, and a plot
	/// is meant to look the same in every one of them.</para>
	/// </remarks>
	public class PlotEntity : IVersionedEntity
	{
		/// <summary>Primary key.</summary>
		public long ID { get; set; }

		/// <summary>
		/// Application-level concurrency token. Incremented on every write to detect stale updates.
		/// </summary>
		public long Version { get; set; }

		/// <summary>
		/// The world server this land belongs to.
		/// </summary>
		/// <remarks>
		/// Part of the plot's identity, not a detail of it. Several world servers run the same
		/// scenes from the same build, so a plot keyed only by scene and key would be one row shared
		/// between them — and a player who bought a house on one world would find it already owned
		/// on every other. Each world has its own land.
		///
		/// <para>Not the scene <em>server</em>: those come and go, and several may host copies of one
		/// world's scene at once. Not a scene instance either — channels are several live copies of
		/// one scene and a plot looks the same in all of them.</para>
		/// </remarks>
		public long WorldServerID { get; set; }

		/// <summary>The Unity scene the plot's foundation is authored in.</summary>
		public string SceneName { get; set; }

		/// <summary>
		/// The designer-authored key identifying this foundation within its scene.
		/// </summary>
		/// <remarks>
		/// Stored already canonicalised, lower-cased and trimmed, by
		/// <c>FishMMO.Shared.PlotIdentity</c>. One stored form means a designer cannot create two
		/// plots that differ only in casing and read as the same one everywhere they are displayed.
		/// </remarks>
		public string PlotKey { get; set; }

		/// <summary>
		/// The owning character, or zero when no character owns it.
		/// </summary>
		/// <remarks>
		/// No foreign key, because zero is a real stored value and would have nothing to point at.
		/// Characters are soft-deleted in any case, so the row a key would protect never actually
		/// goes away; a guild does get hard-deleted, and releasing its plots is the housing
		/// system's job rather than the schema's.
		///
		/// <para>At most one of this and <see cref="OwnerGuildID"/> is ever set. Nothing in the
		/// schema enforces that — the invariant lives in <c>FishMMO.Shared.PlotOwner</c>, which
		/// cannot represent both and refuses to read a row that holds both.</para>
		/// </remarks>
		public long OwnerCharacterID { get; set; }

		/// <summary>
		/// The owning guild, or zero when no guild owns it.
		/// </summary>
		public long OwnerGuildID { get; set; }

		/// <summary>Row creation timestamp (UTC).</summary>
		public DateTime TimeCreated { get; set; }

		/// <summary>
		/// When the next tax payment falls due, or null while the plot is unowned.
		/// </summary>
		/// <remarks>
		/// The only tax state stored. Everything else is derived from it: the plot is delinquent
		/// once this is in the past, and reclaimable once it is further in the past than the grace
		/// period. A stored "is delinquent" flag would be a second source of truth that could
		/// disagree with the date beside it.
		///
		/// <para>Also the concurrency pin. Charging advances this date and requires it to still hold
		/// its old value, so several scene servers sweeping the same world produce exactly one
		/// charge per period rather than one per server.</para>
		/// </remarks>
		public DateTime? TaxDueUtc { get; set; }

		/// <summary>
		/// When the owner first failed to pay, or null while they are up to date.
		/// </summary>
		/// <remarks>
		/// Stored rather than derived, and the reason is the pin above. <see cref="TaxDueUtc"/> has
		/// to advance on every billing attempt — that is what stops two servers charging the same
		/// period — so it advances whether or not the money was actually collected. A plot that
		/// never pays would therefore keep a due date marching into the future and never look
		/// overdue by more than one period, which is to say it would never be reclaimed at all.
		///
		/// <para>This is the date the grace period is measured from: set on the first missed
		/// payment, left alone on later ones so the clock does not restart, and cleared the moment
		/// a payment succeeds.</para>
		/// </remarks>
		public DateTime? TaxDelinquentSinceUtc { get; set; }

		/// <summary>
		/// Where the plot is in its lifecycle.
		/// </summary>
		/// <remarks>
		/// Stored rather than derived from the owner columns, because they cannot tell the two
		/// unowned states apart. Land that has never been claimed and land somebody stopped paying
		/// for both read as owner zero, and they are not the same place: one is a bare lot, the
		/// other is a house standing empty with its contents in a vault. A channel loading the scene
		/// renders them differently and a player walking up to one is told a different thing.
		///
		/// <para>The values are <c>FishMMO.Shared.PlotState</c>, stored as its underlying integer —
		/// this assembly cannot reference the Unity shared assembly, so the mapping is a cast.
		/// <c>PlotStateParityTests</c> pins the pairing; do not renumber either side.</para>
		/// </remarks>
		public int State { get; set; }

		/// <summary>
		/// When the current owner claimed the plot, or null while it is unclaimed.
		/// </summary>
		/// <remarks>
		/// Cleared on release rather than kept, so it always describes the ownership recorded
		/// alongside it instead of some previous owner's tenancy.
		/// </remarks>
		public DateTime? TimeClaimed { get; set; }
	}
}
