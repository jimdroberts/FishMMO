using System;

namespace FishMMO.Database.Data
{
	/// <summary>
	/// Character mail data transfer object.
	/// </summary>
	public struct CharacterMailData
	{
		public readonly long ID;
		public readonly long CharacterID;
		public readonly long SenderID;
		public readonly string SenderName;
		public readonly string Subject;
		public readonly string Body;
		public readonly DateTime TimeSent;
		public readonly bool Read;
		public readonly int CurrencyAttachment;
		public readonly int ItemAttachment;

		public CharacterMailData(long id, long characterID, long senderID, string senderName, string subject, string body, DateTime timeSent, bool read, int currencyAttachment, int itemAttachment)
		{
			ID = id;
			CharacterID = characterID;
			SenderID = senderID;
			SenderName = senderName;
			Subject = subject;
			Body = body;
			TimeSent = timeSent;
			Read = read;
			CurrencyAttachment = currencyAttachment;
			ItemAttachment = itemAttachment;
		}
	}
}