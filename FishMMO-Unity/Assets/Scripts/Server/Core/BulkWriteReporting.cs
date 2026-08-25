using System.Threading.Tasks;
using FishMMO.Database;
using FishMMO.Logging;

namespace FishMMO.Server.Core
{
	/// <summary>
	/// Turns a bulk write's outcome into the log line it deserves, and answers the only two
	/// questions a caller actually has about one.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A batched write reports three counts rather than a boolean — see
	/// <see cref="BulkWriteResult"/> — and the two ways rows go missing want opposite treatment.
	/// A superseded row is the database telling us it already holds something newer, which is
	/// routine under concurrency and not worth a warning on every periodic save. A filtered row
	/// is the service telling us it could not act on what we asked for at all, which is never
	/// routine and was, before this, entirely silent.
	/// </para>
	/// <para>
	/// Centralised so that every call site classifies a discrepancy the same way. Spread across
	/// twenty hand-written checks, the distinction would survive in some and quietly rot in the
	/// rest — which is how it came to be missing in the first place.
	/// </para>
	/// </remarks>
	public static class BulkWriteReporting
	{
		/// <summary>
		/// Reports a best-effort bulk write: one where the database legitimately holding newer
		/// data is an acceptable outcome.
		/// </summary>
		/// <remarks>
		/// The right call for periodic and despawn saves. A superseded row there means another
		/// writer got to it first with a fresher snapshot, so the stored value is the better of
		/// the two and there is nothing to repair.
		/// </remarks>
		/// <param name="tag">Log source.</param>
		/// <param name="operation">What was being written, for the log line.</param>
		/// <param name="result">The write's outcome.</param>
		/// <param name="context">Optional extra identification, such as a character ID.</param>
		/// <returns>True if the write succeeded, whether or not every row landed.</returns>
		public static async Task<bool> ReportAsync(
			string tag,
			string operation,
			DatabaseResult<BulkWriteResult> result,
			string context = null)
		{
			string where = string.IsNullOrEmpty(context) ? string.Empty : $" ({context})";

			if (!result.IsSuccess)
			{
				await Log.Warning(tag, $"{operation}{where} failed: [{result.ErrorCode}] {result.ErrorMessage}");
				return false;
			}

			BulkWriteResult write = result.Data;

			if (write.Filtered > 0)
			{
				/* The service declined to attempt these rows: an unresolvable character or
				 * template, or a key it had already seen. Nothing about the database's state
				 * explains it, so the batch itself is wrong and someone should look. */
				await Log.Warning(tag,
					$"{operation}{where}: {write.Filtered} of {write.Supplied} rows were not attempted " +
					$"(unresolved character or template). {write}");
			}
			else if (write.Superseded > 0)
			{
				// Expected under concurrency, and the stored data is the newer of the two.
				await Log.Debug(tag, $"{operation}{where}: {write}");
			}

			return true;
		}

		/// <summary>
		/// Reports a bulk write that must land in full, failing the caller if it did not.
		/// </summary>
		/// <remarks>
		/// The right call for rows that are being created for the first time — a new character's
		/// starting inventory, factions and abilities. Nothing newer can exist to supersede them,
		/// so a shortfall is not a race: it means rows were dropped, and continuing would hand the
		/// player a character that is quietly missing part of itself.
		/// </remarks>
		/// <param name="tag">Log source.</param>
		/// <param name="operation">What was being written, for the log line.</param>
		/// <param name="result">The write's outcome.</param>
		/// <param name="context">Optional extra identification, such as a character ID.</param>
		/// <returns>True only if the write succeeded and every supplied row was written.</returns>
		public static async Task<bool> RequireCompleteAsync(
			string tag,
			string operation,
			DatabaseResult<BulkWriteResult> result,
			string context = null)
		{
			string where = string.IsNullOrEmpty(context) ? string.Empty : $" ({context})";

			if (!result.IsSuccess)
			{
				await Log.Error(tag, $"{operation}{where} failed: [{result.ErrorCode}] {result.ErrorMessage}");
				return false;
			}

			BulkWriteResult write = result.Data;
			if (!write.IsComplete)
			{
				await Log.Error(tag,
					$"{operation}{where} was incomplete: {write}. These rows are newly created, so " +
					"nothing newer can exist to supersede them — the batch was rejected or filtered.");
				return false;
			}

			return true;
		}
	}
}
