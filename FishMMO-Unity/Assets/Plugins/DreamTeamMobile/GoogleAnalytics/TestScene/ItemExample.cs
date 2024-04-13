using UnityEngine;
using UnityEngine.UI;
using System;
using System.Collections.Generic;

namespace DreamTeamMobile
{
	public class ItemExample : MonoBehaviour
	{
		[Serializable]
		public class Item
		{
			public int item_id;
			public string item_name;

			public Item(int id, string name)
			{
				item_id = id;
				item_name = name;
			}
		}

		public Text text;

		private void Start()
		{
			List<Item> items = new List<Item>
			{
				new Item(12345, "Stan and Friends Tee"),
				new Item(12346, "Google Grey Women's Tee")
			};

			List<Item> guildNames = new List<Item>
			{
				new Item(12, "Happy Tree Friends"),
				new Item(123, "Kyle's Mom")
			};

			Dictionary<string, List<Item>> container = new Dictionary<string, List<Item>>
			{
				{ "items", items },
				{ "guildNames", guildNames },
			};

			GoogleAnalytics.Instance.TrackEvent("Purchases", container);
		}
	}
}