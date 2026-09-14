using System;
using System.Collections.Generic;

namespace FishMMO.Database.Data
{
	/// <summary>One beta code, as staff see it.</summary>
	public sealed class BetaCodeData
	{
		/// <summary>Surrogate key.</summary>
		public long ID { get; set; }

		/// <summary>
		/// The row's <c>xmin</c>, widened to <see cref="long"/>.
		/// </summary>
		/// <remarks>
		/// Carried as a long rather than the entity's <c>uint</c> because Npgsql cannot bind a
		/// <c>uint</c> parameter at all; a DTO field of that type is a runtime failure waiting for
		/// the first caller that passes it back into raw SQL.
		/// </remarks>
		public long Version { get; set; }

		/// <summary>The code, in <c>XXXX-XXXX-XXXX</c> form.</summary>
		public string Code { get; set; }

		/// <summary>The program the code admits to, lowercase.</summary>
		public string Program { get; set; }

		/// <summary>How many accounts may redeem the code.</summary>
		public int MaxUses { get; set; }

		/// <summary>How many accounts have redeemed it.</summary>
		public int UseCount { get; set; }

		/// <summary>When the code stops being redeemable, or null for never.</summary>
		public DateTime? ExpiresUtc { get; set; }

		/// <summary>When staff revoked it, or null.</summary>
		public DateTime? RevokedUtc { get; set; }

		/// <summary>The staff account that revoked it.</summary>
		public string? RevokedBy { get; set; }

		/// <summary>The staff account that minted it.</summary>
		public string CreatedBy { get; set; }

		/// <summary>When it was minted.</summary>
		public DateTime CreatedUtc { get; set; }

		/// <summary>Staff note, such as who the batch was handed to.</summary>
		public string? Note { get; set; }

		/// <summary>Whether <see cref="RevokedUtc"/> is set.</summary>
		public bool IsRevoked { get; set; }

		/// <summary>Whether the expiry had passed when the row was read.</summary>
		/// <remarks>
		/// Computed from the reader's clock at read time, not stored: an expiry is a moment, and a
		/// stored flag would be wrong from that moment until something rewrote it.
		/// </remarks>
		public bool IsExpired { get; set; }

		/// <summary><see cref="MaxUses"/> minus <see cref="UseCount"/>, never negative.</summary>
		public int RemainingUses { get; set; }
	}

	/// <summary>One account's permanent link to a code it redeemed.</summary>
	public sealed class AccountBetaCodeData
	{
		/// <summary>The account, lowercase.</summary>
		public string AccountName { get; set; }

		/// <summary>The redeemed code's id.</summary>
		public long BetaCodeID { get; set; }

		/// <summary>The redeemed code.</summary>
		public string Code { get; set; }

		/// <summary>The program it admitted the account to.</summary>
		public string Program { get; set; }

		/// <summary>When the account redeemed it.</summary>
		public DateTime RedeemedUtc { get; set; }

		/// <summary>
		/// Whether the code has since been revoked, which ends the access it granted.
		/// </summary>
		public bool CodeRevoked { get; set; }
	}

	/// <summary>Filters for a beta code search.</summary>
	public sealed class BetaCodeQuery
	{
		/// <summary>Only codes of this program. Null means every program.</summary>
		public string? Program { get; set; }

		/// <summary>Whether revoked codes are included. Defaults to true.</summary>
		public bool IncludeRevoked { get; set; } = true;

		/// <summary>1-based page number.</summary>
		public int Page { get; set; } = 1;

		/// <summary>Rows per page. Clamped by the service.</summary>
		public int PageSize { get; set; } = 50;
	}

	/// <summary>One page of beta codes.</summary>
	public sealed class BetaCodePage
	{
		/// <summary>The rows on this page, newest first.</summary>
		public IReadOnlyList<BetaCodeData> Items { get; set; } = Array.Empty<BetaCodeData>();

		/// <summary>1-based page number.</summary>
		public int Page { get; set; }

		/// <summary>Rows per page, after clamping.</summary>
		public int PageSize { get; set; }

		/// <summary>Total rows matching the filter.</summary>
		public int TotalCount { get; set; }
	}

	/// <summary>Totals for one beta program.</summary>
	public sealed class BetaProgramSummary
	{
		/// <summary>The program name.</summary>
		public string Program { get; set; }

		/// <summary>Every code ever minted under it, revoked included.</summary>
		public int CodeCount { get; set; }

		/// <summary>
		/// Codes that could still be redeemed now: not revoked, not expired, uses remaining.
		/// </summary>
		public int ActiveCodeCount { get; set; }

		/// <summary>
		/// Account links to the program's codes — how many admissions it has granted.
		/// </summary>
		/// <remarks>
		/// Counted from <c>account_beta_codes</c>, not summed from <c>use_count</c>. The two agree
		/// by construction, but the links are the evidence and the counter is only the gate.
		/// </remarks>
		public int RedemptionCount { get; set; }
	}
}
