using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace FishMMO.Shared
{
	[ExecuteInEditMode]
	public class SceneObjectNamer : MonoBehaviour
	{
#if UNITY_EDITOR
		public NameCache FirstNames;
		public NameCache Surnames;

		[SerializeField]
		private bool HasName = false;

		protected void Update()
		{
			if (HasName)
			{
				return;
			}
			if (FirstNames == null ||
				FirstNames.Names == null ||
				FirstNames.Names.Count < 1)
			{
				return;
			}
			System.Random r = new System.Random();
			int j = r.Next(0, FirstNames.Names.Count);
			string characterName = FirstNames.Names[j];

			if (Surnames == null ||
				Surnames.Names == null ||
				Surnames.Names.Count < 1)
			{
				return;
			}
			j = r.Next(0, Surnames.Names.Count);
			characterName += $" {Surnames.Names[j]}";

			this.gameObject.name = characterName.Trim();
			HasName = true;
			EditorUtility.SetDirty(this);
		}
#endif
	}
}