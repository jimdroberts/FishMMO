using System;

namespace FishMMO.Database.Data
{
	/// <summary>
	/// Character mail data transfer object.
	/// </summary>
	public struct CharacterMailData : IVersioned<CharacterMailData>
	{
		/// <summary>Primary key.</summary>
		public readonly long ID;
		/// <summary>Optimistic concurrency version.</summary>
		public readonly long Version;
		/// <summary>Recipient character ID.</summary>
		public readonly long CharacterID;
		/// <summary>Sending character ID.</summary>
		public readonly long SenderID;
		/// <summary>Display name of the sender.</summary>
		public readonly string SenderName;
		/// <summary>Mail subject line.</summary>
		public readonly string Subject;
		/// <summary>Mail body text.</summary>
		public readonly string Body;
		/// <summary>Timestamp when mail was sent.</summary>
		public readonly DateTime TimeSent;
		/// <summary>Whether the mail has been read.</summary>
		public readonly bool Read;
		/// <summary>Attached currency amount.</summary>
		public readonly int CurrencyAttachment;
		/// <summary>Attached item template ID.</summary>
		public readonly int ItemAttachmentTemplateID;
		/// <summary>Attached item randomization seed.</summary>
		public readonly int ItemAttachmentSeed;
		/// <summary>Attached item stack amount.</summary>
		public readonly int ItemAttachmentAmount;

		long IVersioned<CharacterMailData>.Version => Version;

		public CharacterMailData(long id, long characterID, long senderID, string senderName, string subject, string body, DateTime timeSent, bool read, int currencyAttachment, int itemAttachmentTemplateID, int itemAttachmentSeed, int itemAttachmentAmount)
			: this(id, version: 0, characterID, senderID, senderName, subject, body, timeSent, read, currencyAttachment, itemAttachmentTemplateID, itemAttachmentSeed, itemAttachmentAmount)
		{
		}

		public CharacterMailData(long id, long version, long characterID, long senderID, string senderName, string subject, string body, DateTime timeSent, bool read, int currencyAttachment, int itemAttachmentTemplateID, int itemAttachmentSeed, int itemAttachmentAmount)
		{
			ID = id;
			Version = version;
			CharacterID = characterID;
			SenderID = senderID;
			SenderName = senderName;
			Subject = subject;
			Body = body;
			TimeSent = timeSent;
			Read = read;
			CurrencyAttachment = currencyAttachment;
			ItemAttachmentTemplateID = itemAttachmentTemplateID;
			ItemAttachmentSeed = itemAttachmentSeed;
			ItemAttachmentAmount = itemAttachmentAmount;
		}

		public CharacterMailData WithVersion(long newVersion)
		{
			return new CharacterMailData(ID, newVersion, CharacterID, SenderID, SenderName, Subject, Body, TimeSent, Read, CurrencyAttachment, ItemAttachmentTemplateID, ItemAttachmentSeed, ItemAttachmentAmount);
		}
	}
}