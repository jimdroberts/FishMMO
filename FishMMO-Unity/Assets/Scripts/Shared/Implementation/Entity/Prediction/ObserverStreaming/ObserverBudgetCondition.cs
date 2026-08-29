using FishNet.Connection;
using FishNet.Observing;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Caps how many CHARACTERS one client observes at a time, keeping the most relevant.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Distance alone does not bound a client's cost: a hundred players can stand inside any radius
	/// you pick. This is the bound. <see cref="ObserverStreamingRegistry"/> already ranks every
	/// character a viewer could see by relevance — combat, party, guild, distance — and this
	/// condition simply admits the top <c>ObserverStreamingPolicy.VisibilityBudget</c> of them.
	/// </para>
	/// <para>
	/// <b>It gates existence, not fidelity.</b> A character outside the budget is not merely slowed
	/// down; it is despawned on that client, taking its audio, nameplate, buffs and every broadcast
	/// with it. That is why it must be the LAST condition — everything cheaper should have rejected
	/// first — and why it is rank-hysteretic: two characters of near-identical score either side of
	/// the boundary would otherwise spawn and despawn each other every pass, which costs far more
	/// than any rate change and reads as flicker.
	/// </para>
	/// <para>
	/// <b>It only budgets registered characters.</b> Anything else — interactables, world items,
	/// scene objects, a character whose entry has not been created yet — is admitted untouched, so
	/// this is safe to install as a default condition on every object. Their absence would be far
	/// more confusing than a missing distant stranger, and they are cheap.
	/// </para>
	/// <para>
	/// Party members within the ability ceiling and the viewer's current target are pinned by the
	/// registry and can never be evicted, however dense the crowd gets.
	/// </para>
	/// </remarks>
	[CreateAssetMenu(menuName = "FishMMO/Observers/Observer Budget Condition", fileName = "ObserverBudgetCondition")]
	public class ObserverBudgetCondition : ObserverCondition
	{
		/// <summary>
		/// Whether <paramref name="connection"/> may observe this object under its visibility budget.
		/// </summary>
		/// <param name="connection">The connection being tested.</param>
		/// <param name="currentlyAdded">True when the connection already observes this object.</param>
		/// <param name="notProcessed">
		/// True when this condition could not decide, which tells FishNet to keep the previous
		/// result. Used while a viewer has no ranking yet: the alternative — answering "false" —
		/// would hide the world from every client that connects between two scheduler passes.
		/// </param>
		/// <returns>True when the object is inside the budget.</returns>
		public override bool ConditionMet(NetworkConnection connection, bool currentlyAdded, out bool notProcessed)
		{
			notProcessed = false;

			if (connection == null || NetworkObject == null)
			{
				return true;
			}

			/* Not a registered character: not this condition's business. Interactables, world items
			 * and scene objects are governed by their own distance conditions, and a character is
			 * registered from OnStartServer, so there is a window during spawn where its entry does
			 * not exist yet. Admitting is right in every one of those cases. */
			if (ObserverStreamingRegistry.Get(NetworkObject) == null)
			{
				return true;
			}

			bool withinBudget = ObserverStreamingRegistry.IsWithinVisibilityBudget(
				connection.ClientId, NetworkObject.ObjectId, currentlyAdded, out bool hasRanking);

			if (!hasRanking)
			{
				/* No pass has ranked this viewer yet — it has just connected, or the scheduler has
				 * not reached it. Defer rather than reject, so the first seconds of a session are
				 * not spent with an empty world that pops in all at once. */
				notProcessed = true;
				return currentlyAdded;
			}

			return withinBudget;
		}

		/// <summary>
		/// Timed: the ranking changes as characters move and fight, so this has to be re-evaluated
		/// on the observer sweep rather than only when something structural happens.
		/// </summary>
		public override ObserverConditionType GetConditionType() => ObserverConditionType.Timed;
	}
}
