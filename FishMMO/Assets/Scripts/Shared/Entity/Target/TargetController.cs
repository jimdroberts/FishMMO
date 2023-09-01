using UnityEngine;

[RequireComponent(typeof(Character))]
public class TargetController : MonoBehaviour
{
	public const float MAX_TARGET_DISTANCE = 50.0f;
	public const float TARGET_UPDATE_RATE = 1.0f;

	public Character character;
	public LayerMask layerMask;
#if UNITY_CLIENT
	public float nextTick = 0.0f;
	public Ray currentRay;

	public TargetInfo lastTarget;
	public TargetInfo current;
	//public UILabel3D targetLabel;

	void Awake()
	{
		//targetLabel = UILabel3D.Create("", 32, transform);
		//targetLabel.enabled = false;
	}

	void Update()
	{
		// update target label for the client
		if (nextTick <= 0.0f)
		{
			nextTick = TARGET_UPDATE_RATE;

			lastTarget = current;
			currentRay = Camera.main.ScreenPointToRay(Input.mousePosition);
			//Camera.main.ScreenToWorldPoint(new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0));

			current = GetTarget(this, currentRay, MAX_TARGET_DISTANCE);
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
		nextTick -= Time.deltaTime;
	}
#endif

	public static TargetInfo GetTarget(TargetController controller, Ray ray, float maxDistance)
	{
		float distance = maxDistance > MAX_TARGET_DISTANCE ? MAX_TARGET_DISTANCE : maxDistance;
		RaycastHit hit;
		if (Physics.Raycast(ray, out hit, distance, controller.layerMask))
		{
			//Debug.DrawLine(ray.origin, hit.point, Color.red, 1);
			//Debug.Log("hit: " + hit.transform.name + " pos: " + hit.point);
			return new TargetInfo(hit.transform, hit.point);
		}
		return new TargetInfo(null, ray.GetPoint(distance));
	}
}