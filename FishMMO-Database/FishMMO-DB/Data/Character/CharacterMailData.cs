using System;

namespace FishMMO.Database.Data
{
	/// <summary>
	/// Character mail data transfer object.
	/// </summary>
	public struct CharacterMailData
	{
		public long ID { get; set; }
		public long CharacterID { get; set; }
		public long SenderID { get; set; }
		public string SenderName { get; set; }
		public string Subject { get; set; }
		public string Body { get; set; }
		public DateTime TimeSent { get; set; }
		public bool Read { get; set; }
		public int CurrencyAttachment { get; set; }
		public int ItemAttachment { get; set; }
	}
}