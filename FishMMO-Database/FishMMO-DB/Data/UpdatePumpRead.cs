using System;
using System.Collections.Generic;

namespace FishMMO.Database.Data
{
	/// <summary>
	/// One read of a party or guild update pump: the rows, and the database's own clock when the
	/// read began.
	/// </summary>
	/// <remarks>
	/// The rows are stamped by the database, so the pump's next mark is taken from the database's
	/// clock too. A mark stamped by the scene server's clock lost updates whenever that clock ran
	/// ahead of the database's by more than the pump's allowance.
	/// </remarks>
	public readonly struct UpdatePumpRead<T>
	{
		/// <summary>The update rows at or after the requested mark.</summary>
		public readonly List<T> Updates;

		/// <summary>The database's UTC clock, read before the rows.</summary>
		public readonly DateTime ReadStartedUtc;

		public UpdatePumpRead(List<T> updates, DateTime readStartedUtc)
		{
			Updates = updates;
			ReadStartedUtc = readStartedUtc;
		}
	}
}
