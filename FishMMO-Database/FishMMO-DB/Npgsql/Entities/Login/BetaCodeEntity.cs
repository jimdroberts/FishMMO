using System;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// A beta code: one admission ticket, possibly multi-use, to a named test program.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Codes are minted in batches under a program (<c>alpha</c>, <c>closed-beta</c>, a private
	/// test). While beta mode is on, only an account linked to a live code of an active program may
	/// register or sign in; see <see cref="AccountBetaCodeEntity"/> for the link.
	/// </para>
	/// <para>
	/// <b>Codes are revoked, never deleted.</b> A code row is what every account link points at,
	/// and deleting one would leave links recording an admission to a program nobody can look up.
	/// Revocation keeps the row and ends the access it granted.
	/// </para>
	/// <para>
	/// Redemption does not rely on the <c>xmin</c> token. It is one conditional UPDATE —
	/// <c>use_count &lt; max_uses</c>, not revoked, not expired — whose WHERE clause is the
	/// concurrency control, so two accounts racing for the last use cannot both get it. The token
	/// is there for staff edits made through EF, which would otherwise overwrite a redemption that
	/// landed between their read and their save.
	/// </para>
	/// </remarks>
	public class BetaCodeEntity
	{
		/// <summary>Surrogate key.</summary>
		public long ID { get; set; }

		/// <summary>PostgreSQL <c>xmin</c> concurrency token.</summary>
		public uint Version { get; set; }

		/// <summary>The code in <c>XXXX-XXXX-XXXX</c> form. Unique.</summary>
		public string Code { get; set; }

		/// <summary>The program the code admits to, lowercase.</summary>
		public string Program { get; set; }

		/// <summary>How many distinct accounts may redeem the code.</summary>
		public int MaxUses { get; set; }

		/// <summary>
		/// How many accounts have redeemed it. Only ever moved by the conditional redemption UPDATE.
		/// </summary>
		public int UseCount { get; set; }

		/// <summary>
		/// When the code stops being redeemable, or null for never.
		/// </summary>
		/// <remarks>
		/// Expiry ends redemption, not access: an account that redeemed the code before it expired
		/// keeps its place in the program. A tester admitted in March must not be locked out in
		/// April because the batch they were handed had a deadline for signing up.
		/// </remarks>
		public DateTime? ExpiresUtc { get; set; }

		/// <summary>
		/// When staff revoked the code, or null. Revocation ends redemption and access alike.
		/// </summary>
		public DateTime? RevokedUtc { get; set; }

		/// <summary>The staff account that revoked it.</summary>
		public string? RevokedBy { get; set; }

		/// <summary>The staff account that minted it.</summary>
		public string CreatedBy { get; set; }

		/// <summary>When it was minted.</summary>
		public DateTime CreatedUtc { get; set; }

		/// <summary>Free-text staff note, such as where the batch was handed out.</summary>
		public string? Note { get; set; }
	}
}
