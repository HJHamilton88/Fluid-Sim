using UnityEngine;
using Unity.Mathematics;
using Seb.Fluid.Simulation;
using static Seb.Helpers.ComputeHelper;

namespace Seb.Sand.Simulation
{
    public class SandSim : FluidSim
    {
        [Header("Sand Settings")]
        public float particleRadius = 0.1f;
        public float stackingDistance = 0.2f;
        public float friction = 0.8f;
        public float restitution = 0.2f;

        [Header("Object Interaction")]
        public Transform[] dynamicObjects;
        public float objectDisplacementForce = 20f;
        public float objectFriction = 0.8f;

        [Header("References")]
        public ComputeShader sandCompute;

        private int grainPhysicsKernel = -1;
        private ComputeBuffer objectDataBuffer;
        private Vector3[] lastObjectPositions;
        private ComputeBuffer cellOffsetBuffer;
        private ComputeBuffer cellCountBuffer;
        private bool sandBuffersInitialized = false;

        // Calculate grid size based on bounds
        private Vector3Int GridSize => new Vector3Int(
            Mathf.CeilToInt(transform.localScale.x / (particleRadius * 4)),
            Mathf.CeilToInt(transform.localScale.y / (particleRadius * 4)),
            Mathf.CeilToInt(transform.localScale.z / (particleRadius * 4))
        );

        protected override void Start()
        {
            if (sandCompute != null)
            {
                grainPhysicsKernel = sandCompute.FindKernel("GrainPhysics");
            }

            // Minimize fluid-like behavior
            normalTimeScale = 1.0f;
            maxTimestepFPS = 120;
            iterationsPerFrame = 1;

            // Nearly disable fluid simulation aspects
            gravity = -9.81f;
            smoothingRadius = particleRadius * 4f;
            targetDensity = 1f;       // Minimal density influence
            pressureMultiplier = 0f;  // Disable pressure forces
            nearPressureMultiplier = 0f;
            viscosityStrength = 0f;   // Disable viscosity
            collisionDamping = 0.8f;
            foamActive = false;

            base.Start();
        }

        protected override void Initialize()
        {
            base.Initialize();
            InitializeSandBuffers();
        }

        private void InitializeSandBuffers()
        {
            if (sandCompute == null || grainPhysicsKernel < 0) return;

            // Initialize spatial grid buffers
            Vector3Int gridSize = GridSize;
            int totalCells = gridSize.x * gridSize.y * gridSize.z;
            cellOffsetBuffer = CreateStructuredBuffer<uint>(totalCells);
            cellCountBuffer = CreateStructuredBuffer<uint>(totalCells);

            // Initialize object data if we have dynamic objects
            if (dynamicObjects != null && dynamicObjects.Length > 0)
            {
                lastObjectPositions = new Vector3[dynamicObjects.Length];
                objectDataBuffer = CreateStructuredBuffer<float>(dynamicObjects.Length * 19);

                for (int i = 0; i < dynamicObjects.Length; i++)
                {
                    if (dynamicObjects[i] != null)
                    {
                        lastObjectPositions[i] = dynamicObjects[i].position;
                    }
                }
            }

            // Add our new buffers to the buffer name lookup
            if (bufferNameLookup != null)
            {
                if (cellOffsetBuffer != null) bufferNameLookup.Add(cellOffsetBuffer, "CellOffsets");
                if (cellCountBuffer != null) bufferNameLookup.Add(cellCountBuffer, "CellCounts");
                if (objectDataBuffer != null) bufferNameLookup.Add(objectDataBuffer, "ObjectData");
            }

            // Set up grain physics kernel buffers
            if (positionBuffer != null && velocityBuffer != null && predictedPositionsBuffer != null)
            {
                sandCompute.SetBuffer(grainPhysicsKernel, "Positions", positionBuffer);
                sandCompute.SetBuffer(grainPhysicsKernel, "Velocities", velocityBuffer);
                sandCompute.SetBuffer(grainPhysicsKernel, "PredictedPositions", predictedPositionsBuffer);
                sandCompute.SetBuffer(grainPhysicsKernel, "CellOffsets", cellOffsetBuffer);
                sandCompute.SetBuffer(grainPhysicsKernel, "CellCounts", cellCountBuffer);

                if (objectDataBuffer != null)
                {
                    sandCompute.SetBuffer(grainPhysicsKernel, "ObjectData", objectDataBuffer);
                }
            }

            sandBuffersInitialized = true;
        }

