using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Marks a character model prefab's root (the GameObject with its <see cref="Animator"/>) and names the clips it is
	/// animated with. <see cref="CharacterAnimationController"/> finds it under the character's MeshRoot when the model is
	/// ready and builds a <see cref="CharacterBody"/> on it.
	/// </summary>
	/// <remarks>
	/// The model stands with its origin between its feet at the capsule's base, facing +Z, and its Animator carries a
	/// Humanoid avatar and no controller: the body plays the clips itself.
	/// </remarks>
	[DisallowMultipleComponent]
	[RequireComponent(typeof(Animator))]
	public sealed class CharacterModel : MonoBehaviour
	{
		[Tooltip("The clips this model is animated with.")]
		public CharacterAnimationSet Animations;
	}
}
