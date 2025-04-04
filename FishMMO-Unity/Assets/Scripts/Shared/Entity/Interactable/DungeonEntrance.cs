#if !UNITY_SERVER
using UnityEngine.UI;
#endif

namespace FishMMO.Shared
{
	public class DungeonEntrance : Interactable
	{
		private string title = "Dungeon";

#if !UNITY_SERVER
		public Image DungeonImage;
#endif
		public string DungeonName;

		public override string Title { get { return title; } }
	}
}