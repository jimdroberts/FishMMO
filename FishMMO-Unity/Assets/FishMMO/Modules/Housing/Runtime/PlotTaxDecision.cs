using System;

namespace FishMMO.Shared
{
	/// <summary>
	/// What a tax sweep should do with one plot that has come due.
	/// </summary>
	public enum PlotTaxAction
	{
		/// <summary>Leave it alone.</summary>
		None = 0,

		/// <summary>Take the plot back and clear what was built on it.</summary>
		Reclaim = 1,

		/// <summary>Charge the owning character.</summary>
		Charge = 2,

		/// <summary>Move the due date on without charging anything.</summary>
		Defer = 3,
	}

	/// <summary>
	/// Decides what a tax sweep does with a plot, separately from doing it.
	/// </summary>
	/// <remarks>
	/// Pulled out of the sweep because this is where the mistakes are. The rule it encodes is not
	/// obvious, and getting it wrong fails silently in a direction nobody notices: an owner who
	/// never pays simply keeps their land forever, which looks exactly like an owner who does pay.
	///
	/// <para>The rule is that <b>grace runs from the first missed payment</b>, not from the current
	/// due date. The due date has to advance on every billing <em>attempt</em> — that is the pin
	/// which stops two scene servers charging the same period — so it moves whether or not any money
	/// was collected. Measuring grace against it would mean a plot never looked more than one period
	/// overdue, and nothing would ever be reclaimed.</para>
	/// </remarks>
	public static class PlotTaxDecision
	{
		/// <summary>
		/// Decides what to do with a plot whose tax has fallen due.
		/// </summary>
		/// <param name="ownerCharacterID">The owning character, or zero.</param>
		/// <param name="ownerGuildID">The owning guild, or zero.</param>
		/// <param name="delinquentSinceUtc">When the owner first missed a payment, or null.</param>
		/// <param name="nowUtc">The moment being judged.</param>
		/// <param name="grace">How long an overdue plot is kept before being taken back.</param>
		public static PlotTaxAction Decide(
			long ownerCharacterID,
			long ownerGuildID,
			DateTime? delinquentSinceUtc,
			DateTime nowUtc,
			TimeSpan grace)
		{
			/* Grace is judged before anything else, including before the owner is identified. A plot
			 * past its grace is not owed more tax — it is owed nothing, because it is about to stop
			 * being theirs, and charging first would take a final payment for land the same sweep
			 * then confiscates. */
			if (delinquentSinceUtc.HasValue && nowUtc - delinquentSinceUtc.Value >= grace)
			{
				return PlotTaxAction.Reclaim;
			}

			/* Guild land is deferred rather than charged. Guilds have no balance, so collecting would
			 * mean billing some member personally for land they do not own — and simply letting it
			 * run out of grace would confiscate every guild plot on the server. Neither is
			 * acceptable, so it is untaxed until a treasury exists; the date still moves so it does
			 * not resweep on every pass. */
			if (ownerGuildID != 0)
			{
				return PlotTaxAction.Defer;
			}

			if (ownerCharacterID <= 0)
			{
				// Unowned land is not taxed. Clearing its due date belongs to the release path.
				return PlotTaxAction.None;
			}

			return PlotTaxAction.Charge;
		}
	}
}
