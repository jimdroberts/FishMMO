using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Entity configuration for CharacterPetEntity with explicit indexes and constraints.
	/// </summary>
	public class CharacterPetEntityConfiguration : IEntityTypeConfiguration<CharacterPetEntity>
	{
		/// <inheritdoc/>
		public void Configure(EntityTypeBuilder<CharacterPetEntity> builder)
		{
			builder.ToTable("character_pet");

			// Primary Key
			builder.HasKey(e => e.ID);

			builder.Property(e => e.ID)
				.ValueGeneratedOnAdd();

			// Required fields
			builder.Property(e => e.CharacterID)
				.IsRequired();

			builder.Property(e => e.TemplateID)
				.IsRequired();

			builder.Property(e => e.Abilities)
				.IsRequired()
				.HasDefaultValueSql("''")
				.HasConversion(new ValueConverter<List<int>, string>(
					v => string.Join(",", v),
					v => ParseCsvInts(v)));

			builder.Property(e => e.Spawned)
				.IsRequired()
				.HasDefaultValue(false);

			// Unique constraint: one pet per character
			builder.HasIndex(e => e.CharacterID)
				.IsUnique();

			// Foreign key relationship
			builder.HasOne(e => e.Character)
				.WithOne(c => c.Pet)
				.HasForeignKey<CharacterPetEntity>(e => e.CharacterID)
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