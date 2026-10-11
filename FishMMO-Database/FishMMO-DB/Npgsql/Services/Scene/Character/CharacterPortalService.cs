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
	/// Persists the portals a character has activated for themselves, as per-scene bitmask pages.
	/// </summary>
	/// <remarks>
	/// A copy of <see cref="CharacterWaypointService"/> over its own table rather than a shared
	/// table with a "kind" column: the two index spaces are authored separately (a waypoint and a
	/// portal may both be index 0 in a scene), and keeping them apart keeps the waypoint row's key
	/// and every query over it unchanged. The mask is <see cref="ulong"/> in the game and
	/// <c>bigint</c> here; the two's-complement reinterpretation is done in this class only.
	/// </remarks>
	public sealed class CharacterPortalService : BaseService<CharacterPortalEntity>, ICharacterPortalService
	{
		private static readonly Func<NpgsqlDbContext, long, IAsyncEnumerable<CharacterPortalEntity>> getPortalsQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, long characterId) =>
				context.CharacterPortals
					.AsNoTracking()
					.Where(p => p.CharacterID == characterId));

		public CharacterPortalService(INpgsqlDbContextFactory dbContextFactory) : base(dbContextFactory)
		{
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> MergeAsync(IEnumerable<CharacterPortalData> pages, CancellationToken cancellationToken = default)
		{
			if (pages == null)
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "Portal page collection must not be null.");
			}

			// Folded per key first: ON CONFLICT DO UPDATE refuses to touch the same row twice in one command.
			var merged = new Dictionary<(long CharacterID, string SceneName, short Page), ulong>();
			foreach (CharacterPortalData page in pages)
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
		public async Task<DatabaseResult<IReadOnlyList<CharacterPortalData>>> FetchAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult<IReadOnlyList<CharacterPortalData>>.Failure(
					DatabaseErrorCodes.ValidationError, "Character ID must be greater than 0.");
			}

			return await ExecuteReadAsync(async dbContext =>
			{
				var entities = await getPortalsQuery(dbContext, characterId).MaterializeAsync(cancellationToken).ConfigureAwait(false);
				var pages = entities.Select(p => new CharacterPortalData(
					characterID: p.CharacterID,
					sceneName: p.SceneName,
					page: p.Page,
					mask: unchecked((ulong)p.Mask))).ToList();
				return (IReadOnlyList<CharacterPortalData>)pages;
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		/// <remarks>Hard-deletes every page the character has. The foreign key cascades too; this exists for an explicit purge.</remarks>
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
