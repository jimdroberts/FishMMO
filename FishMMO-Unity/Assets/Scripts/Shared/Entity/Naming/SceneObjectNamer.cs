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
		public NameCache Template;

		[SerializeField]
		private bool HasName = false;

		protected void Update()
		{
			if (HasName)
			{
				return;
			}
			if (Template == null ||
				Template.Names == null ||
				Template.Names.Count < 1)
			{
				return;
			}
			System.Random r = new System.Random();
			int j = r.Next(0, Template.Names.Count);
			string characterName = Template.Names[j];

			string surname = "";
			Merchant merchant = gameObject.GetComponent<Merchant>();
			if (merchant != null)
			{
				surname = "the Merchant";
			}
			else
			{
				AbilityCrafter abilityCrafter = gameObject.GetComponent<AbilityCrafter>();
				if (abilityCrafter != null)
				{
					surname = "the Ability Crafter";
				}
			}
			this.gameObject.name = (characterName + " " + surname).Trim();
			HasName = true;
			EditorUtility.SetDirty(this);
		}
#endif
	}
}