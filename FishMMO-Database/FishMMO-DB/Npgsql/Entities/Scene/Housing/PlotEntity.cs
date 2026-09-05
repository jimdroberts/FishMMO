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
		/// When the current owner claimed the plot, or null while it is unclaimed.
		/// </summary>
		/// <remarks>
		/// Cleared on release rather than kept, so it always describes the ownership recorded
		/// alongside it instead of some previous owner's tenancy.
		/// </remarks>
		public DateTime? TimeClaimed { get; set; }
	}
}
