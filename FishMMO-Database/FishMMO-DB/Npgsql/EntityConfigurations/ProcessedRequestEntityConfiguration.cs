using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Entity configuration for ProcessedRequestEntity (idempotency tracking).
	/// </summary>
	public sealed class ProcessedRequestEntityConfiguration : IEntityTypeConfiguration<ProcessedRequestEntity>
	{
		public void Configure(EntityTypeBuilder<ProcessedRequestEntity> builder)
		{
			builder.HasKey(e => e.RequestID);

			builder.Property(e => e.RequestID)
				.IsRequired()
				.HasColumnName("request_id");

			builder.Property(e => e.AccountID)
				.IsRequired()
				.HasColumnName("account_id");

			builder.Property(e => e.OperationName)
				.IsRequired()
				.HasMaxLength(64)
				.HasColumnName("operation_name");

			builder.Property(e => e.Status)
				.IsRequired()
				.HasDefaultValue((byte)0)
				.HasColumnName("status");

			builder.Property(e => e.Response)
				.HasColumnType("jsonb")
				.HasColumnName("response");

			builder.Property(e => e.ErrorCode)
				.HasMaxLength(64)
				.HasColumnName("error_code");

			builder.Property(e => e.ErrorMessage)
				.HasMaxLength(256)
				.HasColumnName("error_message");

			builder.Property(e => e.TimeCreated)
				.IsRequired()
				.HasDefaultValueSql("CURRENT_TIMESTAMP")
				.HasColumnName("created_at");

			builder.Property(e => e.CompletedAt)
				.HasColumnName("completed_at");

			builder.HasIndex(e => e.TimeCreated)
				.HasDatabaseName("IX_ProcessedRequests_CreatedAt");

			builder.HasIndex(e => e.AccountID)
				.HasDatabaseName("IX_ProcessedRequests_AccountID");

			builder.HasIndex(e => e.Status)
				.HasDatabaseName("IX_ProcessedRequests_Status");

			builder.HasIndex(e => new { e.AccountID, e.TimeCreated })
				.HasDatabaseName("IX_ProcessedRequests_AccountID_CreatedAt");
		}
	}
}