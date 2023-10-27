using TMPro;
using UnityEngine;
using UnityEngine.UI;
using FishMMO.Shared;

namespace FishMMO.Client
{
	public class ServerDetailsButton : MonoBehaviour
	{
		public Button serverButton;
		public TMP_Text serverLabel;
		public TMP_Text serverStatusLabel;

		public WorldServerDetails Details;
		public delegate void ServerSelectEvent(ServerDetailsButton button);
		public event ServerSelectEvent OnServerSelected;

		public void Initialize(WorldServerDetails details)
		{
			Details = details;
			serverLabel.text = (details.Locked ? "[Locked] " : " ") + details.Name;
			serverStatusLabel.text = details.CharacterCount.ToString();
		}

		public void OnClick_ServerButton()
		{
			OnServerSelected?.Invoke(this);
		}

		public void SetLabelColors(Color color)
		{
			serverLabel.color = color;
			serverStatusLabel.color = color;
		}
	}
}