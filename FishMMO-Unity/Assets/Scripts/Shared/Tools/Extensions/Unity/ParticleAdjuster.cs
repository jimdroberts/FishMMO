using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Utility MonoBehaviour for adjusting and visualizing particle system shapes in the editor.
	/// </summary>
	[ExecuteInEditMode]
	public class ParticleAdjuster : MonoBehaviour
	{
#if UNITY_EDITOR
		/// <summary>
		/// The ParticleSystem component to adjust and visualize.
		/// </summary>
		public ParticleSystem ParticleSystem;

		/// <summary>
		/// Ensures the ParticleSystem reference is set. Runs every frame in the editor.
		/// </summary>
		void Update()
		{
			if (ParticleSystem == null)
			{
				ParticleSystem = GetComponent<ParticleSystem>();
			}
		}

		/// <summary>
		/// Draws a wireframe cube gizmo at the particle system's shape position in the editor.
		/// </summary>
		void OnDrawGizmos()
		{
			if (ParticleSystem == null)
			{
				return;
			}

			var shape = ParticleSystem.shape;

			Gizmos.color = Color.green;

			Vector3 position = ParticleSystem.transform.position;

			// Add Particle System Shape position offset. Note: shape.position.y is negated for correct orientation.
			position += new Vector3(shape.position.x, shape.position.z, -shape.position.y);

			// If the shape is a cone, adjust the y position for visualization purposes.
			if (shape.shapeType == ParticleSystemShapeType.Cone)
			{
				position.y += 0.5f;
			}

			Gizmos.DrawWireCube(position, new Vector3(1f, 1f, 1f));
		}
#endif
	}
}