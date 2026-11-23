using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Entities;
using FishMMO.Database.Npgsql.Services.Interfaces;

namespace FishMMO.Database.Npgsql.Services
{
	/// <summary>
	/// Persists a character's one marked location (<c>character_marks</c>, issue #241): one row, replaced on every mark.
	/// </summary>
	public sealed class CharacterMarkService : BaseService<CharacterMarkEntity>, ICharacterMarkService
	{
		public CharacterMarkService(INpgsqlDbContextFactory dbContextFactory) : base(dbContextFactory)
		{
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> SaveAsync(CharacterMarkData mark, CancellationToken cancellationToken = default)
		{
			if (mark.CharacterID <= 0)
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "Character ID must be greater than 0.");
			}
			if (string.IsNullOrWhiteSpace(mark.SceneName) || mark.SceneName.Length > 64)
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "Scene name must be 1 to 64 characters.");
			}
			if (!IsFinite(mark.X) || !IsFinite(mark.Y) || !IsFinite(mark.Z) || !IsFinite(mark.Heading))
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "The mark's position and heading must be finite.");
			}

			return await ExecuteWriteAsync(async dbContext =>
			{
				var sql = $@"
					INSERT INTO {TableName}
						(character_id, scene_name, scene_id, x, y, z, heading, time_updated)
					VALUES ({{0}}, {{1}}, {{2}}, {{3}}, {{4}}, {{5}}, {{6}}, {{7}})
					ON CONFLICT (character_id)
					DO UPDATE SET
						scene_name = EXCLUDED.scene_name,
						scene_id = EXCLUDED.scene_id,
						x = EXCLUDED.x,
						y = EXCLUDED.y,
						z = EXCLUDED.z,
						heading = EXCLUDED.heading,
						time_updated = EXCLUDED.time_updated;";

				await dbContext.Database
					.ExecuteSqlRawAsync(sql, new object[]
					{
						mark.CharacterID, mark.SceneName, mark.SceneID, mark.X, mark.Y, mark.Z, mark.Heading, DateTime.UtcNow,
					}, cancellationToken)
					.ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		/// <remarks>Null when the character has never marked a location.</remarks>
		public async Task<DatabaseResult<CharacterMarkData?>> FetchAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult<CharacterMarkData?>.Failure(DatabaseErrorCodes.ValidationError, "Character ID must be greater than 0.");
			}

			return await ExecuteReadAsync(async dbContext =>
			{
				CharacterMarkEntity entity = await dbContext.Set<CharacterMarkEntity>()
					.AsNoTracking()
					.FirstOrDefaultAsync(m => m.CharacterID == characterId, cancellationToken)
					.ConfigureAwait(false);
				return entity == null
					? (CharacterMarkData?)null
					: new CharacterMarkData(entity.CharacterID, entity.SceneName, entity.SceneID, entity.X, entity.Y, entity.Z, entity.Heading);
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		/// <remarks>Character deletion only.</remarks>
		public async Task<DatabaseResult> DeleteAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "Character ID must be greater than 0.");
			}

			return await ExecuteWriteAsync(async dbContext =>
			{
				var sql = $@"DELETE FROM {TableName} WHERE character_id = {{0}}";
				await dbContext.Database
					.ExecuteSqlRawAsync(sql, new object[] { characterId }, cancellationToken)
					.ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
	}
}
