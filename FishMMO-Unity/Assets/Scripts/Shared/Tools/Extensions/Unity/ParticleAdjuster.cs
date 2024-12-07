using UnityEngine;

namespace FishMMO.Shared
{
	[ExecuteInEditMode]
	public class ParticleAdjuster : MonoBehaviour
	{
#if UNITY_EDITOR
		public ParticleSystem ParticleSystem;

		// You can keep the Update method to run the adjustments automatically if needed
		void Update()
		{
			if (ParticleSystem == null)
			{
				ParticleSystem = GetComponent<ParticleSystem>();
			}
		}

		void OnDrawGizmos()
		{
			if (ParticleSystem == null)
			{
				return;
			}

			var shape = ParticleSystem.shape;

			Gizmos.color = Color.green;

			Vector3 position = ParticleSystem.transform.position;

			// Add Particle System Shape position offset
			position += new Vector3(shape.position.x, shape.position.z, -shape.position.y);

			if (shape.shapeType == ParticleSystemShapeType.Cone)
			{
				position.y += 0.5f;
			}

			Gizmos.DrawWireCube(position, new Vector3(1f, 1f, 1f));
		}
#endif
	}
}