using UnityEditor;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Custom editor for ParticleAdjuster, adds a button to scale the particle system to fit within a 1x1x1 cube.
	/// </summary>
	[CustomEditor(typeof(ParticleAdjuster))]
	public class ParticleSystemEditorExtension : Editor
	{
		/// <summary>
		/// Draws the custom inspector UI, including a button to scale the particle system.
		/// </summary>
		public override void OnInspectorGUI()
		{
			// Draw default inspector elements
			DrawDefaultInspector();

			// Get reference to the target ParticleAdjuster
			ParticleAdjuster particleAdjuster = (ParticleAdjuster)target;

			// Add a button to the Inspector UI to scale the particle system
			if (GUILayout.Button("Scale Particle System to Fit Cube"))
			{
				// Scales the particle system so all particles remain inside a 1x1x1 cube
				ScaleParticleSystem(particleAdjuster);
			}
		}

		/// <summary>
		/// Scales the particle system so all particles remain inside a 1x1x1 cube.
		/// Adjusts start speed, radius, and lifetime based on shape type.
		/// </summary>
		/// <param name="particleAdjuster">The ParticleAdjuster component containing the ParticleSystem.</param>
		private void ScaleParticleSystem(ParticleAdjuster particleAdjuster)
		{
			if (particleAdjuster == null || particleAdjuster.ParticleSystem == null)
			{
				UnityEngine.Debug.LogWarning("Particle system is missing.");
				return;
			}

			var particleSystem = particleAdjuster.ParticleSystem;
			var main = particleSystem.main;
			var shape = particleSystem.shape;

			// Cube half size (distance from the center to any face of the 1x1x1 cube)
			const float CubeSize = 1.0f;
			const float CubeHalfSize = 0.5f;

			// For cone shapes, use full cube size; for others, use half size divided by max lifetime
			float maxStartSpeed = shape.shapeType == ParticleSystemShapeType.Cone ? CubeSize : CubeHalfSize / main.startLifetime.constantMax;

			// Set the start speed to a value that ensures particles stay inside the cube
			main.startSpeed = maxStartSpeed;

			// Set the particle system's radius to fit within the cube for cone shapes
			if (shape.shapeType == ParticleSystemShapeType.Cone)
			{
				shape.radius = CubeHalfSize;
			}

			// Adjust the start lifetime so particles don't travel outside the cube
			main.startLifetime = shape.shapeType == ParticleSystemShapeType.Cone ? CubeSize : CubeHalfSize / maxStartSpeed;  // Ensures particles don't exceed cube boundaries
		}
	}
}