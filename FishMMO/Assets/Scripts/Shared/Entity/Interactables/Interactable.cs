using FishNet.Object;
using UnityEngine;

public class Interactable : NetworkBehaviour, IInteractable
{
	public float interactionDistanceSqr;

	public Transform Transform { get; private set; }

	void Awake()
	{
		Transform = transform;
	}

	public virtual bool OnInteract(Character character)
	{
		return ((character.Transform.position - Transform.position).sqrMagnitude < interactionDistanceSqr);
	}
}