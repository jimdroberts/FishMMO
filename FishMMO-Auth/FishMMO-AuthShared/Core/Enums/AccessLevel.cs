namespace FishMMO.Auth.Core
{
	/// <summary>
	/// Account and character access levels.
	/// Values must stay in sync with FishMMO.Database.Data.Enums.AccessLevel.
	///
	/// WARNING: If you add, remove, or reorder a value here you MUST update
	/// the corresponding enum in the database layer and run a migration.
	/// Consider adding a unit test that asserts every member of this enum
	/// matches the database enum (e.g. via a SELECT DISTINCT query or a
	/// checked-in mapping file) so the mismatch is caught at CI time rather
	/// than at runtime.
	/// </summary>
	public enum AccessLevel : byte
	{
		/// <summary>
		/// Banned user - no access.
		/// </summary>
		Banned = 0,

		/// <summary>
		/// Normal player access.
		/// </summary>
		Player = 1,

		/// <summary>
		/// Game master access.
		/// </summary>
		GameMaster = 2,

		/// <summary>
		/// Administrator access.
		/// </summary>
		Admin = 3,
	}
}