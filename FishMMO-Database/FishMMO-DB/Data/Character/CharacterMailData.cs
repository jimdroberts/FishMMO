using System;

namespace FishMMO.Database.Data
{
	/// <summary>
	/// Character mail data transfer object.
	/// </summary>
	public struct CharacterMailData : IVersioned<CharacterMailData>
	{
		public readonly long ID;
		public readonly long Version;
		public readonly long CharacterID;
		public readonly long SenderID;
		public readonly string SenderName;
		public readonly string Subject;
		public readonly string Body;
		public readonly DateTime TimeSent;
		public readonly bool Read;
		public readonly int CurrencyAttachment;
		public readonly int ItemAttachmentTemplateID;
		public readonly int ItemAttachmentSeed;
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