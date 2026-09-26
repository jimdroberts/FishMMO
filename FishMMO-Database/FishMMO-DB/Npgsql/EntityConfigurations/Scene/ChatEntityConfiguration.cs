using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Entity configuration for ChatEntity with explicit indexes and constraints.
	/// </summary>
	public class ChatEntityConfiguration : IEntityTypeConfiguration<ChatEntity>
	{
		/// <inheritdoc/>
		public void Configure(EntityTypeBuilder<ChatEntity> builder)
		{
			builder.ToTable("chat");

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

			builder.Property(e => e.Channel)
				.IsRequired();

			builder.Property(e => e.Message)
				.IsRequired()
				.HasMaxLength(4000);

			// Performance indexes for chat queries
			builder.HasIndex(e => e.WorldServerID);

			builder.HasIndex(e => e.TimeCreated);

			// Composite index for character chat history
			builder.HasIndex(e => new { e.CharacterID, e.TimeCreated });

			// Composite index for the chat pump (ChatService.FetchPumpAsync hot path)
			// Covers WHERE time_created >= @windowStart [AND (time_created, id) > (@t, @id)] ORDER BY time_created, id
			builder.HasIndex(e => new { e.TimeCreated, e.ID });

			// Composite index on (scene_server_id, channel), from the removed ChatService.FetchAsync's
			// local-message exclusion. The pump (FetchPumpAsync) now range-scans (time_created, id)
			// above and applies its echo rule, NOT (scene_server_id = @sceneServerId AND channel IN
			// (World, Trade, Party, Guild, Tell)), as a filter on that scan.
			builder.HasIndex(e => new { e.SceneServerID, e.Channel });

			/* One row per request. A write retried after its reply was lost (the connection dropped
			 * after the commit) carries the same key and lands on the row its first attempt made,
			 * instead of writing a second (issue #267). Filtered, so rows written without a key
			 * never collide. */
			builder.HasIndex(e => e.RequestKey)
				.IsUnique()
				.HasFilter("request_key IS NOT NULL");
		}
	}
}