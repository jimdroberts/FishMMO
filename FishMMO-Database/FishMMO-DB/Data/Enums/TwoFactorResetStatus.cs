namespace FishMMO.Database.Data.Enums
{
	/// <summary>
	/// Where a delayed two-factor reset request has got to.
	/// </summary>
	/// <remarks>
	/// Stored as the integer. <see cref="Pending"/> is 0 because it is the column default and the
	/// value the one-pending-request-per-account partial index is filtered on (<c>status = 0</c>);
	/// renumbering it would silently change what that index constrains.
	/// </remarks>
	public enum TwoFactorResetStatus : int
	{
		/// <summary>Waiting for its effective time, or effective and not yet used.</summary>
		Pending = 0,

		/// <summary>
		/// Withdrawn — by staff, or by the account signing in normally, which proves the owner
		/// still has their second factor.
		/// </summary>
		Cancelled = 1,

		/// <summary>The account used it after it took effect.</summary>
		Completed = 2,
	}
}
