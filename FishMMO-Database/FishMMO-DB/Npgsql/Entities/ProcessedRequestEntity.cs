using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace FishMMO.Database.Npgsql.Entities
{
	[Table("processed_requests")]
	public sealed class ProcessedRequestEntity
	{
		public Guid RequestID { get; set; }
		public long AccountID { get; set; }
		public string OperationName { get; set; }
		public byte Status { get; set; }
		public string Response { get; set; }
		public string ErrorCode { get; set; }
		public string ErrorMessage { get; set; }
		public DateTime CreatedAt { get; set; }
		public DateTime? CompletedAt { get; set; }
	}
}