using System.Collections.Generic;
using System.Linq;

namespace FishMMO.Database.Data
{
	/// <summary>
	/// Extension methods for working with versioned DTOs.
	/// These helpers ensure callers explicitly increment versions before persist operations.
	/// Because DTOs are immutable structs, these methods return new instances with incremented versions.
	/// </summary>
	/// <threadsafety static="true" instance="true">
	/// All methods in this class are thread-safe and async-safe:
	/// <list type="bullet">
	///   <item><description>No static state is maintained - all methods are pure functions.</description></item>
	///   <item><description>Input DTOs are value types (structs) copied by value on each call.</description></item>
	///   <item><description>All operations return new instances rather than mutating inputs.</description></item>
	///   <item><description>Multiple threads can safely call these methods on the same source data simultaneously.</description></item>
	/// </list>
	/// <para>
	/// <b>Note:</b> When using <see cref="WithIncrementedVersions{T}"/> or <see cref="WithIncrementedVersionsToList{T}"/>,
	/// the source <see cref="IEnumerable{T}"/> must not be modified during enumeration (standard .NET collection thread-safety).
	/// The extension methods themselves do not modify the source collection.
	/// </para>
	/// </threadsafety>
	public static class VersionExtensions
	{
		/// <summary>
		/// Returns a new DTO with an incremented version for fluent chaining.
		/// Call this immediately before passing the DTO to a persist method.
		/// </summary>
		/// <typeparam name="T">A struct type implementing <see cref="IVersioned{T}"/>.</typeparam>
		/// <param name="data">The versioned DTO to increment.</param>
		/// <returns>A new DTO instance with an incremented version.</returns>
		/// <example>
		/// <code>
		/// await factionService.PersistAsync(new[] { factionData.WithIncrementedVersion() }, ct);
		/// </code>
		/// </example>
		public static T WithIncrementedVersion<T>(this T data) where T : struct, IVersioned<T>
		{
			return data.WithVersion(data.Version + 1);
		}

		/// <summary>
		/// Returns new DTOs with incremented versions for all items in a collection.
		/// Call this immediately before passing the collection to a persist method.
		/// </summary>
		/// <typeparam name="T">A struct type implementing <see cref="IVersioned{T}"/>.</typeparam>
		/// <param name="data">The collection of versioned DTOs to increment.</param>
		/// <returns>An enumerable of new DTO instances with incremented versions.</returns>
		/// <example>
		/// <code>
		/// await factionService.PersistAsync(factions.WithIncrementedVersions().ToList(), ct);
		/// </code>
		/// </example>
		public static IEnumerable<T> WithIncrementedVersions<T>(this IEnumerable<T> data) where T : struct, IVersioned<T>
		{
			foreach (var item in data)
			{
				yield return item.WithVersion(item.Version + 1);
			}
		}

		/// <summary>
		/// Returns a new list with all items having incremented versions.
		/// More efficient than <see cref="WithIncrementedVersions{T}"/> when you need a list result.
		/// </summary>
		/// <typeparam name="T">A struct type implementing <see cref="IVersioned{T}"/>.</typeparam>
		/// <param name="data">The list of versioned DTOs to increment.</param>
		/// <returns>A new list with all items' versions incremented.</returns>
		/// <example>
		/// <code>
		/// await factionService.PersistAsync(factionList.WithIncrementedVersionsToList(), ct);
		/// </code>
		/// </example>
		public static List<T> WithIncrementedVersionsToList<T>(this IEnumerable<T> data) where T : struct, IVersioned<T>
		{
			return data.Select(item => item.WithVersion(item.Version + 1)).ToList();
		}
	}
}