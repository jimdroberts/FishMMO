using FishNet.Object;

public class Interactable : NetworkBehaviour, IInteractable
{
	public float interactionDistanceSqr;

	public virtual bool OnInteract(Character character)
	{
		return ((character.transform.position - transform.position).sqrMagnitude < interactionDistanceSqr);
	}
}