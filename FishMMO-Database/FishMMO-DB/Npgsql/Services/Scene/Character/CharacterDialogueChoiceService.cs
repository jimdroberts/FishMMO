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
	/// Service for the per-character dialogue choice bitmasks.
	/// Provides async merge/fetch operations over <c>character_dialogue_choices</c>.
	/// Uses the BaseService execution strategy for automatic retry on transient database failures.
	/// Returns DatabaseResult for consistent, safe error handling.
	/// </summary>
	/// <remarks>
	/// <para><b>Error Handling:</b> All exceptions are classified by <c>BaseService</c> and mapped to
	/// <see cref="DatabaseResult"/> error codes. Transient failures are retried automatically —
	/// which is safe here only because the write is an OR-merge and therefore idempotent.</para>
	/// </remarks>
	public sealed class CharacterDialogueChoiceService : BaseService<CharacterDialogueChoiceEntity>, ICharacterDialogueChoiceService
	{
		/// <summary>
		/// Compiled query for retrieving a character's dialogue choice masks.
		/// </summary>
		private static readonly Func<NpgsqlDbContext, long, IAsyncEnumerable<CharacterDialogueChoiceEntity>> getChoicesQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, long characterId) =>
				context.CharacterDialogueChoices
					.AsNoTracking()
					.Where(c => c.CharacterID == characterId));

		/// <summary>
		/// Initializes a new instance of the <see cref="CharacterDialogueChoiceService"/> class.
		/// </summary>
		/// <param name="dbContextFactory">Factory for creating database contexts.</param>
		/// <exception cref="ArgumentNullException">Thrown when dbContextFactory is null.</exception>
		public CharacterDialogueChoiceService(INpgsqlDbContextFactory dbContextFactory) : base(dbContextFactory)
		{
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> MergeAsync(IEnumerable<CharacterDialogueChoiceData> choices, CancellationToken cancellationToken = default)
		{
			if (choices == null)
			{
				return DatabaseResult.Failure(
					DatabaseErrorCodes.ValidationError,
					"Dialogue choice collection must not be null.");
			}

			/* Fold duplicate (character, template) pairs together before the statement runs.
			 * PostgreSQL refuses an ON CONFLICT DO UPDATE that would touch the same row twice
			 * ("command cannot affect row a second time"), and the fold is an OR rather than a
			 * last-one-wins overwrite so a batch carrying two partial masks for one conversation
			 * still ends up with the union of them. */
			var merged = new Dictionary<(long CharacterID, int TemplateID), short>();
			foreach (CharacterDialogueChoiceData choice in choices)
			{
				if (choice.CharacterID <= 0)
				{
					return DatabaseResult.Failure(
						DatabaseErrorCodes.ValidationError,
						"Character ID must be greater than 0.");
				}

				var key = (choice.CharacterID, choice.TemplateID);
				merged.TryGetValue(key, out short existing);
				merged[key] = (short)((ushort)existing | (ushort)choice.Choices);
			}

			if (merged.Count == 0)
			{
				return DatabaseResult.Success();
			}

			var keys = merged.Keys.ToList();
			var characterIdArray = keys.Select(k => k.CharacterID).ToArray();
			var templateIdArray = keys.Select(k => k.TemplateID).ToArray();
			var choicesArray = keys.Select(k => merged[k]).ToArray();

			return await ExecuteWriteAsync(async dbContext =>
			{
				var now = DateTime.UtcNow;

				/* choices = table.choices | EXCLUDED.choices, not "= EXCLUDED.choices".
				 *
				 * The mask only ever gains bits, and every bit stands for a choice the character
				 * has already made — usually a one-time reward. An overwrite would let a writer
				 * holding a stale in-memory session clear bits another writer had just set, and
				 * the player could then take that reward again. Merging makes the write both
				 * order-independent (a scene transfer where old and new servers overlap converges)
				 * and idempotent (the execution strategy may retry it after an ambiguous failure).
				 *
				 * The WHERE on the DO UPDATE skips rows the merge would not change, so
				 * re-persisting an unchanged mask — the common case when a character re-opens a
				 * dialogue they have exhausted — writes no dead tuple at all. That also means the
				 * affected row count is legitimately lower than the batch size, which is why this
				 * does not go through ExecuteBulkUpsertAsync: that helper treats a short count as
				 * a lost-authority error, and here it is the intended no-op. */
				var sql = $@"
					INSERT INTO {TableName}
						(character_id, template_id, choices, time_created, time_updated)
					SELECT
						u.character_id,
						u.template_id,
						u.choices,
						{{3}},
						{{3}}
					FROM UNNEST(
						{{0}}::bigint[],
						{{1}}::integer[],
						{{2}}::smallint[]
					) AS u(character_id, template_id, choices)
					ON CONFLICT (character_id, template_id)
					DO UPDATE SET
						choices = {TableName}.choices | EXCLUDED.choices,
						time_updated = EXCLUDED.time_updated
					WHERE
						{TableName}.choices <> ({TableName}.choices | EXCLUDED.choices);";

				await dbContext.Database
					.ExecuteSqlRawAsync(sql, new object[] { characterIdArray, templateIdArray, choicesArray, now }, cancellationToken)
					.ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<IReadOnlyList<CharacterDialogueChoiceData>>> FetchAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult<IReadOnlyList<CharacterDialogueChoiceData>>.Failure(
					DatabaseErrorCodes.ValidationError,
					"Character ID must be greater than 0.");
			}

			return await ExecuteReadAsync(async dbContext =>
			{
				var entities = await getChoicesQuery(dbContext, characterId).MaterializeAsync(cancellationToken).ConfigureAwait(false);
				var choices = entities.Select(c => new CharacterDialogueChoiceData(
					characterID: c.CharacterID,
					templateID: c.TemplateID,
					choices: c.Choices
				)).ToList();

				return (IReadOnlyList<CharacterDialogueChoiceData>)choices;
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<int>> ResetTemplateAsync(int templateId, CancellationToken cancellationToken = default)
		{
			return await ExecuteWriteAsync(async dbContext =>
			{
				var sql = $@"DELETE FROM {TableName} WHERE template_id = {{0}}";

				return await dbContext.Database
					.ExecuteSqlRawAsync(sql, new object[] { templateId }, cancellationToken)
					.ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}
	}
}
