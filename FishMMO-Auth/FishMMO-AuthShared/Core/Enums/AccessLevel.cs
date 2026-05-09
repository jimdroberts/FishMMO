namespace FishMMO.Auth.Core
{
	/// <summary>
	/// Account and character access levels.
	/// Values must stay in sync with FishMMO.Database.Data.Enums.AccessLevel.
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