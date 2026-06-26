using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Centralized animation control interface for characters.
	/// Abstracts Animator parameter setting and integrates with FishNet NetworkAnimator for sync.
	/// </summary>
	public interface ICharacterAnimationController : ICharacterBehaviour
	{
		/// <summary>
		/// Sets the locomotion speed parameter (0=Idle, 0.5=Walk, 1=Run, 1.5=Sprint).
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
