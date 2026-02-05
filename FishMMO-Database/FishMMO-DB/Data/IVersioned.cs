namespace FishMMO.Database.Data
{
	/// <summary>
	/// Marker interface for immutable DTOs that carry a logical version for optimistic concurrency.
	/// Implement this interface on any Data transfer object that participates in version-gated persistence.
	/// </summary>
	/// <typeparam name="T">The concrete DTO type (for fluent return types).</typeparam>
	/// <threadsafety static="true" instance="true">
	/// This interface is designed for immutable value types (structs with readonly fields).
	/// All implementations are inherently thread-safe because:
	/// <list type="bullet">
	///   <item><description>Structs are copied by value, so each thread operates on its own copy.</description></item>
	///   <item><description>All fields are readonly, preventing mutation after construction.</description></item>
	///   <item><description><see cref="WithVersion"/> returns a new instance rather than mutating state.</description></item>
	/// </list>
	/// No synchronization is required when using implementations of this interface across threads.
	/// </threadsafety>
	public interface IVersioned<T> where T : struct, IVersioned<T>
	{
		/// <summary>
		/// The authoritative logical version. Services use this value to gate writes:
		/// only writes with a version greater than the persisted version will succeed.
		/// </summary>
		long Version { get; }

		/// <summary>
		/// Returns a new instance of this DTO with the specified version.
		/// Used by extension methods to create incremented copies.
		/// </summary>
		/// <param name="newVersion">The new version value.</param>
		/// <returns>A new instance with the updated version.</returns>
		T WithVersion(long newVersion);
	}
}