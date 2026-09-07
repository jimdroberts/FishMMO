using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Entities;
using FishMMO.Database.Npgsql.Services.Interfaces;

namespace FishMMO.Database.Npgsql.Services
{
	/// <summary>
	/// Persists a character's discovered waypoints as per-scene bitmask pages.
	/// </summary>
	/// <remarks>
	/// The mask travels as <see cref="ulong"/> in the game and as <c>bigint</c> in PostgreSQL. The
	/// conversion is a two's-complement reinterpretation, which is lossless, and it is done in
	/// this class only, so nothing above it knows the column is signed. The merge itself is a
	/// bitwise OR in SQL — the database, not the application, is where two concurrent writers'
	/// bits meet.
	/// </remarks>
	public sealed class CharacterWaypointService : BaseService<CharacterWaypointEntity>, ICharacterWaypointService
	{
		private static readonly Func<NpgsqlDbContext, long, IAsyncEnumerable<CharacterWaypointEntity>> getWaypointsQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, long characterId) =>
				context.CharacterWaypoints
					.AsNoTracking()
					.Where(w => w.CharacterID == characterId));

		public CharacterWaypointService(INpgsqlDbContextFactory dbContextFactory) : base(dbContextFactory)
		{
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> MergeAsync(IEnumerable<CharacterWaypointData> pages, CancellationToken cancellationToken = default)
		{
			if (pages == null)
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "Waypoint page collection must not be null.");
			}

			/* Folded per key before the statement, because ON CONFLICT DO UPDATE refuses to touch
			 * the same row twice in one command. Two snapshots of the same page in one batch —
			 * the periodic save and an unlock landing together — OR into one row here, which is
			 * exactly what the database would have done for them in two statements. */
			var merged = new Dictionary<(long CharacterID, string SceneName, short Page), ulong>();
			foreach (CharacterWaypointData page in pages)
			{
				if (page.CharacterID <= 0)
				{
					return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "Character ID must be greater than 0.");
				}
				if (string.IsNullOrWhiteSpace(page.SceneName))
				{
					return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "Scene name must not be empty.");
				}
				if (page.Page < 0)
				{
					return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "Page must not be negative.");
				}
				if (page.Mask == 0)
				{
					// Nothing to merge; an empty page written would only create a row of zero.
					continue;
				}

				var key = (page.CharacterID, page.SceneName, page.Page);
				merged.TryGetValue(key, out ulong existing);
				merged[key] = existing | page.Mask;
			}

			if (merged.Count == 0)
			{
				return DatabaseResult.Success();
			}

			var keys = merged.Keys.ToList();
			var characterIdArray = keys.Select(k => k.CharacterID).ToArray();
			var sceneNameArray = keys.Select(k => k.SceneName).ToArray();
			var pageArray = keys.Select(k => k.Page).ToArray();
			var maskArray = keys.Select(k => unchecked((long)merged[k])).ToArray();

			return await ExecuteWriteAsync(async dbContext =>
			{
				var now = DateTime.UtcNow;
				var sql = $@"
					INSERT INTO {TableName}
						(character_id, scene_name, page, mask, time_created, time_updated)
					SELECT u.character_id, u.scene_name, u.page, u.mask, {{4}}, {{4}}
					FROM UNNEST({{0}}::bigint[], {{1}}::text[], {{2}}::smallint[], {{3}}::bigint[])
						AS u(character_id, scene_name, page, mask)
					ON CONFLICT (character_id, scene_name, page)
					DO UPDATE SET
						mask = {TableName}.mask | EXCLUDED.mask,
						time_updated = EXCLUDED.time_updated
					WHERE {TableName}.mask <> ({TableName}.mask | EXCLUDED.mask);";

				await dbContext.Database
					.ExecuteSqlRawAsync(sql, new object[] { characterIdArray, sceneNameArray, pageArray, maskArray, now }, cancellationToken)
					.ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<IReadOnlyList<CharacterWaypointData>>> FetchAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult<IReadOnlyList<CharacterWaypointData>>.Failure(
					DatabaseErrorCodes.ValidationError, "Character ID must be greater than 0.");
			}

			return await ExecuteReadAsync(async dbContext =>
			{
				var entities = await getWaypointsQuery(dbContext, characterId).MaterializeAsync(cancellationToken).ConfigureAwait(false);
				var pages = entities.Select(w => new CharacterWaypointData(
					characterID: w.CharacterID,
					sceneName: w.SceneName,
					page: w.Page,
					mask: unchecked((ulong)w.Mask))).ToList();
				return (IReadOnlyList<CharacterWaypointData>)pages;
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		/// <remarks>Character deletion only. Hard-deletes every page the character has.</remarks>
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
	}
}