        protected override void RunSimulationFrame(float frameDeltaTime)
        {
            // Run fluid simulation with minimal effect
            base.RunSimulationFrame(frameDeltaTime);

            // Run sand simulation
            if (sandBuffersInitialized && sandCompute != null && grainPhysicsKernel >= 0)
            {
                // Update spatial grid
                UpdateSpatialGrid();

                // Update object data
                UpdateObjectData();

                // Set required parameters
                sandCompute.SetInt("numParticles", positionBuffer.count);
                sandCompute.SetFloat("deltaTime", frameDeltaTime);
                sandCompute.SetFloat("particleRadius", particleRadius);
                sandCompute.SetFloat("stackingDistance", stackingDistance);
                sandCompute.SetFloat("friction", friction);
                sandCompute.SetFloat("restitution", restitution);
                sandCompute.SetFloat("gravity", gravity);
                sandCompute.SetFloat("objectDisplacementForce", objectDisplacementForce);
                sandCompute.SetFloat("objectFriction", objectFriction);
                sandCompute.SetInt("numObjects", dynamicObjects?.Length ?? 0);

                // Set grid parameters using parent class bounds
                Vector3Int gridSize = GridSize;
                sandCompute.SetInts("gridSize", gridSize.x, gridSize.y, gridSize.z);

                // Dispatch the compute shader
                Dispatch(sandCompute, positionBuffer.count, kernelIndex: grainPhysicsKernel);
            }
        }

        void UpdateSpatialGrid()
        {
            // Clear the grid each frame
            if (cellCountBuffer != null)
            {
                Vector3Int gridSize = GridSize;
                int totalCells = gridSize.x * gridSize.y * gridSize.z;
                uint[] zeroes = new uint[totalCells];
                cellCountBuffer.SetData(zeroes);
                cellOffsetBuffer.SetData(zeroes);
            }
        }

        void UpdateObjectData()
        {
            if (dynamicObjects == null || objectDataBuffer == null) return;

            var data = new float[dynamicObjects.Length * 19];

            for (int i = 0; i < dynamicObjects.Length; i++)
            {
                if (dynamicObjects[i] == null) continue;

                Transform obj = dynamicObjects[i];
                Vector3 currentPos = obj.position;
                Vector3 velocity = (currentPos - lastObjectPositions[i]) / Time.deltaTime;
                lastObjectPositions[i] = currentPos;

                int baseIndex = i * 19;
                // Position
                data[baseIndex + 12] = currentPos.x;
                data[baseIndex + 13] = currentPos.y;
                data[baseIndex + 14] = currentPos.z;
                // Scale
                data[baseIndex + 0] = obj.localScale.x;
                data[baseIndex + 1] = obj.localScale.y;
                data[baseIndex + 2] = obj.localScale.z;
                // Velocity
                data[baseIndex + 16] = velocity.x;
                data[baseIndex + 17] = velocity.y;
                data[baseIndex + 18] = velocity.z;
            }

            objectDataBuffer.SetData(data);
        }

        protected override void OnDestroy()
        {
            if (objectDataBuffer != null)
            {
                Release(objectDataBuffer);
            }
            if (cellOffsetBuffer != null)
            {
                Release(cellOffsetBuffer);
            }
            if (cellCountBuffer != null)
            {
                Release(cellCountBuffer);
            }
            base.OnDestroy();
        }
    }
}