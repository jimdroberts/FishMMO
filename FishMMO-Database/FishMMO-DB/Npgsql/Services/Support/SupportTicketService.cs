using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using FishMMO.Database.Data;
using FishMMO.Database.Data.Enums;
using FishMMO.Database.Exceptions;
using FishMMO.Database.Npgsql.Entities;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Shared;

namespace FishMMO.Database.Npgsql.Services
{
	/// <summary>
	/// Support tickets and their conversations.
	/// </summary>
	/// <remarks>
	/// EF throughout, as <see cref="AdminAuditService"/> is and for the same reason: an
	/// unqualified raw statement resolves against the connection's <c>search_path</c> rather
	/// than the model's schema, and silently affects nothing where those differ.
	/// </remarks>
	public sealed class SupportTicketService : BaseService<SupportTicketEntity>, ISupportTicketService
	{
		/// <summary>The most rows one search will return, whatever it asks for.</summary>
		private const int MaxPageSize = 100;

		/// <summary>
		/// How many unfinished tickets one account may hold at once.
		/// </summary>
		/// <remarks>
		/// Not a policy about how much help somebody deserves — it is the flood limit. Without
		/// it one annoyed player can bury the queue deep enough that real reports are never
		/// seen, which costs every other player their support.
		/// </remarks>
		private const int MaxOpenTicketsPerAccount = 5;

		/// <summary>The shortest gap between one account's filings.</summary>
		private static readonly TimeSpan FilingCooldown = TimeSpan.FromSeconds(60);

		/// <summary>Statuses that still count as work outstanding.</summary>
		private static readonly SupportTicketStatus[] UnfinishedStatuses =
		{
			SupportTicketStatus.Open,
			SupportTicketStatus.InProgress,
			SupportTicketStatus.AwaitingPlayer,
		};

