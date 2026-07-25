using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Entity configuration for DeploymentSecretEntity.
	/// Maps to the "deployment_secrets" table with a text PK on "key".
	/// </summary>
	public class DeploymentSecretEntityConfiguration : IEntityTypeConfiguration<DeploymentSecretEntity>
	{
		/// <inheritdoc/>
		public void Configure(EntityTypeBuilder<DeploymentSecretEntity> builder)
		{
			builder.ToTable("deployment_secrets");

			// Primary Key
			builder.HasKey(e => e.Key);

			// Key — logical identifier for the secret
			builder.Property(e => e.Key)
				.IsRequired()
				.HasMaxLength(255);

			// Value — the secret payload
			builder.Property(e => e.Value)
				.IsRequired();

			// TimeCreated — mapped to "created_at" per the DDL spec
			builder.Property(e => e.TimeCreated)
				.IsRequired()
				.HasColumnName("created_at");

			// TimeUpdated — mapped to "updated_at" per the DDL spec
			builder.Property(e => e.TimeUpdated)
				.IsRequired()
				.HasColumnName("updated_at");
		}
	}
}
