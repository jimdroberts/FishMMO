using UnityEngine;
using KinematicCharacterController;

namespace KCCPredictionV2
{
	public class KCCInitializer : MonoBehaviour
	{
		private void Awake()
		{
			KinematicCharacterSystem.EnsureCreation();
			KinematicCharacterSystem.Settings.AutoSimulation = false;
		}
	}
}