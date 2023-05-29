using FishNet.Object;
using UnityEngine;

[RequireComponent(typeof(Character))]
public class TargetController : NetworkBehaviour
{

	public Character character;
	public LayerMask layerMask;
	public float targetDistance = 50.0f;
	public float targetUpdateRate = 1.0f;
	public float nextTick = 0.0f;
	public TargetInfo lastTarget;
	public TargetInfo current;
	//public UILabel3D targetLabel;

	public override void OnStartClient()
	{
		base.OnStartClient();

		if (character == null || !base.IsOwner)
		{
			enabled = false;
			return;
		}

		//targetLabel = UILabel3D.Create("", 32, transform);
		//targetLabel.enabled = false;
	}

	void Update()
	{
		// should we obtain target info from the client or just do distance checks on interactions..?
		if (base.IsServer)
		{
			enabled = false;
			return;
		}

		if (nextTick <= 0.0f)
		{
			nextTick = targetUpdateRate;

			lastTarget = current;
			Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
			RaycastHit hit;
			if (Physics.Raycast(ray, out hit, targetDistance, layerMask))
			{
				//Debug.DrawLine(ray.origin, hit.point, Color.red, 1);
				//Debug.Log("hit: " + hit.transform.name + " pos: " + hit.point);
				current = new TargetInfo(hit.transform, hit.point);

				// outline?
				if (lastTarget.target != null && current.target != null && lastTarget.target != current.target)
				{
					Outline previous = lastTarget.target.GetComponent<Outline>();
					if (previous != null)
					{
						//targetLabel.enabled = false;
						previous.enabled = false;
					}

					Outline nextOutline = current.target.GetComponent<Outline>();
					if (nextOutline != null)
					{
						//targetLabel.SetPosition(current.target.position);
						//targetLabel.SetText(current.target.name);
						//targetLabel.enabled = true;
						nextOutline.enabled = true;
					}
				}
			}
			else
			{
				current = new TargetInfo(null, Vector3.zero);
			}
		}
		nextTick -= Time.deltaTime;
	}
}