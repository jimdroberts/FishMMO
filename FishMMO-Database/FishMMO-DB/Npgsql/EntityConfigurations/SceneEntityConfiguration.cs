using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Entity configuration for SceneEntity with explicit indexes and constraints.
	/// </summary>
	public class SceneEntityConfiguration : IEntityTypeConfiguration<SceneEntity>
	{
		public void Configure(EntityTypeBuilder<SceneEntity> builder)
		{
			// Primary Key
			builder.HasKey(e => e.ID);

			builder.Property(e => e.ID)
				.ValueGeneratedOnAdd();

			// Required fields
			builder.Property(e => e.SceneServerID)
				.IsRequired();

			builder.Property(e => e.WorldServerID)
				.IsRequired();

			builder.Property(e => e.SceneName)
				.IsRequired()
				.HasMaxLength(100);

			builder.Property(e => e.SceneHandle)
				.IsRequired();

			builder.Property(e => e.SceneStatus)
				.IsRequired();

			builder.Property(e => e.SceneType)
				.IsRequired();

			builder.Property(e => e.CharacterID)
				.IsRequired();

			builder.Property(e => e.CharacterCount)
				.IsRequired()
				.HasDefaultValue(0);

			builder.Property(e => e.TimeCreated)
				.IsRequired()
				.HasDefaultValueSql("CURRENT_TIMESTAMP");

			// Performance indexes for common queries
			builder.HasIndex(e => e.SceneServerID)
				.HasDatabaseName("IX_Scene_SceneServerID");

			builder.HasIndex(e => e.WorldServerID)
				.HasDatabaseName("IX_Scene_WorldServerID");

			builder.HasIndex(e => e.CharacterID)
				.HasDatabaseName("IX_Scene_CharacterID");

			builder.HasIndex(e => new { e.SceneType, e.SceneStatus })
				.HasDatabaseName("IX_Scene_Type_Status");

			builder.HasIndex(e => e.TimeCreated)
				.HasDatabaseName("IX_Scene_TimeCreated");

			// Performance index for scene queue dequeue (hot path: DequeueAsync)
			// Covers WHERE scene_status = 0 ORDER BY time_created, id LIMIT 1
			builder.HasIndex(e => new { e.SceneStatus, e.TimeCreated, e.ID })
				.HasDatabaseName("IX_Scene_Status_TimeCreated")
				.HasFilter("scene_status = 0");

			// Performance index for scene ready-claim (hot path: SetReadyAsync)
			// Covers WHERE world_server_id = ? AND scene_name = ? AND scene_status = 1 ORDER BY time_created, id LIMIT 1
			builder.HasIndex(e => new { e.WorldServerID, e.SceneName, e.TimeCreated, e.ID })
				.HasDatabaseName("IX_Scene_ClaimReady")
				.HasFilter("scene_status = 1");
		}
	}
}