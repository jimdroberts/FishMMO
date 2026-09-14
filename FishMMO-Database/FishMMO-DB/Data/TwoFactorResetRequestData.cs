using System;
using System.Collections.Generic;
using FishMMO.Database.Data.Enums;

namespace FishMMO.Database.Data
{
	/// <summary>A delayed two-factor reset request.</summary>
	public sealed class TwoFactorResetRequestData
	{
		/// <summary>Surrogate key.</summary>
		public long ID { get; set; }

		/// <summary>
		/// The row's <c>xmin</c>, widened to <see cref="long"/> because Npgsql cannot bind a
		/// <c>uint</c>.
		/// </summary>
		public long Version { get; set; }

		/// <summary>The account, lowercase.</summary>
		public string AccountName { get; set; }

		/// <summary>Where the request has got to.</summary>
		public TwoFactorResetStatus Status { get; set; }

		/// <summary>When it was asked for.</summary>
		public DateTime RequestedUtc { get; set; }

		/// <summary>When it may be completed.</summary>
		public DateTime EffectiveUtc { get; set; }

		/// <summary>Client address that asked, or null.</summary>
		public string? RequestedIp { get; set; }

		/// <summary>When it was cancelled or completed.</summary>
		public DateTime? ResolvedUtc { get; set; }

		/// <summary>Who cancelled or completed it: a staff account, or the account itself.</summary>
		public string? ResolvedBy { get; set; }

		/// <summary>When staff last brought it forward.</summary>
		public DateTime? ShortenedUtc { get; set; }

		/// <summary>The staff account that last brought it forward.</summary>
		public string? ShortenedBy { get; set; }

		/// <summary>Why staff last shortened or cancelled it.</summary>
		public string? StaffReason { get; set; }

		/// <summary>
		/// Whether it was pending and past its effective time when read.
		/// </summary>
		/// <remarks>
		/// Computed from the reader's clock, not stored, for the same reason an expiry flag is not
		/// stored. It is advisory: <c>CompleteAsync</c> re-tests both conditions in its own UPDATE,
		/// and that is the only check that decides anything.
		/// </remarks>
		public bool IsEffective { get; set; }
	}

	/// <summary>One page of reset requests.</summary>
	public sealed class TwoFactorResetRequestPage
	{
		/// <summary>The rows on this page.</summary>
		public IReadOnlyList<TwoFactorResetRequestData> Items { get; set; } = Array.Empty<TwoFactorResetRequestData>();

		/// <summary>1-based page number.</summary>
		public int Page { get; set; }

		/// <summary>Rows per page, after clamping.</summary>
		public int PageSize { get; set; }

		/// <summary>Total rows matching.</summary>
		public int TotalCount { get; set; }
	}
}
