using UnityEngine;
using Unity.Mathematics;
using System.Runtime.InteropServices;

namespace Seb.Fluid.Simulation
{
    public class UnifiedSim : MonoBehaviour
    {
        [Header("Simulation Settings")]
        public float gravity = -9.81f;
        public int iterationsPerFrame = 1;
        public Vector3 boundsSize = new Vector3(10, 10, 10);
        public bool debugMode = true;

        [Header("Fluid Settings")]
        public float smoothingRadius = 0.2f;
        public float targetDensity = 630;
        public float pressureMultiplier = 288;

        [Header("Sand Settings")]
        public float particleRadius = 0.1f;
        public float stackingDistance = 0.2f;

        [Header("Performance Settings")]
        [Range(16, 256)]
        public int threadGroupSize = 64;
        public int maxParticlesPerFrame = 10000;

        [Header("References")]
        public ComputeShader computeShader;
        public Spawner3D spawner;

        // Kernels
        private int externalForcesKernel;
        private int updateSpatialHashKernel;
        private int calculateForcesKernel;
        private int updatePositionsKernel;

        // Compute buffers
        private ComputeBuffer particleBuffer;
        private ComputeBuffer spatialHashBuffer;
        private ComputeBuffer spatialOffsetBuffer;
        private ComputeBuffer forceBuffer;

        // Spatial hash parameters
        private int spatialHashSize;
        private const int maxParticlesPerCell = 64;

        // Data tracking
        private int numParticles;
        private bool isInitialized;

        protected virtual void Start()
        {
            InitializeSimulation();
        }

