using UnityEngine;

namespace FishMMO.Shared.Core
{
	/// <summary>
	/// The server-side brain of an NPC, as seen from shared code.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The brain itself lives in the server assembly and is attached to an NPC only when the
	/// server spawns it, so a client never has one and shared code cannot name its type. This is
	/// the whole surface shared code needs: the aim the brain solved, whether it is evading, the
	/// corpse hand-off, facing an interactor, and the two threat mechanics ECA actions apply.
	/// Everything else about AI is the server's business.
	/// </para>
	/// <para>
	/// Resolved through <see cref="ICharacter"/>'s behaviour registry like any other controller,
	/// so <c>character.TryGet(out INPCBrain brain)</c> answers false on every client and on every
	/// player.
	/// </para>
	/// </remarks>
	public interface INPCBrain : ICharacterBehaviour
	{
		/// <summary>
		/// The rotation whose forward vector is the direction this NPC aims, refreshed every
		/// network tick.
		/// </summary>
		Quaternion AimRotation { get; }

		/// <summary>
		/// True while the NPC is walking home after a leash pulled it out of a fight. An evading
		/// NPC takes no damage, no threat, no taunt, no debuff from another character and no
		/// knockback, and a hit on it is reported to the attacker as <c>Evade</c>. Shared code asks
		/// through <c>CharacterEvade.RefusesHostileEffects</c>, not here directly.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The evade is what stops a leash being farmed: without it a player could drag a mob to the
		/// edge of its leash and hit it all the way home, or hit it once on the way to reset the
		/// fight on their own terms. Server-side only — a client has no brain, so this is never
		/// true there.
		/// </para>
		/// <para>
		/// Separate from <see cref="ICharacterDamageController.Immortal"/> on purpose. Immortal is
		/// authored on the prefab (a training dummy, a quest giver), set on corpses and toggled by
		/// administrators; the brain borrowing it for an evade would have to save and restore it
		/// around every way out of the return, and one missed path would leave a mob unkillable
		/// for the rest of its life. The evade is derived from the brain's current state instead,
		/// so it ends whenever the return does, however it ends.
		/// </para>
		/// </remarks>
		bool IsEvading { get; }

		/// <summary>
		/// Stops the brain for a corpse: no thinking, no movement, no target, no threat.
		/// </summary>
		/// <returns>True when the brain was running and has been stopped by this call.</returns>
		bool SuspendForCorpse();

		/// <summary>
		/// Starts a brain that <see cref="SuspendForCorpse"/> stopped.
		/// </summary>
		void ResumeAfterCorpse();

		/// <summary>
		/// Turns to face a character that is interacting with this NPC and stops wandering.
		/// </summary>
		/// <param name="interactor">The interacting character's transform.</param>
		void FaceInteractor(Transform interactor);

		/// <summary>
		/// Taunts this NPC: threat for <paramref name="taunter"/>, optionally enough to put it on top
		/// of the table, and optionally an immediate target switch.
		/// </summary>
		/// <param name="taunter">The taunting character.</param>
		/// <param name="threatPoints">Flat threat added.</param>
		/// <param name="guaranteeTopThreat">When true, adds at least enough to outscore every other entry.</param>
		/// <param name="leadOverHighest">Margin above the previous highest score when guaranteeing top threat.</param>
		/// <param name="forceImmediateTargetSwitch">When true, the NPC switches to the taunter now.</param>
		void ApplyTaunt(ICharacter taunter, float threatPoints, bool guaranteeTopThreat, float leadOverHighest, bool forceImmediateTargetSwitch);

		/// <summary>
		/// Records a nearby cast against an NPC that is already in combat.
		/// </summary>
		/// <param name="caster">The casting character.</param>
		/// <param name="threatPoints">Flat threat added, only while the NPC already has a threat table.</param>
		/// <param name="resourceSpent">Resource the cast spent, weighted by the NPC's own resource weight.</param>
		void ApplyAreaThreat(ICharacter caster, float threatPoints, int resourceSpent);
	}
}
