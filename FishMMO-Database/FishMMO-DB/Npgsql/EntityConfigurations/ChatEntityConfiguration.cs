using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Entity configuration for ChatEntity with explicit indexes and constraints.
	/// </summary>
	public class ChatEntityConfiguration : IEntityTypeConfiguration<ChatEntity>
	{
		public void Configure(EntityTypeBuilder<ChatEntity> builder)
		{
			// Primary Key
			builder.HasKey(e => e.ID);

			builder.Property(e => e.ID)
				.ValueGeneratedOnAdd();

			// Required fields
			builder.Property(e => e.CharacterID)
				.IsRequired();

			builder.Property(e => e.CharacterName)
				.IsRequired()
				.HasMaxLength(50);

			builder.Property(e => e.AccountName)
				.IsRequired()
				.HasMaxLength(50);

			builder.Property(e => e.WorldServerID)
				.IsRequired();

			builder.Property(e => e.SceneServerID)
				.IsRequired();

			builder.Property(e => e.ServerReceivedTime)
				.IsRequired();

			builder.Property(e => e.TimeCreated)
				.IsRequired()
				.HasDefaultValueSql("CURRENT_TIMESTAMP");

			builder.Property(e => e.Channel)
				.IsRequired();

			builder.Property(e => e.Message)
				.IsRequired()
				.HasMaxLength(4000);

			// Performance indexes for chat queries
			builder.HasIndex(e => e.WorldServerID)
				.HasDatabaseName("IX_Chat_WorldServerID");

			builder.HasIndex(e => e.TimeCreated)
				.HasDatabaseName("IX_Chat_TimeCreated");

			// Composite index for character chat history
			builder.HasIndex(e => new { e.CharacterID, e.TimeCreated })
				.HasDatabaseName("IX_Chat_CharacterID_TimeCreated");

			// Composite index for chat pagination (FetchAsync hot path)
			// Covers WHERE time_created >= @lastFetch AND id > @lastPosition ORDER BY time_created, id
			builder.HasIndex(e => new { e.TimeCreated, e.ID })
				.HasDatabaseName("IX_Chat_TimeCreated_ID");

			// Composite index for scene-server local channel filtering (FetchAsync hot path)
			// Covers WHERE scene_server_id = @sceneServerId filtering by channel
			// Used to exclude local messages: localChannels.Contains(c.Channel) && c.SceneServerID == sceneServerId
			builder.HasIndex(e => new { e.SceneServerID, e.Channel })
				.HasDatabaseName("IX_Chat_SceneServerID_Channel");
		}
	}
}