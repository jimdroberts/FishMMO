using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace FishMMO.Shared
{
	[ExecuteInEditMode]
	public class SceneObjectUID : MonoBehaviour
	{
		private static long LastID;
		public static Dictionary<long, SceneObjectUID> IDs = new Dictionary<long, SceneObjectUID>();

		public long ID;

		protected void Awake()
		{
#if !UNITY_EDITOR
			IDs[ID] = this;
		}
#else
			if (ID != 0 && IDs.ContainsKey(ID))
			{
				return;
			}

			if (string.IsNullOrWhiteSpace(gameObject.scene.name))
			{
				return;
			}

			do
			{
				ID = ++LastID;
			}
			while (IDs.ContainsKey(ID));
			IDs[ID] = this;

			EditorUtility.SetDirty(this);
		}

		protected void OnDestroy()
		{
			// remove the ID from the list if the object was deleted
			if (gameObject.scene.isLoaded)
			{
				Debug.Log("SceneObjectUID: Deleted[" + ID + "]");
				IDs.Remove(ID);
			}
		}
#endif
	}
}