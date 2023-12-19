using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	public class SceneObjectUID : MonoBehaviour
	{
		public readonly static SceneObjectUIDDictionary IDs = new SceneObjectUIDDictionary();

		[Tooltip("Rebuild the World Scene Details Cache in order to generate Unique IDs for Scene Objects.")]
		public int ID = 0;

		protected void Awake()
		{
			IDs[ID] = this;
		}
	}
}