        private void InitializeSimulation()
        {
            try
            {
                InitializeKernels();
                InitializeParticles();
                InitializeSpatialHash();
                isInitialized = true;

                if (debugMode)
                {
                    Debug.Log($"Simulation initialized with {numParticles} particles");
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Failed to initialize simulation: {e.Message}");
                CleanupBuffers();
                isInitialized = false;
            }
        }

        private void InitializeKernels()
        {
            if (computeShader == null)
            {
                throw new System.NullReferenceException("Compute shader not assigned to UnifiedSim!");
            }

            externalForcesKernel = computeShader.FindKernel("ExternalForces");
            updateSpatialHashKernel = computeShader.FindKernel("UpdateSpatialHash");
            calculateForcesKernel = computeShader.FindKernel("CalculateForces");
            updatePositionsKernel = computeShader.FindKernel("UpdatePositions");

            if (debugMode)
            {
                Debug.Log("Kernels initialized successfully");
            }
        }

        private void InitializeParticles()
        {
            if (spawner == null)
            {
                throw new System.NullReferenceException("Spawner not assigned to UnifiedSim!");
            }

            Spawner3D.SpawnData spawnData = spawner.GetSpawnData();
            numParticles = Mathf.Min(spawnData.points.Length, maxParticlesPerFrame);

            if (numParticles == 0)
            {
                throw new System.InvalidOperationException("No particles spawned!");
            }

            if (debugMode)
            {
                Debug.Log($"Initializing {numParticles} particles");
            }

            // Create and initialize particle buffer
            UnifiedParticle[] particles = new UnifiedParticle[numParticles];
            for (int i = 0; i < numParticles; i++)
            {
                particles[i] = new UnifiedParticle
                {
                    position = spawnData.points[i],
                    velocity = spawnData.velocities[i],
                    density = float2.zero,
                    type = spawnData.types[i],
                    properties = new float4(
                        spawnData.properties[i].friction,
                        spawnData.properties[i].restitution,
                        spawnData.properties[i].viscosity,
                        spawnData.properties[i].pressure
                    )
                };
            }

            particleBuffer = new ComputeBuffer(numParticles, Marshal.SizeOf<UnifiedParticle>());
            particleBuffer.SetData(particles);
            forceBuffer = new ComputeBuffer(numParticles, Marshal.SizeOf<float3>());

            if (debugMode)
            {
                Debug.Log("Particle buffers created successfully");
            }
        }

        private void InitializeSpatialHash()
        {
            float cellSize = smoothingRadius * 2;
            int3 gridSize = new int3(
                Mathf.CeilToInt(boundsSize.x / cellSize),
                Mathf.CeilToInt(boundsSize.y / cellSize),
                Mathf.CeilToInt(boundsSize.z / cellSize)
            );
            spatialHashSize = gridSize.x * gridSize.y * gridSize.z;

            spatialHashBuffer = new ComputeBuffer(numParticles, sizeof(uint));
            spatialOffsetBuffer = new ComputeBuffer(spatialHashSize + 1, sizeof(uint));

            // Set compute shader parameters
            computeShader.SetInt("_SpatialHashSize", spatialHashSize);
            computeShader.SetInt("_MaxParticlesPerCell", maxParticlesPerCell);

            if (debugMode)
            {
                Debug.Log($"Spatial hash initialized with grid size {gridSize}, total cells: {spatialHashSize}");
            }
        }

        protected virtual void Update()
        {
            if (!isInitialized)
            {
                if (debugMode) Debug.LogWarning("Simulation not initialized, skipping update");
                return;
            }

            RunSimulation(Time.deltaTime);
        }

        private void RunSimulation(float deltaTime)
        {
            try
            {
                // Set simulation parameters
                computeShader.SetFloat("_DeltaTime", deltaTime);
                computeShader.SetFloat("_Gravity", gravity);
                computeShader.SetFloat("_SmoothingRadius", smoothingRadius);
                computeShader.SetFloat("_TargetDensity", targetDensity);
                computeShader.SetFloat("_PressureMultiplier", pressureMultiplier);
                computeShader.SetFloat("_ParticleRadius", particleRadius);
                computeShader.SetFloat("_StackingDistance", stackingDistance);
                computeShader.SetVector("_BoundsSize", boundsSize);
                computeShader.SetVector("_BoundsCenter", transform.position);
                computeShader.SetInt("_NumParticles", numParticles);

                for (int i = 0; i < iterationsPerFrame; i++)
                {
                    SetBuffersForKernel(externalForcesKernel);
                    SetBuffersForKernel(updateSpatialHashKernel);
                    SetBuffersForKernel(calculateForcesKernel);
                    SetBuffersForKernel(updatePositionsKernel);

                    int numGroups = Mathf.CeilToInt(numParticles / (float)threadGroupSize);
                    computeShader.Dispatch(externalForcesKernel, numGroups, 1, 1);
                    computeShader.Dispatch(updateSpatialHashKernel, numGroups, 1, 1);
                    computeShader.Dispatch(calculateForcesKernel, numGroups, 1, 1);
                    computeShader.Dispatch(updatePositionsKernel, numGroups, 1, 1);
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Error during simulation: {e.Message}");
                CleanupBuffers();
                isInitialized = false;
            }
        }

        private void SetBuffersForKernel(int kernel)
        {
            computeShader.SetBuffer(kernel, "Particles", particleBuffer);
            computeShader.SetBuffer(kernel, "SpatialLookup", spatialHashBuffer);
            computeShader.SetBuffer(kernel, "SpatialOffsets", spatialOffsetBuffer);
            computeShader.SetBuffer(kernel, "Forces", forceBuffer);
        }

        private void CleanupBuffers()
        {
            if (particleBuffer != null) { particleBuffer.Release(); particleBuffer = null; }
            if (spatialHashBuffer != null) { spatialHashBuffer.Release(); spatialHashBuffer = null; }
            if (spatialOffsetBuffer != null) { spatialOffsetBuffer.Release(); spatialOffsetBuffer = null; }
            if (forceBuffer != null) { forceBuffer.Release(); forceBuffer = null; }
        }

        private void OnDestroy()
        {
            CleanupBuffers();
        }

        public ComputeBuffer GetParticleBuffer()
        {
            return particleBuffer;
        }

        public int GetParticleCount()
        {
            return numParticles;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct UnifiedParticle
        {
            public float3 position;
            public float3 velocity;
            public float2 density;    // (density, nearDensity) for fluids
            public uint type;         // 0=fluid, 1=sand
            public float4 properties; // (friction, restitution, viscosity, pressure)
        }
    }
}