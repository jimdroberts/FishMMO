using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Entity configuration for <see cref="QuestEntity"/>.
	/// </summary>
	public class QuestEntityConfiguration : IEntityTypeConfiguration<QuestEntity>
	{
		public void Configure(EntityTypeBuilder<QuestEntity> builder)
		{
			builder.ToTable("quests");

			// Primary Key
			builder.HasKey(e => e.ID);

			builder.Property(e => e.ID)
				.ValueGeneratedOnAdd();

			// Required fields
			builder.Property(e => e.TimeCreated)
				.IsRequired()
				.HasDefaultValue(DateTime.UnixEpoch);
		}
	}
}