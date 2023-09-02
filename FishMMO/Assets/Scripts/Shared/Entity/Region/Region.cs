using System.Collections.Generic;
using UnityEngine;

public class Region : MonoBehaviour
{
    public string RegionName;
    public Collider Collider;
	[Tooltip("Add a terrain if you would like the region to span the entire map. (Requires BoxCollider)")]
	public Terrain Terrain;

	public List<RegionAction> OnUpdate = new List<RegionAction>();
	public List<RegionAction> OnRegionEnter = new List<RegionAction>();
    public List<RegionAction> OnRegionStay = new List<RegionAction>();
    public List<RegionAction> OnRegionExit = new List<RegionAction>();

	void Awake()
	{
		Collider = gameObject.GetComponent<Collider>();
		if (Collider == null)
		{
			Debug.Log(RegionName + " collider is null and will not function properly.");
			return;
		}
		// set the collider to trigger just incase we forgot to set it in the inspector
		Collider.isTrigger = true;
		
		// terrain bounds override the collider
		if (Terrain != null)
		{
			BoxCollider box = Collider as BoxCollider;
			if (box != null)
			{
				box.size = Terrain.terrainData.size;
			}
		}
	}

#if UNITY_EDITOR
	public Color RegionColor = Color.red;

	void OnDrawGizmosSelected()
	{
		Gizmos.color = RegionColor;

		BoxCollider box = Collider as BoxCollider;
		if (box != null)
		{
			Gizmos.DrawWireCube(transform.position, box.size);
			return;
		}
		else
		{
			SphereCollider sphere = Collider as SphereCollider;
			if (sphere != null)
			{
				Gizmos.DrawWireSphere(transform.position, sphere.radius);
				return;
			}
		}
	}
#endif

	private void OnTriggerEnter(Collider other)
	{
		Character character = other.GetComponent<Character>();
		if (character != null)
		{
			foreach (RegionAction action in OnRegionEnter)
			{
				action.Invoke(character, this);
			}
		}
	}

	private void OnTriggerStay(Collider other)
	{
		Character character = other.GetComponent<Character>();
		if (character != null)
		{
			foreach (RegionAction action in OnRegionStay)
			{
				action.Invoke(character, this);
			}
		}
	}

	private void OnTriggerExit(Collider other)
	{
		Character character = other.GetComponent<Character>();
		if (character != null)
		{
			foreach (RegionAction action in OnRegionExit)
			{
				action.Invoke(character, this);
			}
		}
	}
}