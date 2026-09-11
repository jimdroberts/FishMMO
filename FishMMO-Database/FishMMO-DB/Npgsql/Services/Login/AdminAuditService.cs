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
	/// Append-only operator audit log.
	/// </summary>
	/// <remarks>
	/// EF throughout and no raw SQL, for the same reason as <see cref="WebSessionService"/>: an
	/// unqualified raw statement resolves against the connection's <c>search_path</c> rather than
	/// the model's configured schema, and on a deployment where those differ it silently affects
	/// nothing. A log that silently writes nothing is worse than no log, because it is believed.
	/// </remarks>
	public sealed class AdminAuditService : BaseService<AdminAuditEntity>, IAdminAuditService
	{
		/// <summary>The most rows one read will return, whatever it asks for.</summary>
		private const int MaxPageSize = 200;

		/// <summary>Longest reason accepted; matches the column.</summary>
		private const int MaxReasonLength = 1024;

		public AdminAuditService(INpgsqlDbContextFactory dbContextFactory) : base(dbContextFactory)
		{
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<long>> AppendAsync(AdminAuditData entry, CancellationToken cancellationToken = default)
		{
			if (entry == null)
			{
				return DatabaseResult<long>.Failure(DatabaseErrorCodes.ValidationError, "An audit entry is required.");
			}
			if (string.IsNullOrWhiteSpace(entry.ActorName))
			{
				return DatabaseResult<long>.Failure(DatabaseErrorCodes.ValidationError, "An audit entry must name its actor.");
			}
			if (string.IsNullOrWhiteSpace(entry.Action))
			{
				return DatabaseResult<long>.Failure(DatabaseErrorCodes.ValidationError, "An audit entry must name its action.");
			}

			/* Truncate rather than refuse. The row is the record of something that has already
			 * happened; losing it because an operator pasted an essay into the reason box would
			 * be the wrong trade. The column bounds are generous enough that this is rare. */
			var entity = new AdminAuditEntity
			{
				OccurredUtc = entry.OccurredUtc == default ? DateTime.UtcNow : entry.OccurredUtc,
				ActorName = Clamp(entry.ActorName, 100),
				ActorAccessLevel = entry.ActorAccessLevel,
				ActorSessionID = entry.ActorSessionID,
				Action = Clamp(entry.Action, 64),
				TargetType = Clamp(entry.TargetType, 32),
				TargetID = Clamp(entry.TargetID, 128),
				TargetName = Clamp(entry.TargetName, 128),
				Reason = Clamp(entry.Reason, MaxReasonLength),
				Succeeded = entry.Succeeded,
				Outcome = Clamp(entry.Outcome, 512),
				Details = Clamp(entry.Details, 4096),
				IpAddress = Clamp(entry.IpAddress, 64),
				Source = string.IsNullOrWhiteSpace(entry.Source) ? "panel" : Clamp(entry.Source, 16),
			};

			return await ExecuteWriteAsync(async dbContext =>
			{
				await dbContext.AdminAuditLog.AddAsync(entity, cancellationToken).ConfigureAwait(false);
				await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
				return entity.ID;
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<AdminAuditPage>> SearchAsync(AdminAuditQuery query, CancellationToken cancellationToken = default)
		{
			query ??= new AdminAuditQuery();

			int page = query.Page < 1 ? 1 : query.Page;
			int pageSize = query.PageSize < 1 ? 50 : query.PageSize;
			if (pageSize > MaxPageSize)
			{
				pageSize = MaxPageSize;
			}

			string actor = string.IsNullOrWhiteSpace(query.ActorName) ? null : query.ActorName.Trim();
			string targetType = string.IsNullOrWhiteSpace(query.TargetType) ? null : query.TargetType.Trim();
			string targetId = string.IsNullOrWhiteSpace(query.TargetID) ? null : query.TargetID.Trim();
			string action = string.IsNullOrWhiteSpace(query.Action) ? null : query.Action.Trim();

			return await ExecuteReadAsync(async dbContext =>
			{
				IQueryable<AdminAuditEntity> q = dbContext.AdminAuditLog.AsNoTracking();

				if (actor != null)
				{
					q = q.Where(e => e.ActorName == actor);
				}
				if (targetType != null)
				{
					q = q.Where(e => e.TargetType == targetType);
				}
				if (targetId != null)
				{
					q = q.Where(e => e.TargetID == targetId);
				}
				if (action != null)
				{
					q = q.Where(e => e.Action == action);
				}
				if (query.Succeeded.HasValue)
				{
					bool succeeded = query.Succeeded.Value;
					q = q.Where(e => e.Succeeded == succeeded);
				}
				if (query.FromUtc.HasValue)
				{
					DateTime from = query.FromUtc.Value;
					q = q.Where(e => e.OccurredUtc >= from);
				}
				if (query.ToUtc.HasValue)
				{
					DateTime to = query.ToUtc.Value;
					q = q.Where(e => e.OccurredUtc < to);
				}

				int total = await q.CountAsync(cancellationToken).ConfigureAwait(false);

				// ID descending as the tie-break: two actions inside the same clock tick must
				// still come back in the order they were written.
				var rows = await q
					.OrderByDescending(e => e.OccurredUtc)
					.ThenByDescending(e => e.ID)
					.Skip((page - 1) * pageSize)
					.Take(pageSize)
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);

				return new AdminAuditPage
				{
					Items = rows.Select(Map).ToList(),
					Page = page,
					PageSize = pageSize,
					TotalCount = total,
				};
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<IReadOnlyList<string>>> FetchActionsAsync(CancellationToken cancellationToken = default)
		{
			return await ExecuteReadAsync(async dbContext =>
			{
				var actions = await dbContext.AdminAuditLog
					.AsNoTracking()
					.Select(e => e.Action)
					.Distinct()
					.OrderBy(a => a)
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);
				return (IReadOnlyList<string>)actions;
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		private static string Clamp(string value, int max) =>
			string.IsNullOrEmpty(value) || value.Length <= max ? value : value.Substring(0, max);

		private static AdminAuditData Map(AdminAuditEntity e) => new AdminAuditData
		{
			ID = e.ID,
			OccurredUtc = e.OccurredUtc,
			ActorName = e.ActorName,
			ActorAccessLevel = e.ActorAccessLevel,
			ActorSessionID = e.ActorSessionID,
			Action = e.Action,
			TargetType = e.TargetType,
			TargetID = e.TargetID,
			TargetName = e.TargetName,
			Reason = e.Reason,
			Succeeded = e.Succeeded,
			Outcome = e.Outcome,
			Details = e.Details,
			IpAddress = e.IpAddress,
			Source = e.Source,
		};
	}
}
