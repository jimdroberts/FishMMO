using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FishMMO.Database.Npgsql.Services
{
	/// <summary>
	/// Helpers for consuming compiled EF Core queries that return a sequence.
	/// </summary>
	/// <remarks>
	/// <para>
	/// EF Core only supports two shapes for <c>EF.CompileAsyncQuery</c>: a scalar/single
	/// result (<c>Task&lt;TResult&gt;</c>, e.g. <c>Count()</c> or <c>FirstOrDefault()</c>)
	/// and a sequence (<c>IAsyncEnumerable&lt;TResult&gt;</c>). Wrapping a sequence query in
	/// a synchronous <c>.ToList()</c> to force it into the <c>Task&lt;List&lt;T&gt;&gt;</c>
	/// overload compiles, but is not a translatable expression tree: every invocation throws
	/// <c>InvalidOperationException: The LINQ expression ... could not be translated</c> at
	/// runtime, which the service layer surfaces as a failed <c>DatabaseResult</c> — a query
	/// that silently returns nothing instead of the rows it was asked for.
	/// </para>
	/// <para>
	/// Sequence-returning compiled queries therefore declare
	/// <c>Func&lt;NpgsqlDbContext, ..., IAsyncEnumerable&lt;TEntity&gt;&gt;</c> and are
	/// materialized through <see cref="MaterializeAsync{T}"/>. The returned func takes no
	/// <see cref="CancellationToken"/> of its own, so the token is applied here, at enumeration.
	/// </para>
	/// </remarks>
	internal static class CompiledQueryExtensions
	{
		/// <summary>
		/// Enumerates a compiled query's results into a list, honouring cancellation.
		/// </summary>
		/// <typeparam name="T">Entity type produced by the query.</typeparam>
		/// <param name="source">The async sequence returned by a compiled query.</param>
		/// <param name="cancellationToken">Token observed while enumerating.</param>
		/// <returns>The materialized results.</returns>
		internal static async Task<List<T>> MaterializeAsync<T>(
			this IAsyncEnumerable<T> source,
			CancellationToken cancellationToken = default)
		{
			var results = new List<T>();
			await foreach (var item in source.WithCancellation(cancellationToken).ConfigureAwait(false))
			{
				results.Add(item);
			}
			return results;
		}
	}
}
