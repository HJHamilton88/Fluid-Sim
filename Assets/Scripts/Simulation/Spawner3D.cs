using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace Seb.Fluid.Simulation
{
    public enum ParticleType
    {
        Fluid = 0,
        Sand = 1
    }

    public struct ParticleProperties
    {
        public float friction;
        public float restitution;
        public float viscosity;
        public float pressure;
    }

    public class Spawner3D : MonoBehaviour
    {
        public int particleSpawnDensity = 600;
        public float3 initialVel;
        public float jitterStrength;
        public bool showSpawnBounds = true;  // Default to true
        public SpawnRegion[] spawnRegions;

        [Header("Debug Info")]
        public int debug_num_particles;
        public float debug_spawn_volume;

        public SpawnData GetSpawnData()
        {
            List<float3> allPoints = new();
            List<float3> allVelocities = new();
            List<uint> allTypes = new();
            List<ParticleProperties> allProperties = new();

            foreach (SpawnRegion region in spawnRegions)
            {
                int particlesPerAxis = region.CalculateParticleCountPerAxis(particleSpawnDensity);
                (float3[] points, float3[] velocities) = SpawnCube(particlesPerAxis, region.centre, Vector3.one * region.size);

                // Create particle type and properties arrays for this region
                uint[] types = new uint[points.Length];
                ParticleProperties[] properties = new ParticleProperties[points.Length];

                for (int i = 0; i < points.Length; i++)
                {
                    types[i] = (uint)region.particleType;
                    properties[i] = new ParticleProperties
                    {
                        friction = region.friction,
                        restitution = region.restitution,
                        viscosity = region.viscosity,
                        pressure = region.pressure
                    };
                }

                allPoints.AddRange(points);
                allVelocities.AddRange(velocities);
                allTypes.AddRange(types);
                allProperties.AddRange(properties);
            }

            return new SpawnData
            {
                points = allPoints.ToArray(),
                velocities = allVelocities.ToArray(),
                types = allTypes.ToArray(),
                properties = allProperties.ToArray()
            };
        }

        (float3[] p, float3[] v) SpawnCube(int numPerAxis, Vector3 centre, Vector3 size)
        {
            int numPoints = numPerAxis * numPerAxis * numPerAxis;
            float3[] points = new float3[numPoints];
            float3[] velocities = new float3[numPoints];

            int i = 0;

            for (int x = 0; x < numPerAxis; x++)
            {
                for (int y = 0; y < numPerAxis; y++)
                {
                    for (int z = 0; z < numPerAxis; z++)
                    {
                        float tx = x / (numPerAxis - 1f);
                        float ty = y / (numPerAxis - 1f);
                        float tz = z / (numPerAxis - 1f);

                        float px = (tx - 0.5f) * size.x + centre.x;
                        float py = (ty - 0.5f) * size.y + centre.y;
                        float pz = (tz - 0.5f) * size.z + centre.z;
                        float3 jitter = UnityEngine.Random.insideUnitSphere * jitterStrength;
                        points[i] = new float3(px, py, pz) + jitter;
                        velocities[i] = initialVel;
                        i++;
                    }
                }
            }

            return (points, velocities);
        }

        void OnValidate()
        {
            debug_spawn_volume = 0;
            debug_num_particles = 0;

            if (spawnRegions != null)
            {
                foreach (SpawnRegion region in spawnRegions)
                {
                    debug_spawn_volume += region.Volume;
                    int numPerAxis = region.CalculateParticleCountPerAxis(particleSpawnDensity);
                    debug_num_particles += numPerAxis * numPerAxis * numPerAxis;
                }
            }
        }

        void OnDrawGizmos()
        {
            if (spawnRegions == null || spawnRegions.Length == 0)
            {
                Debug.LogWarning("No spawn regions defined in " + gameObject.name);
                return;
            }

            if (showSpawnBounds)
            {
                foreach (SpawnRegion region in spawnRegions)
                {
                    // Draw main cube
                    Gizmos.color = region.debugDisplayCol;
                    Gizmos.DrawWireCube(region.centre, Vector3.one * region.size);

                    // Draw a small colored sphere at each corner to make it more visible
                    float halfSize = region.size * 0.5f;
                    Vector3[] corners = new Vector3[]
                    {
                        new Vector3(-1, -1, -1),
                        new Vector3(-1, -1,  1),
                        new Vector3(-1,  1, -1),
                        new Vector3(-1,  1,  1),
                        new Vector3( 1, -1, -1),
                        new Vector3( 1, -1,  1),
                        new Vector3( 1,  1, -1),
                        new Vector3( 1,  1,  1)
                    };

                    foreach (Vector3 corner in corners)
                    {
                        Vector3 cornerPos = region.centre + Vector3.Scale(corner, Vector3.one * halfSize);
                        Gizmos.DrawSphere(cornerPos, region.size * 0.02f);
                    }

                    // Draw particle type label
                    UnityEditor.Handles.Label(region.centre + Vector3.up * (region.size * 0.5f),
                        region.particleType.ToString());
                }
            }
        }

        void OnDrawGizmosSelected()
        {
            // Draw additional visualization when the object is selected
            if (showSpawnBounds && spawnRegions != null)
            {
                foreach (SpawnRegion region in spawnRegions)
                {
                    Gizmos.color = new Color(region.debugDisplayCol.r, region.debugDisplayCol.g, region.debugDisplayCol.b, 0.2f);
                    Gizmos.DrawCube(region.centre, Vector3.one * region.size);
                }
            }
        }

        [System.Serializable]
        public struct SpawnRegion
        {
            public Vector3 centre;
            public float size;
            public Color debugDisplayCol;
            public ParticleType particleType;

            // Material properties
            public float friction;        // For sand
            public float restitution;     // For sand
            public float viscosity;       // For fluid
            public float pressure;        // For fluid

            public float Volume => size * size * size;

            public int CalculateParticleCountPerAxis(int particleDensity)
            {
                int targetParticleCount = (int)(Volume * particleDensity);
                return (int)Math.Cbrt(targetParticleCount);
            }
        }

        public struct SpawnData
        {
            public float3[] points;
            public float3[] velocities;
            public uint[] types;
            public ParticleProperties[] properties;
        }
    }
}