using System;

namespace FishMMO.Database.Data
{
	/// <summary>
	/// Plot data transfer object.
	/// </summary>
	/// <remarks>
	/// Carries ownership as the two columns the row stores rather than as a resolved owner, because
	/// this assembly cannot reference the shared types where that resolution lives. Callers turn the
	/// pair into a single answer with <c>FishMMO.Shared.PlotOwner.TryFromColumns</c>, which is also
	/// where a row holding both is caught.
	/// </remarks>
	public struct PlotData
	{
		/// <summary>Primary key.</summary>
		public readonly long ID;

		/// <summary>The world server this land belongs to.</summary>
		public readonly long WorldServerID;

		/// <summary>The Unity scene the plot's foundation is authored in.</summary>
		public readonly string SceneName;

		/// <summary>The designer-authored key identifying the foundation within its scene.</summary>
		public readonly string PlotKey;

		/// <summary>The owning character, or zero.</summary>
		public readonly long OwnerCharacterID;

		/// <summary>The owning guild, or zero.</summary>
		public readonly long OwnerGuildID;

		/// <summary>
		/// Where the plot is in its lifecycle, as a <c>FishMMO.Shared.PlotState</c> value.
		/// </summary>
		/// <remarks>
		/// An integer for the same reason the owner arrives as two columns: this assembly cannot
		/// reference the shared enum. Callers cast, and <c>PlotStateParityTests</c> pins the pairing.
		/// </remarks>
		public readonly int State;

		/// <summary>When the current owner claimed the plot, or null while unclaimed.</summary>
		public readonly DateTime? TimeClaimed;

		/// <summary>When the next tax payment falls due, or null while unowned.</summary>
		public readonly DateTime? TaxDueUtc;

		/// <summary>When the owner first failed to pay, or null while up to date.</summary>
		public readonly DateTime? TaxDelinquentSinceUtc;

		public PlotData(long id, long worldServerID, string sceneName, string plotKey, long ownerCharacterID, long ownerGuildID, DateTime? timeClaimed, DateTime? taxDueUtc = null, DateTime? taxDelinquentSinceUtc = null, int state = 0)
		{
			ID = id;
			WorldServerID = worldServerID;
			SceneName = sceneName;
			PlotKey = plotKey;
			OwnerCharacterID = ownerCharacterID;
			OwnerGuildID = ownerGuildID;
			TimeClaimed = timeClaimed;
			TaxDueUtc = taxDueUtc;
			TaxDelinquentSinceUtc = taxDelinquentSinceUtc;
			State = state;
		}
	}
}
