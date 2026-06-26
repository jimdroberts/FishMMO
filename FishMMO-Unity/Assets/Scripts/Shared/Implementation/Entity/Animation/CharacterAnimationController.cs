using FishMMO.Shared.Core;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Centralized animation controller for characters.
	/// Sets parameters on the character's Animator. When combined with FishNet's
	/// NetworkAnimator component (on BaseCharacter), all parameter changes are
	/// automatically synchronized to remote clients.
	///
	/// Animator parameter names are constants to ensure consistency.
	///
	/// Implements <see cref="IModelReadyHandler"/> to re-acquire the Animator
	/// reference after the character model finishes loading asynchronously.
	/// </summary>
	public class CharacterAnimationController : CharacterBehaviour, ICharacterAnimationController, IModelReadyHandler
	{
		// ── Animator parameter name constants ────────────────────────────

		private static readonly int ParamSpeed = Animator.StringToHash("Speed");
		private static readonly int ParamIsGrounded = Animator.StringToHash("IsGrounded");
		private static readonly int ParamIsCrouching = Animator.StringToHash("IsCrouching");
		private static readonly int ParamJump = Animator.StringToHash("Jump");
		private static readonly int ParamAttack = Animator.StringToHash("Attack");
		private static readonly int ParamBlock = Animator.StringToHash("Block");
		private static readonly int ParamRoll = Animator.StringToHash("Roll");
		private static readonly int ParamCast = Animator.StringToHash("Cast");
		private static readonly int ParamDeath = Animator.StringToHash("Death");

		/// <summary>
		/// Cached Animator reference from the instantiated character model.
		/// May be null if model hasn't loaded yet. Re-acquired in <see cref="OnModelReady"/>.
		/// </summary>
		private Animator animator;

		/// <summary>
		/// Attempts initial discovery. May fail if model isn't loaded.
		/// <see cref="OnModelReady"/> provides the guaranteed re-acquisition.
		/// </summary>
		public override void OnStartCharacter()
		{
			base.OnStartCharacter();
#if !UNITY_SERVER
			TryAcquireAnimator();
#endif
		}

		/// <inheritdoc />
		public void OnModelReady()
		{
#if !UNITY_SERVER
			TryAcquireAnimator();
#endif
		}

#if !UNITY_SERVER
		/// <summary>
		/// Attempts to find the Animator. Gets it from the NetworkAnimator if available
		/// (set by BaseCharacter after model load), or falls back to searching MeshRoot.
		/// </summary>
		private void TryAcquireAnimator()
		{
			if (Character == null) return;

			// Primary source: NetworkAnimator (wired by BaseCharacter after model load)
			if (Character is BaseCharacter baseChar && baseChar.NetworkAnimator != null)
			{
				animator = baseChar.NetworkAnimator.Animator;
				if (animator != null) return;
			}

			// Fallback: search MeshRoot
			Transform meshRoot = Character.MeshRoot;
			if (meshRoot != null)
			{
				animator = meshRoot.GetComponentInChildren<Animator>();
			}
		}
#endif

		/// <inheritdoc />
		public void SetSpeed(float speed)
		{
#if !UNITY_SERVER
			if (animator != null)
			{
				animator.SetFloat(ParamSpeed, speed);
			}
#endif
		}

		/// <inheritdoc />
		public void SetGrounded(bool grounded)
		{
#if !UNITY_SERVER
			if (animator != null)
			{
				animator.SetBool(ParamIsGrounded, grounded);
			}
#endif
		}

		/// <inheritdoc />
		public void SetCrouching(bool crouching)
		{
#if !UNITY_SERVER
			if (animator != null)
			{
				animator.SetBool(ParamIsCrouching, crouching);
			}
#endif
		}

		/// <inheritdoc />
		public void TriggerJump()
		{
#if !UNITY_SERVER
			if (animator != null)
			{
				animator.SetTrigger(ParamJump);
			}
#endif
		}

		/// <inheritdoc />
		public void TriggerAttack()
		{
#if !UNITY_SERVER
			if (animator != null)
			{
				animator.SetTrigger(ParamAttack);
			}
#endif
		}

		/// <inheritdoc />
		public void SetBlocking(bool blocking)
		{
#if !UNITY_SERVER
			if (animator != null)
			{
				animator.SetBool(ParamBlock, blocking);
			}
#endif
		}

		/// <inheritdoc />
		public void TriggerRoll()
		{
#if !UNITY_SERVER
			if (animator != null)
			{
				animator.SetTrigger(ParamRoll);
			}
#endif
		}

		/// <inheritdoc />
		public void TriggerCast()
		{
#if !UNITY_SERVER
			if (animator != null)
			{
				animator.SetTrigger(ParamCast);
			}
#endif
		}

		/// <inheritdoc />
		public void TriggerDeath()
		{
#if !UNITY_SERVER
			if (animator != null)
			{
				animator.SetTrigger(ParamDeath);
			}
#endif
		}

		/// <inheritdoc />
		public void SetRootMotion(bool enabled)
		{
#if !UNITY_SERVER
			if (animator != null)
			{
				animator.applyRootMotion = enabled;
			}
#endif
		}
	}
}
