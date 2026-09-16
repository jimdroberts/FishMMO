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
	/// the whole surface shared code needs: the aim the brain solved, the corpse hand-off, facing an
	/// interactor, and the two threat mechanics ECA actions apply. Everything else about AI is the
	/// server's business.
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
