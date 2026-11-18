using System.Runtime.CompilerServices;
using UnityEngine;
using System;

namespace FishMMO.Shared
{
	/// <summary>
	/// Extension methods for Vector3, providing randomization and geometric utilities for spheres and toroids.
	/// </summary>
	public static class Vector3Extensions
	{
		/// <summary>
		/// A rotation looking along a direction drawn uniformly from inside a cone opening along
		/// <paramref name="forward"/>.
		/// </summary>
		/// <remarks>
		/// <para>
		/// This used to read <c>Quaternion.Euler(startPosition - ((distance * forward) + (RandomOnUnitSphere(random) * coneRadius)))</c>,
		/// which fed a WORLD POSITION into <see cref="Quaternion.Euler"/> as if it were Euler angles in
		/// degrees. The result was a function of the object's absolute map coordinates rather than of
		/// its heading, the arc was applied as a sphere radius, and the same ability produced unrelated
		/// rotations at different places in the world. The start position and distance are gone because
		/// a direction does not depend on either.
		/// </para>
		/// <para>
		/// Sampling is uniform over the spherical cap, not over the polar angle: drawing
		/// <c>cos(theta)</c> flat keeps the density even across the cap, where drawing <c>theta</c> flat
		/// would bunch results towards the axis. Every draw comes from
		/// <paramref name="random"/> so two peers running the same ability with the same generator
		/// state produce the same direction.
		/// </para>
		/// </remarks>
		/// <param name="forward">Axis the cone opens along. A degenerate vector yields the identity rotation.</param>
		/// <param name="coneAngleDegrees">TOTAL spread of the cone. Zero or less means no deviation; 360 or more is the whole sphere.</param>
		/// <param name="random">Deterministic generator. Required — a shared or per-process stream would desynchronise peers.</param>
		/// <returns>A rotation whose forward axis is the sampled direction.</returns>
		public static Quaternion GetRandomConicalDirection(this Vector3 forward, float coneAngleDegrees, DeterministicRNG random)
		{
			if (forward.sqrMagnitude < 1e-8f)
			{
				return Quaternion.identity;
			}

			Vector3 axis = forward.normalized;
			if (coneAngleDegrees <= 0.0f || random == null)
			{
				return Quaternion.LookRotation(axis, StableUpFor(axis));
			}

			float halfAngleDegrees = coneAngleDegrees * 0.5f;
			if (halfAngleDegrees > 180.0f)
			{
				halfAngleDegrees = 180.0f;
			}

			// Uniform over the cap: cos(theta) flat between the rim and the axis.
			double cosLimit = Math.Cos(halfAngleDegrees * Mathf.Deg2Rad);
			double cosTheta = cosLimit + (1.0 - cosLimit) * random.NextDouble();
			double sinTheta = Math.Sqrt(Math.Max(0.0, 1.0 - (cosTheta * cosTheta)));
			double phi = random.NextDouble() * 2.0 * Math.PI;

			Vector3 local = new Vector3(
				(float)(sinTheta * Math.Cos(phi)),
				(float)(sinTheta * Math.Sin(phi)),
				(float)cosTheta);

			// Local +Z is the cone's axis, so rotating the sample into the axis frame aims it.
			Vector3 direction = Quaternion.LookRotation(axis, StableUpFor(axis)) * local;
			if (direction.sqrMagnitude < 1e-8f)
			{
				return Quaternion.LookRotation(axis, StableUpFor(axis));
			}
			return Quaternion.LookRotation(direction.normalized, StableUpFor(direction));
		}

		/// <summary>
		/// An up vector that is never parallel to <paramref name="direction"/>, so
		/// <see cref="Quaternion.LookRotation(Vector3, Vector3)"/> cannot degenerate on a
		/// straight-up or straight-down heading.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static Vector3 StableUpFor(Vector3 direction)
		{
			return Mathf.Abs(direction.normalized.y) > 0.99f ? Vector3.forward : Vector3.up;
		}

		/// <summary>
		/// Returns a random position within a circle of given radius around a center point (on the XZ plane).
		/// </summary>
		/// <param name="center">The center position.</param>
		/// <param name="radius">The radius of the circle.</param>
		/// <returns>A random Vector3 position within the radius.</returns>
		public static Vector3 RandomPositionWithinRadius(Vector3 center, float radius)
		{
			// Generate a random angle between 0 and 2π
			float angle = DeterministicRNG.Shared.Range(0f, 2 * Mathf.PI);

			// Generate a random distance between 0 and radius
			float distance = DeterministicRNG.Shared.Range(0f, radius);

			// Calculate X and Z offsets
			float xOffset = distance * Mathf.Cos(angle);
			float zOffset = distance * Mathf.Sin(angle);

			// Return the random position offset from the center
			return new Vector3(center.x + xOffset, center.y, center.z + zOffset);
		}

		/// <summary>
		/// Gets the nearest position on the surface of a sphere centered at pointB, from pointA.
		/// </summary>
		/// <param name="pointA">The point to project from.</param>
		/// <param name="pointB">The center of the sphere.</param>
		/// <param name="radius">The radius of the sphere.</param>
		/// <returns>The nearest position on the sphere surface.</returns>
		public static Vector3 GetNearestPositionOnSphere(Vector3 pointA, Vector3 pointB, float radius)
		{
			// Calculate the direction vector from B to A
			Vector3 direction = pointA - pointB;

			// Normalize the direction vector to get a unit vector
			Vector3 unitDirection = direction.normalized;

			// Multiply the unit direction by the sphere's radius to get the nearest point on the sphere
			Vector3 nearestPoint = pointB + unitDirection * radius;

			return nearestPoint;
		}

