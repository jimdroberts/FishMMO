using System;
using System.Collections.Generic;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.Server.Core.World.SceneServer
{
	/// <summary>
	/// Engine-agnostic public API for granting crafted abilities on scene servers.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The one funnel every path that gives a character a new <see cref="Ability"/> goes through.
	/// It exists because there were three copies of "build the ability, learn it, persist it" and
	/// only the item layer had worked out what they were missing: the database mints the row's
	/// identity, and an ability whose <see cref="Ability.ID"/> is still the constructor's
	/// <c>-1</c> is filed into <c>KnownAbilities[-1]</c> — so a second craft silently replaced the
	/// first, and a hotkey bound to either one resolved against the sentinel.
	/// </para>
	/// <para>
	/// The identity is therefore applied BEFORE the ability is learned. That is why the learn is a
	/// continuation of the persist rather than something the caller does when this returns: an
	/// ability has no slot to lock while its identity is unknown, and the instance id is its key on
	/// the server, in the client's known-ability table, in every activation broadcast, and inside
	/// every persisted hotkey row. See <c>AbilitySystem</c> for the item-layer comparison.
	/// </para>
	/// </remarks>
	public interface IAbilitySystem : IServerBehaviour
	{
		/// <summary>
		/// Builds and grants a new crafted ability to <paramref name="character"/>.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>Asynchronous by contract.</b> A <c>true</c> return means the request was admitted,
		/// not that the ability exists yet. Exactly one of <paramref name="onGranted"/> or
		/// <paramref name="onFailed"/> runs later, on the main thread, once the database has
		/// answered.
		/// </para>
		/// <para>
		/// <b>This method takes ownership of <paramref name="releaseGuard"/>.</b> It is invoked
		/// exactly once on whichever path finishes the request — a synchronous refusal, a refused
		/// enqueue, or the persist's continuation — so a caller must NOT also release it, and a
		/// caller that holds a request-open guard across this call passes the release in here
		/// instead of running it in its own <c>finally</c>.
		/// </para>
		/// <para>
		/// A refusal is synchronous and already reports itself through the return value, so
		/// <paramref name="onFailed"/> is reserved for a persist that was attempted and did not
		/// land. Callers answer a <c>false</c> return themselves.
		/// </para>
		/// </remarks>
		/// <param name="character">The character receiving the ability.</param>
		/// <param name="template">The ability template the crafted ability is built from.</param>
		/// <param name="events">The crafted event template ids to attach, or null for none.</param>
		/// <param name="releaseGuard">
		/// Releases the caller's in-flight guard. Always invoked, exactly once, by this system.
		/// </param>
		/// <param name="onGranted">Runs on the main thread once the ability is learned and broadcast.</param>
		/// <param name="onFailed">Runs on the main thread when the ability could not be recorded.</param>
		/// <returns><c>true</c> when the grant was admitted; <c>false</c> when it was refused outright.</returns>
		bool TryGrantAbility(
			IPlayerCharacter character,
			AbilityTemplate template,
			IReadOnlyList<int> events,
			Action releaseGuard,
			Action<IPlayerCharacter, Ability> onGranted,
			Action<IPlayerCharacter> onFailed);
	}
}
