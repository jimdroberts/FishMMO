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
	/// Persists portals opened for the whole world: one row per (scene, portal index).
	/// </summary>
	/// <remarks>
	/// The merge is done in SQL so two scene servers opening the same portal at once converge:
	/// <c>permanent</c> ORs and <c>active_until</c> takes <c>GREATEST</c>, never the newer write.
	/// </remarks>
	public sealed class WorldPortalStateService : BaseService<WorldPortalStateEntity>, IWorldPortalStateService
	{
		private static readonly Func<NpgsqlDbContext, string, IAsyncEnumerable<WorldPortalStateEntity>> getSceneQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, string sceneName) =>
				context.WorldPortalStates
					.AsNoTracking()
					.Where(p => p.SceneName == sceneName));

		public WorldPortalStateService(INpgsqlDbContextFactory dbContextFactory) : base(dbContextFactory)
		{
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> MergeAsync(IEnumerable<WorldPortalStateData> states, CancellationToken cancellationToken = default)
		{
			if (states == null)
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "Portal state collection must not be null.");
			}

			var merged = new Dictionary<(string SceneName, int PortalIndex), (bool Permanent, DateTime ActiveUntil)>();
			foreach (WorldPortalStateData state in states)
			{
				if (string.IsNullOrWhiteSpace(state.SceneName))
				{
					return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "Scene name must not be empty.");
				}
				if (state.PortalIndex < 0)
				{
					return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "Portal index must not be negative.");
				}

				var key = (state.SceneName, state.PortalIndex);
				DateTime until = DateTime.SpecifyKind(state.ActiveUntilUtc, DateTimeKind.Utc);
				if (merged.TryGetValue(key, out var existing))
				{
					merged[key] = (existing.Permanent || state.Permanent, existing.ActiveUntil > until ? existing.ActiveUntil : until);
				}
				else
				{
					merged[key] = (state.Permanent, until);
				}
			}

			if (merged.Count == 0)
			{
				return DatabaseResult.Success();
			}

			var keys = merged.Keys.ToList();
			var sceneArray = keys.Select(k => k.SceneName).ToArray();
			var indexArray = keys.Select(k => k.PortalIndex).ToArray();
			var permanentArray = keys.Select(k => merged[k].Permanent).ToArray();
			var untilArray = keys.Select(k => merged[k].ActiveUntil).ToArray();

			return await ExecuteWriteAsync(async dbContext =>
			{
				var now = DateTime.UtcNow;
				var sql = $@"
					INSERT INTO {TableName}
						(scene_name, portal_index, permanent, active_until, time_created, time_updated)
					SELECT u.scene_name, u.portal_index, u.permanent, u.active_until, {{4}}, {{4}}
					FROM UNNEST({{0}}::text[], {{1}}::integer[], {{2}}::boolean[], {{3}}::timestamp without time zone[])
						AS u(scene_name, portal_index, permanent, active_until)
					ON CONFLICT (scene_name, portal_index)
					DO UPDATE SET
						permanent = {TableName}.permanent OR EXCLUDED.permanent,
						active_until = GREATEST({TableName}.active_until, EXCLUDED.active_until),
						time_updated = EXCLUDED.time_updated
					WHERE ({TableName}.permanent OR EXCLUDED.permanent) <> {TableName}.permanent
						OR EXCLUDED.active_until > {TableName}.active_until;";

				await dbContext.Database
					.ExecuteSqlRawAsync(sql, new object[] { sceneArray, indexArray, permanentArray, untilArray, now }, cancellationToken)
					.ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<IReadOnlyList<WorldPortalStateData>>> FetchAsync(string sceneName, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(sceneName))
			{
				return DatabaseResult<IReadOnlyList<WorldPortalStateData>>.Failure(
					DatabaseErrorCodes.ValidationError, "Scene name must not be empty.");
			}

			return await ExecuteReadAsync(async dbContext =>
			{
				var entities = await getSceneQuery(dbContext, sceneName).MaterializeAsync(cancellationToken).ConfigureAwait(false);
				var states = entities.Select(p => new WorldPortalStateData(
					sceneName: p.SceneName,
					portalIndex: p.PortalIndex,
					permanent: p.Permanent,
					activeUntilUtc: DateTime.SpecifyKind(p.ActiveUntil, DateTimeKind.Utc))).ToList();
				return (IReadOnlyList<WorldPortalStateData>)states;
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}
	}
}
