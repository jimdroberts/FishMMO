using System;
using System.Collections.Generic;

namespace FishMMO.Database.Data
{
	/// <summary>
	/// One poll of <c>plot_updates</c>: the marks it found, and the database time it was taken at.
	/// </summary>
	/// <remarks>
	/// The time is the database's, read before the marks were, because the marks are stamped by the
	/// database clock and the next poll's window has to be measured on the same clock. A poller that
	/// measured it on its own clock lost every change stamped inside however far that clock ran
	/// ahead of the database's.
	/// </remarks>
	public struct PlotUpdatePollData
	{
		/// <summary>The marks found, one per changed plot.</summary>
		public readonly List<PlotUpdateData> Updates;

		/// <summary>
		/// The database clock (UTC) just before the marks were read. Every mark stamped at or after
		/// this is certain to be found again by a poll starting from it.
		/// </summary>
		public readonly DateTime AsOfUtc;

		public PlotUpdatePollData(List<PlotUpdateData> updates, DateTime asOfUtc)
		{
			Updates = updates;
			AsOfUtc = asOfUtc;
		}
	}
}