		public SupportTicketService(INpgsqlDbContextFactory dbContextFactory) : base(dbContextFactory)
		{
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<long>> CreateAsync(SupportTicketCreate ticket, CancellationToken cancellationToken = default)
		{
			if (ticket == null)
			{
				return DatabaseResult<long>.Failure(DatabaseErrorCodes.ValidationError, "A ticket is required.");
			}
			if (!Authentication.IsAllowedUsername(ticket.ReporterAccount))
			{
				return DatabaseResult<long>.Failure(DatabaseErrorCodes.ValidationError, Authentication.InvalidUsernameError);
			}
			if (string.IsNullOrWhiteSpace(ticket.Subject))
			{
				return DatabaseResult<long>.Failure(DatabaseErrorCodes.ValidationError, "A subject is required.");
			}
			if (string.IsNullOrWhiteSpace(ticket.Body))
			{
				return DatabaseResult<long>.Failure(DatabaseErrorCodes.ValidationError, "Describe what happened.");
			}
			if (!Enum.IsDefined(typeof(SupportTicketCategory), ticket.Category))
			{
				return DatabaseResult<long>.Failure(DatabaseErrorCodes.ValidationError, "That is not a ticket category.");
			}

			/* A player report that names nobody cannot be acted on, and arrives as a ticket
			 * staff must close with "who?". Refusing it at the door is kinder than the round
			 * trip. */
			if (ticket.Category == SupportTicketCategory.PlayerReport &&
				string.IsNullOrWhiteSpace(ticket.TargetCharacterName) &&
				string.IsNullOrWhiteSpace(ticket.TargetAccount))
			{
				return DatabaseResult<long>.Failure(DatabaseErrorCodes.ValidationError, "Name the player you are reporting.");
			}

			string reporter = ticket.ReporterAccount;
			DateTime now = DateTime.UtcNow;

			return await ExecuteTransactionAsync(async dbContext =>
			{
				/* Counted inside the transaction that inserts, so two rapid filings cannot both
				 * read four and both write a fifth. */
				int open = await dbContext.SupportTickets
					.CountAsync(t => t.ReporterAccount == reporter && UnfinishedStatuses.Contains(t.Status), cancellationToken)
					.ConfigureAwait(false);

				if (open >= MaxOpenTicketsPerAccount)
				{
					throw new DatabaseException(
						$"You already have {open} tickets open. Wait for one to be answered before filing another.",
						errorCode: DatabaseErrorCodes.CapacityExceeded);
				}

				DateTime cooldownStart = now - FilingCooldown;
				bool tooSoon = await dbContext.SupportTickets
					.AnyAsync(t => t.ReporterAccount == reporter && t.CreatedUtc > cooldownStart, cancellationToken)
					.ConfigureAwait(false);

				if (tooSoon)
				{
					throw new DatabaseException(
						"You have just filed a ticket. Give it a moment before filing another.",
						errorCode: DatabaseErrorCodes.CapacityExceeded);
				}

				var entity = new SupportTicketEntity
				{
					CreatedUtc = now,
					LastActivityUtc = now,
					ReporterAccount = reporter,
					ReporterCharacterName = Clamp(ticket.ReporterCharacterName, 64),
					ReporterCharacterID = ticket.ReporterCharacterID,
					Category = ticket.Category,
					// Always Open, never taken from the caller: see SupportTicketCreate.
					Status = SupportTicketStatus.Open,
					Priority = 0,
					Subject = Clamp(ticket.Subject.Trim(), 160),
					Body = Clamp(ticket.Body.Trim(), 4000),
					TargetAccount = Clamp(ticket.TargetAccount, 100),
					TargetCharacterName = Clamp(ticket.TargetCharacterName, 64),
					TargetCharacterID = ticket.TargetCharacterID,
					SceneName = Clamp(ticket.SceneName, 128),
				};

				await dbContext.SupportTickets.AddAsync(entity, cancellationToken).ConfigureAwait(false);
				await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
				return entity.ID;
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<SupportTicketData>> FetchAsync(long ticketId, bool includeInternal, CancellationToken cancellationToken = default)
		{
			if (ticketId <= 0)
			{
				return DatabaseResult<SupportTicketData>.Failure(DatabaseErrorCodes.ValidationError, "Ticket ID must be greater than 0.");
			}

			return await ExecuteReadAsync(async dbContext =>
			{
				var entity = await dbContext.SupportTickets
					.AsNoTracking()
					.FirstOrDefaultAsync(t => t.ID == ticketId, cancellationToken)
					.ConfigureAwait(false);

				if (entity == null)
				{
					throw new DatabaseEntityNotFoundException("SupportTicket", ticketId.ToString());
				}

				/* The internal filter is applied in the QUERY, not to the results. A projection
				 * that fetched everything and trimmed afterwards would put the private notes in
				 * the process's memory one careless serialization away from the player. */
				IQueryable<SupportTicketMessageEntity> messages = dbContext.SupportTicketMessages
					.AsNoTracking()
					.Where(m => m.TicketID == ticketId);

				if (!includeInternal)
				{
					messages = messages.Where(m => !m.Internal);
				}

				var rows = await messages
					.OrderBy(m => m.CreatedUtc)
					.ThenBy(m => m.ID)
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);

				var data = Map(entity);
				data.Messages = rows.Select(MapMessage).ToList();
				data.MessageCount = rows.Count;
				return data;
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<SupportTicketPage>> SearchAsync(SupportTicketQuery query, CancellationToken cancellationToken = default)
		{
			query ??= new SupportTicketQuery();

			int page = query.Page < 1 ? 1 : query.Page;
			int pageSize = query.PageSize < 1 ? 25 : query.PageSize;
			if (pageSize > MaxPageSize)
			{
				pageSize = MaxPageSize;
			}

			var statuses = query.Statuses?.ToArray();
			string assignedTo = string.IsNullOrWhiteSpace(query.AssignedTo) ? null : query.AssignedTo.Trim();
			string reporter = string.IsNullOrWhiteSpace(query.ReporterAccount) ? null : query.ReporterAccount.Trim();
			string target = string.IsNullOrWhiteSpace(query.TargetAccount) ? null : query.TargetAccount.Trim();
			string subject = string.IsNullOrWhiteSpace(query.Subject) ? null : query.Subject.Trim();

			return await ExecuteReadAsync(async dbContext =>
			{
				IQueryable<SupportTicketEntity> q = dbContext.SupportTickets.AsNoTracking();

				if (statuses != null && statuses.Length > 0)
				{
					q = q.Where(t => statuses.Contains(t.Status));
				}
				if (query.Category.HasValue)
				{
					var category = query.Category.Value;
					q = q.Where(t => t.Category == category);
				}
				if (query.UnassignedOnly)
				{
					q = q.Where(t => t.AssignedTo == null);
				}
				else if (assignedTo != null)
				{
					q = q.Where(t => t.AssignedTo == assignedTo);
				}
				if (reporter != null)
				{
					q = q.Where(t => t.ReporterAccount == reporter);
				}
				if (target != null)
				{
					q = q.Where(t => t.TargetAccount == target);
				}
				if (subject != null)
				{
					// A contains, which no index can serve. The subject list is small and always
					// narrowed by a status filter in practice; see the queue's default.
					q = q.Where(t => EF.Functions.ILike(t.Subject, $"%{subject}%"));
				}

				int total = await q.CountAsync(cancellationToken).ConfigureAwait(false);

				/* Priority first, then oldest activity. A queue sorted by newest would mean the
				 * tickets nobody has answered sink out of sight, which is the failure mode of
				 * every support queue that sorts that way. */
				var rows = await q
					.OrderByDescending(t => t.Priority)
					.ThenBy(t => t.LastActivityUtc)
					.ThenBy(t => t.ID)
					.Skip((page - 1) * pageSize)
					.Take(pageSize)
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);

				var ids = rows.Select(r => r.ID).ToList();
				var counts = await dbContext.SupportTicketMessages
					.AsNoTracking()
					.Where(m => ids.Contains(m.TicketID))
					.GroupBy(m => m.TicketID)
					.Select(g => new { TicketID = g.Key, Count = g.Count() })
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);

				var byTicket = counts.ToDictionary(c => c.TicketID, c => c.Count);

				return new SupportTicketPage
				{
					Items = rows.Select(r =>
					{
						var data = Map(r);
						data.MessageCount = byTicket.TryGetValue(r.ID, out int count) ? count : 0;
						return data;
					}).ToList(),
					Page = page,
					PageSize = pageSize,
					TotalCount = total,
				};
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<long>> AppendMessageAsync(
			long ticketId,
			string authorAccount,
			bool authorIsStaff,
			bool internalNote,
			string body,
			CancellationToken cancellationToken = default)
		{
			if (ticketId <= 0)
			{
				return DatabaseResult<long>.Failure(DatabaseErrorCodes.ValidationError, "Ticket ID must be greater than 0.");
			}
			if (string.IsNullOrWhiteSpace(authorAccount))
			{
				return DatabaseResult<long>.Failure(DatabaseErrorCodes.ValidationError, "An author is required.");
			}
			if (string.IsNullOrWhiteSpace(body))
			{
				return DatabaseResult<long>.Failure(DatabaseErrorCodes.ValidationError, "A message is required.");
			}

			/* An internal note from somebody who is not staff is a contradiction, and the safe
			 * reading of it is not "make it public" — it is that the caller is confused. Refuse. */
			if (internalNote && !authorIsStaff)
			{
				return DatabaseResult<long>.Failure(DatabaseErrorCodes.ValidationError, "Only staff may write an internal note.");
			}

			DateTime now = DateTime.UtcNow;

			return await ExecuteTransactionAsync(async dbContext =>
			{
				var ticket = await dbContext.SupportTickets
					.FirstOrDefaultAsync(t => t.ID == ticketId, cancellationToken)
					.ConfigureAwait(false);

				if (ticket == null)
				{
					throw new DatabaseEntityNotFoundException("SupportTicket", ticketId.ToString());
				}
				if (ticket.Status == SupportTicketStatus.Closed)
				{
					throw new DatabaseException("That ticket is closed.", errorCode: DatabaseErrorCodes.ValidationError);
				}

				var message = new SupportTicketMessageEntity
				{
					TicketID = ticketId,
					CreatedUtc = now,
					AuthorAccount = authorAccount,
					AuthorIsStaff = authorIsStaff,
					Internal = internalNote,
					Body = Clamp(body.Trim(), 4000),
				};
				await dbContext.SupportTicketMessages.AddAsync(message, cancellationToken).ConfigureAwait(false);

				/* An internal note does not move the activity clock. The queue sorts by it to
				 * answer "who has been waiting longest", and staff talking among themselves is
				 * not the player being answered — counting it would let a busy ticket look
				 * attended to while its reporter hears nothing. */
				if (!internalNote)
				{
					ticket.LastActivityUtc = now;

					/* A player replying to a resolved ticket reopens it. They are telling you it
					 * was not resolved, and the alternative is that the reply lands somewhere
					 * nobody looks. */
					if (!authorIsStaff && ticket.Status == SupportTicketStatus.Resolved)
					{
						ticket.Status = SupportTicketStatus.Open;
						ticket.ClosedUtc = null;
					}
					else if (authorIsStaff && ticket.Status == SupportTicketStatus.Open)
					{
						ticket.Status = SupportTicketStatus.InProgress;
					}
				}

				await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
				return message.ID;
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> AssignAsync(long ticketId, string staffAccount, CancellationToken cancellationToken = default)
		{
			if (ticketId <= 0)
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "Ticket ID must be greater than 0.");
			}

			return await ExecuteWriteAsync(async dbContext =>
			{
				var ticket = await dbContext.SupportTickets
					.FirstOrDefaultAsync(t => t.ID == ticketId, cancellationToken)
					.ConfigureAwait(false);

				if (ticket == null)
				{
					throw new DatabaseEntityNotFoundException("SupportTicket", ticketId.ToString());
				}
				if (ticket.Status == SupportTicketStatus.Closed)
				{
					throw new DatabaseException("That ticket is closed.", errorCode: DatabaseErrorCodes.ValidationError);
				}

				ticket.AssignedTo = string.IsNullOrWhiteSpace(staffAccount) ? null : staffAccount.Trim();

				// Taking a ticket and saying you are working on it are one act.
				if (ticket.AssignedTo != null && ticket.Status == SupportTicketStatus.Open)
				{
					ticket.Status = SupportTicketStatus.InProgress;
				}

				ticket.LastActivityUtc = DateTime.UtcNow;
				await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> SetStatusAsync(
			long ticketId,
			SupportTicketStatus status,
			string staffAccount,
			string resolution,
			CancellationToken cancellationToken = default)
		{
			if (ticketId <= 0)
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "Ticket ID must be greater than 0.");
			}
			if (!Enum.IsDefined(typeof(SupportTicketStatus), status))
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "That is not a ticket status.");
			}

			bool finishing = status == SupportTicketStatus.Resolved || status == SupportTicketStatus.Closed;
			if (finishing && string.IsNullOrWhiteSpace(resolution))
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError,
					"Say what was decided. A finished ticket with no resolution cannot answer anything later.");
			}

			return await ExecuteWriteAsync(async dbContext =>
			{
				var ticket = await dbContext.SupportTickets
					.FirstOrDefaultAsync(t => t.ID == ticketId, cancellationToken)
					.ConfigureAwait(false);

				if (ticket == null)
				{
					throw new DatabaseEntityNotFoundException("SupportTicket", ticketId.ToString());
				}

				/* Closed is terminal. A ticket that can be reopened by staff indefinitely is one
				 * whose closure means nothing; a player who needs more sends a new one, which
				 * also gives the new conversation its own record. */
				if (ticket.Status == SupportTicketStatus.Closed)
				{
					throw new DatabaseException("That ticket is closed.", errorCode: DatabaseErrorCodes.ValidationError);
				}

				ticket.Status = status;
				ticket.LastActivityUtc = DateTime.UtcNow;

				if (finishing)
				{
					ticket.Resolution = Clamp(resolution.Trim(), 2000);
					ticket.ClosedUtc = DateTime.UtcNow;
					ticket.ClosedBy = Clamp(staffAccount, 100);
				}
				else
				{
					// Reopening: the old closure no longer describes the ticket's state.
					ticket.ClosedUtc = null;
					ticket.ClosedBy = null;
				}

				await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> SetPriorityAsync(long ticketId, int priority, CancellationToken cancellationToken = default)
		{
			if (ticketId <= 0)
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "Ticket ID must be greater than 0.");
			}
			if (priority < 0 || priority > 3)
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "Priority runs from 0 to 3.");
			}

			return await ExecuteWriteAsync(async dbContext =>
			{
				var ticket = await dbContext.SupportTickets
					.FirstOrDefaultAsync(t => t.ID == ticketId, cancellationToken)
					.ConfigureAwait(false);

				if (ticket == null)
				{
					throw new DatabaseEntityNotFoundException("SupportTicket", ticketId.ToString());
				}

				ticket.Priority = priority;
				await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<int>> CountOpenForAccountAsync(string accountName, CancellationToken cancellationToken = default)
		{
			if (!Authentication.IsAllowedUsername(accountName))
			{
				return DatabaseResult<int>.Failure(DatabaseErrorCodes.ValidationError, Authentication.InvalidUsernameError);
			}

			return await ExecuteReadAsync(async dbContext => await dbContext.SupportTickets
				.AsNoTracking()
				.CountAsync(t => t.ReporterAccount == accountName && UnfinishedStatuses.Contains(t.Status), cancellationToken)
				.ConfigureAwait(false),
				cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		private static string Clamp(string value, int max) =>
			string.IsNullOrEmpty(value) || value.Length <= max ? value : value.Substring(0, max);

		private static SupportTicketData Map(SupportTicketEntity e) => new SupportTicketData
		{
			ID = e.ID,
			CreatedUtc = e.CreatedUtc,
			LastActivityUtc = e.LastActivityUtc,
			ReporterAccount = e.ReporterAccount,
			ReporterCharacterName = e.ReporterCharacterName,
			ReporterCharacterID = e.ReporterCharacterID,
			Category = e.Category,
			Status = e.Status,
			Priority = e.Priority,
			Subject = e.Subject,
			Body = e.Body,
			TargetAccount = e.TargetAccount,
			TargetCharacterName = e.TargetCharacterName,
			TargetCharacterID = e.TargetCharacterID,
			SceneName = e.SceneName,
			AssignedTo = e.AssignedTo,
			Resolution = e.Resolution,
			ClosedUtc = e.ClosedUtc,
			ClosedBy = e.ClosedBy,
		};

		private static SupportTicketMessageData MapMessage(SupportTicketMessageEntity e) => new SupportTicketMessageData
		{
			ID = e.ID,
			TicketID = e.TicketID,
			CreatedUtc = e.CreatedUtc,
			AuthorAccount = e.AuthorAccount,
			AuthorIsStaff = e.AuthorIsStaff,
			Internal = e.Internal,
			Body = e.Body,
		};
	}
}
