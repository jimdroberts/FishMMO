namespace FishMMO.Database.Data
{
	/// <summary>
	/// The attachment taken off one mail by a successful claim.
	/// </summary>
	/// <remarks>
	/// Returned by <c>ICharacterMailService.ClaimAttachmentAsync</c>, which reads and clears the
	/// attachment columns in a single statement. These are the values as they stood <em>before</em>
	/// the clear — the whole point of the call, since after it the row holds zeroes.
	/// <para>
	/// A claim that finds nothing to take returns no data at all rather than a zeroed instance, so
	/// "there was an attachment worth nothing" and "there was no attachment" cannot be confused.
	/// </para>
	/// </remarks>
	public struct CharacterMailAttachmentData
	{
		/// <summary>Template ID of the attached item, or 0 when only currency was attached.</summary>
		public readonly int ItemTemplateID;

		/// <summary>Generation seed of the attached item.</summary>
		public readonly int ItemSeed;

		/// <summary>Stack size of the attached item.</summary>
		public readonly uint ItemAmount;

		/// <summary>Currency attached to the mail.</summary>
		public readonly int CurrencyAmount;

		/// <summary>
		/// Initializes a claimed attachment.
		/// </summary>
		/// <param name="itemTemplateID">Template ID of the attached item.</param>
		/// <param name="itemSeed">Generation seed of the attached item.</param>
		/// <param name="itemAmount">Stack size of the attached item.</param>
		/// <param name="currencyAmount">Currency attached to the mail.</param>
		public CharacterMailAttachmentData(int itemTemplateID, int itemSeed, uint itemAmount, int currencyAmount)
		{
			ItemTemplateID = itemTemplateID;
			ItemSeed = itemSeed;
			ItemAmount = itemAmount;
			CurrencyAmount = currencyAmount;
		}

		/// <summary>True when this attachment carries an item or any currency.</summary>
		public bool HasAnything => (ItemTemplateID != 0 && ItemAmount > 0) || CurrencyAmount > 0;
	}
}
