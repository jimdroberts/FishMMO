using System;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// The permanent record that an account redeemed a beta code.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A row is written once, in the same transaction that takes a use from the code, and is never
	/// updated or deleted. Whether the account still has access is decided at read time from the
	/// code it points at: a revoked code ends it, an expired one does not.
	/// </para>
	/// <para>
	/// <b>No foreign keys, deliberately.</b> The link is evidence of who was admitted to which
	/// program, and it has to outlive anything that might happen to either side. Codes are revoked,
	/// never deleted, and accounts are never deleted in this system, so a cascade could never fire
	/// in normal operation — its only possible effect would be to erase the evidence when somebody
	/// removed a row by hand, which is precisely the case the record exists for.
	/// </para>
	/// <para>
	/// <see cref="Code"/> and <see cref="Program"/> are copied from the code at redemption. Neither
	/// ever changes after a code is minted, so the copies cannot drift, and a staff lookup of "who
	/// is in closed beta" reads one table. Access checks still join to the code, because
	/// revocation lives there.
	/// </para>
	/// </remarks>
	public class AccountBetaCodeEntity
	{
		/// <summary>Surrogate key.</summary>
		public long ID { get; set; }

		/// <summary>The account, matching <c>accounts.name</c>, stored lowercase.</summary>
		public string AccountName { get; set; }

		/// <summary>The redeemed code's id.</summary>
		public long BetaCodeID { get; set; }

		/// <summary>The redeemed code, copied at redemption.</summary>
		public string Code { get; set; }

		/// <summary>The code's program, copied at redemption.</summary>
		public string Program { get; set; }

		/// <summary>When the account redeemed it.</summary>
		public DateTime RedeemedUtc { get; set; }
	}
}
