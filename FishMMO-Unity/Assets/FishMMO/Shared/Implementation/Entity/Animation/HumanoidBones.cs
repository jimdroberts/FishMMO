using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Finds a humanoid model's bones by its avatar's own map of them.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Read from the avatar's human description rather than <see cref="Animator.GetBoneTransform"/>, which answers null
	/// until the Animator is initialised, and one with no controller and no graph yet is not. <see cref="CharacterBody"/>
	/// finds the feet it sets on the ground this way, and anything that rides a bone (a buff effect at the ankles) asks
	/// the same question the same way, so the two can never disagree about where a foot is.
	/// </para>
	/// </remarks>
	public static class HumanoidBones
	{
		/// <summary>
		/// The transform the avatar maps to <paramref name="bone"/>, searched for under <paramref name="searchRoot"/>.
		/// </summary>
		/// <param name="animator">The model's animator. Null, or one with no humanoid avatar, finds nothing.</param>
		/// <param name="searchRoot">Where to look for the named bone; the animator's own transform when null.</param>
		/// <param name="bone">The human bone wanted.</param>
		/// <returns>The bone, or null when the model does not map it.</returns>
		public static Transform Find(Animator animator, Transform searchRoot, HumanBodyBones bone)
		{
			if (animator == null || bone < 0 || bone >= HumanBodyBones.LastBone)
			{
				return null;
			}
			Avatar avatar = animator.avatar;
			if (avatar != null && avatar.isHuman)
			{
				string human = HumanTrait.BoneName[(int)bone];
				Transform root = searchRoot != null ? searchRoot : animator.transform;
				foreach (HumanBone mapped in avatar.humanDescription.human)
				{
					if (mapped.humanName == human)
					{
						Transform found = Named(root, mapped.boneName);
						if (found != null)
						{
							return found;
						}
					}
				}
			}
			return animator.isInitialized && animator.isHuman ? animator.GetBoneTransform(bone) : null;
		}

		/// <summary>The first transform named <paramref name="name"/> at or under <paramref name="parent"/>, depth first.</summary>
		public static Transform Named(Transform parent, string name)
		{
			if (parent == null)
			{
				return null;
			}
			if (parent.name == name)
			{
				return parent;
			}
			for (int i = 0; i < parent.childCount; i++)
			{
				Transform found = Named(parent.GetChild(i), name);
				if (found != null)
				{
					return found;
				}
			}
			return null;
		}
	}
}
