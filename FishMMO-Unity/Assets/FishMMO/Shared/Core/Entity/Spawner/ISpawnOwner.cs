namespace FishMMO.Shared.Core
{
	/// <summary>
	/// The server-side spawner an <see cref="ISpawnable"/> belongs to.
	/// </summary>
	/// <remarks>
	/// Only the hand-back is visible to shared code. The spawner decides what a despawn means —
	/// return the object to the pool and schedule its respawn — and an entity with no owner
	/// despawns itself directly.
	/// </remarks>
	public interface ISpawnOwner
	{
		/// <summary>
		/// Returns <paramref name="spawnable"/> to the pool and schedules its respawn.
		/// </summary>
		/// <param name="spawnable">An entity this spawner created.</param>
		void Despawn(ISpawnable spawnable);
	}
}
