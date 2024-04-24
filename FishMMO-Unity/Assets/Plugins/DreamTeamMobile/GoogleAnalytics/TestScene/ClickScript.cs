using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using System;

namespace DreamTeamMobile
{
	public class ClickScript : MonoBehaviour
    {
        public Text text;

        void OnMouseDown()
        {
            if (gameObject.tag == "Event1")
            {
                GoogleAnalytics.Instance.TrackEvent("Event_Green", new Dictionary<string, object>
                {
                    { "event_name", "mouse_click" },
                    { "event_value", 1000 }
                });
                text.text = "An event GREEN has been sent to Google Analytics GA4";
            }
            else if (gameObject.tag == "Event2")
            {
                GoogleAnalytics.Instance.TrackEvent("Event_Blue", new Dictionary<string, object>
                {
                    { "items", new[]
                        {
                            new Item { item_id = "SKU_123A", item_name = "Item1", item_price = 123 },
                            new Item { item_id = "SKU_123B", item_name = "Item2", item_price = 234 }
                        }
                    }
                });
                text.text = "An event BLUE has been sent to Google Analytics GA4";
            }
            else if (gameObject.tag == "Event3")
            {
                GoogleAnalytics.Instance.TrackEvent("Event_Red", new Dictionary<string, object>());
                text.text = "An event RED has been sent to Google Analytics GA4";
            }
        }
    }

    [Serializable]
    public class Item
    {
        public string item_id;
        public string item_name;
        public int item_price;
    }
}