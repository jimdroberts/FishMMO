using UnityEngine;

namespace FishMMO.Client
{
	/// <summary>What the Staff and Support module adds to the client's menus: "Report" on every character menu.</summary>
	public static class StaffClient
	{
		/// <summary>The entry's id in <see cref="CharacterMenuEntries"/>.</summary>
		public const string ReportEntryId = "staff.report";

		/// <summary>Last, where it always was.</summary>
		public const int ReportOrder = 900;

		/// <summary>Registers the entry. Runs before the first scene loads; safe to run again.</summary>
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		public static void Install()
		{
			CharacterMenuEntries.Register(ReportEntryId, "Report", ReportOrder,
				target => UITKReportPlayer.TryOpen(target.CharacterID, target.Name),
				// The guild roster never offered it; every other menu did.
				target => target.Source != CharacterMenuSource.Guild);
		}
	}
}
