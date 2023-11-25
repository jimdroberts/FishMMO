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

#if !UNITY_EDITOR
		protected void Awake()
		{
			IDs[ID] = this;
		}
#endif

#if UNITY_EDITOR
		protected void Update()
		{
			if (ID > 0)
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
			IDs.Add(ID, this);

			EditorUtility.SetDirty(this);
		}

		protected void OnDestroy()
		{
			IDs.Remove(ID);
		}
#endif
	}
}