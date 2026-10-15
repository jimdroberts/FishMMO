using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// What a character's movement, abilities and death tell its animation. The character's own peers call it (its
	/// movement and abilities run there); a client watching someone else hears nothing from that character and reads its
	/// body off what it can see, plus the cast and death broadcasts every observer receives. Nothing here is synced.
	/// </summary>
	public interface ICharacterAnimationController : ICharacterBehaviour
	{
		/// <summary>
		/// The movement's speed band (0 idle, 0.3 crouch-walk, 0.5 walk, 1 run, 1.5 sprint). An implementation may measure the
		/// body's real speed instead, as the game's does.
		/// </summary>
		void SetSpeed(float speed);

		/// <summary>
		/// Sets whether the character is on the ground.
		/// </summary>
		void SetGrounded(bool grounded);

		/// <summary>
		/// Sets whether the character is crouching.
		/// </summary>
		void SetCrouching(bool crouching);

		/// <summary>
		/// Sets whether the character is swimming, and how it lies in the water: <paramref name="pitch"/> degrees, positive
		/// nose down (diving), negative nose up.
		/// </summary>
		void SetSwimming(bool swimming, float pitch);

		/// <summary>
		/// Sets whether the character is climbing a ladder, rope ladder or rope, and how fast: <paramref name="rate"/> is
		/// the climb's speed as a share of the stock climbing speed, positive up and negative down (zero holding still).
		/// </summary>
		void SetClimbing(bool climbing, float rate);

		/// <summary>
		/// Triggers a jump animation.
		/// </summary>
		void TriggerJump();

		/// <summary>
		/// Triggers an attack animation.
		/// </summary>
		void TriggerAttack();

		/// <summary>
		/// Sets the blocking state.
		/// </summary>
		void SetBlocking(bool blocking);

		/// <summary>
		/// Triggers a roll/dodge animation.
		/// </summary>
		void TriggerRoll();

		/// <summary>
		/// Triggers a spell cast animation.
		/// </summary>
		void TriggerCast();

		/// <summary>
		/// Triggers the death animation and suppresses all other animation state.
		/// </summary>
		void TriggerDeath();

		/// <summary>
		/// Resets the death animation state and restores default locomotion params.
		/// Called on resurrection or when death was mispredicted (reconcile correction).
		/// Resets Death trigger, clears Speed to 0, restores Grounded/Crouching defaults,
		/// and clears any lingering Attack/Cast/Roll/Jump/Block state.
		/// </summary>
		void ResetDeath();

		/// <summary>
		/// Enables or disables root motion on the Animator.
		/// </summary>
		void SetRootMotion(bool enabled);
	}
}
