using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Client
{
	public class UIMenu : UIControl
	{
		public override void OnStarting()
		{
		}

		public override void OnDestroying()
		{
		}

		public void OnButtonOptions()
		{
		}

		public void OnButtonQuitToLogin()
		{
			Client.QuitToLogin();
		}

		public void OnButtonQuit()
		{
			Client.Quit();
		}
	}
}