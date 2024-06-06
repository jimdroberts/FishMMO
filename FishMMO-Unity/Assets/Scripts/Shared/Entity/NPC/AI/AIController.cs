using System.Collections.Generic;

namespace FishMMO.Shared
{
	public class AIController : CharacterBehaviour
	{
		public AIState Previous;
		public AIState Current;
		public float MinStateChangeDuration;
		public float MaxStateChangeDuration;
		public bool RandomizeState;
		public List<AIState> AllowedStates;

		private float currentStateChangeTime;
	}
}