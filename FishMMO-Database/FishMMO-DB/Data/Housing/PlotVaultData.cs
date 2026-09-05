using System;

namespace FishMMO.Database.Data
{
	/// <summary>
	/// House vault entry data transfer object.
	/// </summary>
	/// <remarks>
	/// Self-describing on purpose: <see cref="BaseFee"/> and <see cref="FeeRatePerDay"/> travel with
	/// the row so what it costs to retrieve is answerable from this and the clock alone, with no
	/// template loaded and no server configuration consulted. A player quoted a price when their
	/// house was taken should be charged that price, not whatever the rates happen to be today.
	/// </remarks>
	public struct PlotVaultData
	{
		/// <summary>Primary key.</summary>
		public readonly long ID;

		/// <summary>The character whose vault holds it.</summary>
		public readonly long CharacterID;

		/// <summary>The structure template stored.</summary>
		public readonly int TemplateID;

		/// <summary>How many are held.</summary>
		public readonly int Amount;

		/// <summary>The plot it came off.</summary>
		public readonly long OriginalPlotID;

		/// <summary>When it was stored (UTC).</summary>
		public readonly DateTime StoredAtUtc;

		/// <summary>The fee charged before any time had passed.</summary>
		public readonly long BaseFee;

		/// <summary>How much of the base fee is added per day stored.</summary>
		public readonly float FeeRatePerDay;

		public PlotVaultData(long id, long characterID, int templateID, int amount, long originalPlotID, DateTime storedAtUtc, long baseFee, float feeRatePerDay)
		{
			ID = id;
			CharacterID = characterID;
			TemplateID = templateID;
			Amount = amount;
			OriginalPlotID = originalPlotID;
			StoredAtUtc = storedAtUtc;
			BaseFee = baseFee;
			FeeRatePerDay = feeRatePerDay;
		}
	}
}
