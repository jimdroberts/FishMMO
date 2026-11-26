using UnityEngine;

namespace FishMMO.Client
{
	/// <summary>Destroys the mesh and materials a duel ground's visual made, with it.</summary>
	public sealed class DuelGroundVisualOwner : MonoBehaviour
	{
		private Object[] owned;

		/// <summary>Takes ownership of what the visual made.</summary>
		public void Own(params Object[] objects)
		{
			owned = objects;
		}

		private void OnDestroy()
		{
			if (owned == null)
			{
				return;
			}
			for (int i = 0; i < owned.Length; ++i)
			{
				if (owned[i] != null)
				{
					Destroy(owned[i]);
				}
			}
			owned = null;
		}
	}
}
