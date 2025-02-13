# Unified Fluid and Sand Simulation Implementation Guide

## Overview
This document outlines the implementation of a unified particle simulation system that handles both fluid and sand particles within the same simulation space. The goal is to maintain the efficiency of the current system while allowing different particle types to coexist and interact.

## Implementation Steps

### 1. Modify Spawner3D
```csharp
[System.Serializable]
public struct SpawnRegion
{
    public Vector3 centre;
    public float size;
    public Color debugDisplayCol;
    public ParticleType particleType;  // New field

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
    public uint[] types;           // New field
    public ParticleProperties[] properties;  // New field
}

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
```

### 2. Create UnifiedParticle Structure (HLSL)
```hlsl
// In UnifiedParticle.hlsl
struct UnifiedParticle
{
    float3 position;
    float3 velocity;
    float2 density;    // (density, nearDensity) for fluids
    uint type;         // 0=fluid, 1=sand
    float4 properties; // (friction, restitution, viscosity, pressure)
};
```

### 3. Create UnifiedSim Compute Shader
```hlsl
// UnifiedSim.compute
#pragma kernel ExternalForces
#pragma kernel UpdateSpatialHash
// ... (keep other existing kernels)

#include "UnifiedParticle.hlsl"
#include "FluidMaths3D.hlsl"
#include "SpatialHash3D.hlsl"

// Buffers
RWStructuredBuffer<UnifiedParticle> Particles;
// ... (other existing buffers)

[numthreads(256,1,1)]
void CalculateForces(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= numParticles) return;

    UnifiedParticle particle = Particles[id.x];

    if(particle.type == 0) // Fluid
    {
        // Existing fluid force calculations
        CalculateFluidForces(particle);
    }
    else // Sand
    {
        // Existing sand force calculations
        CalculateSandForces(particle);
    }

    Particles[id.x] = particle;
}
```

### 4. Create UnifiedSim C# Class
```csharp
public class UnifiedSim : MonoBehaviour
{
    [Header("Simulation Settings")]
    public float gravity = -9.81f;
    public int iterationsPerFrame = 2;

    [Header("Fluid Settings")]
    public float smoothingRadius = 0.2f;
    public float targetDensity = 630;
    public float pressureMultiplier = 288;

    [Header("Sand Settings")]
    public float particleRadius = 0.1f;
    public float stackingDistance = 0.2f;

    [Header("References")]
    public ComputeShader computeShader;
    public Spawner3D spawner;

    private ComputeBuffer particleBuffer;
    private ComputeBuffer spatialHashBuffer;
    // ... other necessary buffers

    protected virtual void Start()
    {
        InitializeBuffers();
        SetupComputeShader();
    }

    protected virtual void Update()
    {
        RunSimulation(Time.deltaTime);
    }

    private void RunSimulation(float deltaTime)
    {
        // Update spatial hash
        // Calculate forces
        // Update positions
        // Handle rendering
    }
}
```

### 5. Modify RayMarchingTest
```csharp
public class RayMarchingTest : MonoBehaviour
{
    public UnifiedSim sim;
    public Shader fluidShader;
    public Shader sandShader;

    Material fluidMaterial;
    Material sandMaterial;

    void OnRenderImage(RenderTexture src, RenderTexture target)
    {
        // Create temporary render texture for compositing
        RenderTexture temp = RenderTexture.GetTemporary(src.width, src.height);

        // Render fluid particles
        Graphics.Blit(src, temp, fluidMaterial);

        // Render sand particles
        Graphics.Blit(temp, target, sandMaterial);

        RenderTexture.ReleaseTemporary(temp);
    }
}
```

## Implementation Order

1. Start with Spawner3D modifications
   - Add particle type and properties
   - Update spawn data generation
   - Test with simple visualization

2. Create UnifiedParticle structure
   - Implement in HLSL
   - Create corresponding C# structure
   - Update buffer creation

3. Implement UnifiedSim compute shader
   - Start with basic forces
   - Add spatial hashing
   - Implement type-specific calculations
   - Test with simple particles

4. Create UnifiedSim C# class
   - Basic simulation loop
   - Buffer management
   - Parameter handling

5. Update rendering system
   - Modify shaders to handle types
   - Update RayMarchingTest
   - Implement compositing

6. Testing and optimization
   - Test particle interaction
   - Profile performance
   - Optimize compute shaders

## Key Considerations

1. Performance
   - Use single compute pass where possible
   - Minimize branching in compute shaders
   - Optimize buffer access patterns

2. Memory Management
   - Properly initialize and release buffers
   - Handle dynamic particle counts
   - Monitor GPU memory usage

3. Extensibility
   - Design for additional particle types
   - Keep material properties flexible
   - Document interaction rules

4. Debug Support
   - Add visualization options
   - Include performance metrics
   - Support particle type inspection

## Next Steps

1. Begin with Spawner3D modifications
2. Create test scene with both particle types
3. Implement basic unified simulation
4. Add visualization support
5. Test and optimize

Would you like to start with any particular component?