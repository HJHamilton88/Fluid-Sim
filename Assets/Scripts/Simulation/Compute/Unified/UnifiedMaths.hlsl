#ifndef UNIFIED_MATHS_INCLUDED
#define UNIFIED_MATHS_INCLUDED

#include "UnifiedParticle.hlsl"

float Poly6(float sqrDist, float radius)
{
    if (sqrDist >= radius * radius) return 0;
    float scale = 315.0f / (64.0f * 3.14159f * pow(radius, 9));
    float v = radius * radius - sqrDist;
    return scale * v * v * v;
}

float SpikyGrad(float dist, float radius)
{
    if (dist > radius || dist < 0.0001f) return 0;
    float scale = -45.0f / (3.14159f * pow(radius, 6));
    float v = radius - dist;
    return scale * v * v;
}

float ViscosityLap(float dist, float radius)
{
    if (dist >= radius) return 0;
    float scale = 45.0f / (3.14159f * pow(radius, 6));
    return scale * (radius - dist);
}

float2 CalculateDensities(UnifiedParticle particle, UnifiedParticle neighbours[64], uint numNeighbours, float smoothingRadius)
{
    float density = 0;
    float nearDensity = 0;
    float sqrRadius = smoothingRadius * smoothingRadius;

    for (uint i = 0; i < numNeighbours; i++)
    {
        float3 offsetToNeighbour = neighbours[i].position - particle.position;
        float sqrDist = dot(offsetToNeighbour, offsetToNeighbour);
        float dist = sqrt(sqrDist);

        density += Poly6(sqrDist, smoothingRadius);
        nearDensity += Poly6(sqrDist, smoothingRadius * 0.5f);
    }

    return float2(density, nearDensity);
}

float3 CalculatePressureForce(UnifiedParticle particle, UnifiedParticle neighbours[64], uint numNeighbours, float smoothingRadius, float density, float targetDensity, float pressureMultiplier)
{
    float pressure = (density - targetDensity) * pressureMultiplier;
    float nearPressure = particle.density.y * pressureMultiplier * 0.5f;
    float3 pressureForce = 0;

    for (uint i = 0; i < numNeighbours; i++)
    {
        float3 offsetToNeighbour = neighbours[i].position - particle.position;
        float dist = length(offsetToNeighbour);
        float3 dirToNeighbour = offsetToNeighbour / (dist + 0.0001f);

        float neighbourPressure = (neighbours[i].density.x - targetDensity) * pressureMultiplier;
        float sharedPressure = (pressure + neighbourPressure) * 0.5f;

        float nearNeighbourPressure = neighbours[i].density.y * pressureMultiplier * 0.5f;
        float sharedNearPressure = (nearPressure + nearNeighbourPressure) * 0.5f;

        pressureForce += dirToNeighbour * sharedPressure * SpikyGrad(dist, smoothingRadius);
        pressureForce += dirToNeighbour * sharedNearPressure * SpikyGrad(dist, smoothingRadius * 0.5f);
    }

    return -pressureForce / (density + 0.0001f);
}

float3 CalculateViscosityForce(UnifiedParticle particle, UnifiedParticle neighbours[64], uint numNeighbours, float smoothingRadius, float viscosity)
{
    float3 viscosityForce = 0;
    float density = particle.density.x;

    for (uint i = 0; i < numNeighbours; i++)
    {
        float3 offsetToNeighbour = neighbours[i].position - particle.position;
        float dist = length(offsetToNeighbour);
        
        float3 velocityDiff = neighbours[i].velocity - particle.velocity;
        viscosityForce += velocityDiff * ViscosityLap(dist, smoothingRadius);
    }

    return viscosityForce * viscosity / (density + 0.0001f);
}

float3 CalculateContactForce(UnifiedParticle particle, UnifiedParticle neighbours[64], uint numNeighbours, float particleRadius, float stackingDistance, float friction)
{
    float3 contactForce = 0;
    
    for (uint i = 0; i < numNeighbours; i++)
    {
        float3 offsetToNeighbour = neighbours[i].position - particle.position;
        float dist = length(offsetToNeighbour);
        
        if (dist < stackingDistance && dist > 0.0001f)
        {
            float3 dirToNeighbour = offsetToNeighbour / dist;
            float overlap = stackingDistance - dist;
            contactForce += dirToNeighbour * overlap * friction;
        }
    }
    
    return contactForce;
}

float3 CalculateFrictionForce(UnifiedParticle particle, float friction)
{
    return -particle.velocity * friction;
}

#endif // UNIFIED_MATHS_INCLUDED