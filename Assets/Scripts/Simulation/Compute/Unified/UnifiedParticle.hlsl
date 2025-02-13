#ifndef UNIFIED_PARTICLE_INCLUDED
#define UNIFIED_PARTICLE_INCLUDED

struct UnifiedParticle
{
    float3 position;
    float3 velocity;
    float2 density;    // (density, nearDensity) for fluids
    uint type;         // 0=fluid, 1=sand
    float4 properties; // (friction, restitution, viscosity, pressure)
};

#endif // UNIFIED_PARTICLE_INCLUDED