using System.Threading;
using System.Threading.Tasks;

namespace FishMMO.Database.Npgsql.Services.Interfaces.Actions
{
	/// <summary>
	/// Defines a persist operation.
	/// </summary>
	/// <remarks>
	/// Intended for service interfaces that expose a single-item persistence operation.
	/// The persistence semantics (insert/update, version gating, etc.) are defined by the specialized service.
	/// </remarks>
	/// <typeparam name="TRequest">The data required to persist.</typeparam>
	public interface IPersistAction<TRequest>
	{
		/// <summary>
		/// Persists the provided data.
		/// </summary>
		/// <param name="request">The data to persist.</param>
		/// <param name="cancellationToken">Token to cancel the operation.</param>
		Task<DatabaseResult> PersistAsync(TRequest request, CancellationToken cancellationToken = default);
	}

	/// <summary>
	/// Defines a persist operation that returns a value on success.
	/// </summary>
	/// <remarks>
	/// Intended for service interfaces where persistence returns a derived value (e.g. generated row ID).
	/// </remarks>
	/// <typeparam name="TRequest">The data required to persist.</typeparam>
	/// <typeparam name="TResult">The value returned on success.</typeparam>
	public interface IPersistAction<TRequest, TResult>
	{
		/// <summary>
		/// Persists the provided data.
		/// </summary>
		/// <param name="request">The data to persist.</param>
		/// <param name="cancellationToken">Token to cancel the operation.</param>
		Task<DatabaseResult<TResult>> PersistAsync(TRequest request, CancellationToken cancellationToken = default);
	}
}