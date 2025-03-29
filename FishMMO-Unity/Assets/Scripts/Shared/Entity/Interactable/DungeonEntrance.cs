using UnityEngine;

namespace FishMMO.Shared
{
	public class DungeonEntrance : Interactable
	{
		private string title = "Dungeon";

		public string DungeonName;

		public override string Title { get { return title; } }
	}
}