		/// <summary>
		/// Returns a random point on the surface of a unit sphere using a provided or new <see cref="DeterministicRNG"/>.
		/// </summary>
		/// <param name="random">Optional random number generator.</param>
		/// <returns>A random Vector3 on the unit sphere.</returns>
		public static Vector3 RandomOnUnitSphere(DeterministicRNG random = null)
		{
			if (random == null)
			{
				random = new DeterministicRNG();
			}

			// Generate random spherical coordinates
			double theta = random.NextDouble() * 2 * Math.PI;  // azimuthal angle (0 to 2pi)
			double phi = Math.Acos(2 * random.NextDouble() - 1);  // polar angle (0 to pi)

			// Convert spherical coordinates to Cartesian coordinates
			double x = Math.Sin(phi) * Math.Cos(theta);
			double y = Math.Sin(phi) * Math.Sin(theta);
			double z = Math.Cos(phi);

			/* A plain cast, not Unsafe.As. This read `Unsafe.As<double, float>(ref x)` and called it a
			 * "fast double-to-float conversion", but Unsafe.As REINTERPRETS the first four bytes of the
			 * double rather than converting its value: 0.5 came back as 0f (the low half of its mantissa
			 * is zero) and 0.9999 as 476472.94f. Every vector this returned was garbage, not a point on
			 * the unit sphere. */
			return new Vector3((float)x, (float)y, (float)z);
		}

		/// <summary>
		/// Returns a random position inside a bounding box centered at the origin.
		/// </summary>
		/// <param name="boundingBox">The size of the bounding box.</param>
		/// <returns>A random Vector3 inside the bounding box.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static Vector3 RandomInBoundingBox(Vector3 boundingBox)
		{
			return new Vector3(DeterministicRNG.Shared.Range(-boundingBox.x, boundingBox.x),
							   DeterministicRNG.Shared.Range(-boundingBox.y, boundingBox.y),
							   DeterministicRNG.Shared.Range(-boundingBox.z, boundingBox.z));
		}

		/// <summary>
		/// Returns a random point inside a 3D toroid (donut shape) with major radius R and tube radius.
		/// </summary>
		/// <param name="R">Major radius (distance from center to tube center).</param>
		/// <param name="radius">Tube radius.</param>
		/// <returns>A random Vector3 inside the toroid.</returns>
		public static Vector3 GetRandomPointInToroid(float R, float radius)
		{
			// Generate random angles
			float theta = DeterministicRNG.Shared.Range(0f, 2f * Mathf.PI); // Angle around the central axis
			float phi = DeterministicRNG.Shared.Range(0f, 2f * Mathf.PI);   // Angle around the tube

			// Convert to Cartesian coordinates
			float x = (R + radius * Mathf.Cos(phi)) * Mathf.Cos(theta);
			float y = (R + radius * Mathf.Cos(phi)) * Mathf.Sin(theta);
			float z = radius * Mathf.Sin(phi);

			return new Vector3(x, y, z);
		}

		/// <summary>
		/// Returns a random point inside a flat toroid (2D ring) with major radius R and tube radius.
		/// </summary>
		/// <param name="R">Major radius (distance from center to tube center).</param>
		/// <param name="radius">Tube radius.</param>
		/// <returns>A random Vector3 inside the flat toroid (z = 0).</returns>
		public static Vector3 GetRandomPointInFlatToroid(float R, float radius)
		{
			// Generate a random angle (theta) around the central axis (the hole of the ring)
			float theta = DeterministicRNG.Shared.Range(0f, 2f * Mathf.PI);  // Angle around the center

			// Generate a random radial distance between R and (R + radius) from the center of the ring
			float r = DeterministicRNG.Shared.Range(R, R + radius);  // Radial distance within the tube

			// Convert to Cartesian coordinates
			float x = r * Mathf.Cos(theta);
			float y = r * Mathf.Sin(theta);

			// Return as a Vector3, setting z = 0 (since it's a flat toroid)
			return new Vector3(x, y, 0f);
		}

		/// <summary>
		/// Gets the nearest point on a flat toroid (2D ring) from pointA, clamped to the toroid's bounds.
		/// </summary>
		/// <param name="pointA">The point to project from.</param>
		/// <param name="pointB">The center of the toroid.</param>
		/// <param name="R">Major radius.</param>
		/// <param name="radius">Tube radius.</param>
		/// <returns>The nearest point on the flat toroid.</returns>
		public static Vector3 GetNearestPointOnFlatToroid(Vector3 pointA, Vector3 pointB, float R, float radius)
		{
			// Calculate the direction from A to B
			Vector3 direction = pointA - pointB;

			// Project the direction onto the xy-plane (ignore the z-component)
			float px = direction.x;
			float py = direction.y;

			// Calculate the angle (theta) of the direction relative to the center of the toroid (pointB)
			float theta = Mathf.Atan2(py, px);

			// Calculate the radial distance from point B (the center of the toroid)
			float distanceFromCenter = Mathf.Sqrt(px * px + py * py);

			// Clamp the radial distance to lie within the bounds of the toroidal surface (R to R + radius)
			float clampedRadius = Mathf.Clamp(distanceFromCenter, R, R + radius);

			// Calculate the new position on the toroid at the clamped radius and angle theta
			float x = clampedRadius * Mathf.Cos(theta);
			float y = clampedRadius * Mathf.Sin(theta);

			// Return the nearest point in world space (since pointB is the center of the toroid)
			return new Vector3(x, y, 0f) + pointB;
		}
	}
}