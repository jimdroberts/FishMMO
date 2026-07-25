using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Entity configuration for CharacterAbilityEntity with explicit indexes and constraints.
	/// </summary>
	public class CharacterAbilityEntityConfiguration : IEntityTypeConfiguration<CharacterAbilityEntity>
	{
		/// <inheritdoc/>
		public void Configure(EntityTypeBuilder<CharacterAbilityEntity> builder)
		{
			builder.ToTable("character_abilities");

			// Primary Key
			builder.HasKey(e => e.ID);

			builder.Property(e => e.ID)
				.ValueGeneratedOnAdd();

			// Required fields
			builder.Property(e => e.CharacterID)
				.IsRequired();

			builder.Property(e => e.TemplateID)
				.IsRequired();

			builder.Property(e => e.AbilityEvents)
				.IsRequired()
				.HasDefaultValueSql("''")
				.HasConversion(new ValueConverter<List<int>, string>(
					v => string.Join(",", v),
					v => ParseCsvInts(v)));

			builder.Property(e => e.Cooldown)
				.IsRequired()
				.HasDefaultValue(0f);

			// Unique constraint: one ability template per character
			builder.HasIndex(e => new { e.CharacterID, e.TemplateID })
				.IsUnique();

			// Performance index for character ability queries
			builder.HasIndex(e => e.CharacterID);

			// Foreign key relationship
			builder.HasOne(e => e.Character)
				.WithMany(c => c.Abilities)
				.HasForeignKey(e => e.CharacterID)
				.OnDelete(DeleteBehavior.NoAction);
		}

		/// <summary>
		/// Static helper for EF ValueConverter — expression trees cannot call methods with optional args (CS0854).
		/// </summary>
		private static List<int> ParseCsvInts(string v)
		{
			if (string.IsNullOrEmpty(v)) return new List<int>();
			string[] parts = v.Split(new char[] { ',' }, StringSplitOptions.None);
			var list = new List<int>(parts.Length);
			for (int i = 0; i < parts.Length; i++)
				list.Add(int.Parse(parts[i]));
			return list;
		}
	}
}