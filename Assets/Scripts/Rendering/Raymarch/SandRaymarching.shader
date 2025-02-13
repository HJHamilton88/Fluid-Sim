Shader "Fluid/SandRaymarching"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _SandColor ("Sand Color", Color) = (0.76, 0.7, 0.5, 1)
        _SandSpecular ("Sand Specular", Range(0,1)) = 0.2
        _SandRoughness ("Sand Roughness", Range(0,1)) = 0.8
        _GrainScale ("Grain Scale", Range(1,100)) = 50
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
                float3 viewVector : TEXCOORD1;
            };

            sampler2D _MainTex;
            float4 _SandColor;
            float _SandSpecular;
            float _SandRoughness;
            float _GrainScale;

            Texture3D<float4> DensityMap;
            SamplerState linearClampSampler;

            float3 extinctionCoeff;
            float3 boundsSize;
            float volumeValueOffset;
            float densityMultiplier;
            float viewMarchStepSize;
            float lightStepSize;
            float3 dirToSun;
            float4x4 cubeLocalToWorld;
            float4x4 cubeWorldToLocal;
            static const float TinyNudge = 0.01;

            struct HitInfo {
                bool didHit;
                bool isInside;
                float dst;
                float3 hitPoint;
                float3 normal;
            };

            // Returns (dstToBox, dstInsideBox). If ray misses box, dstInsideBox will be zero
            float2 RayBoxDst(float3 boundsMin, float3 boundsMax, float3 rayOrigin, float3 rayDir) {
                float3 t0 = (boundsMin - rayOrigin) / rayDir;
                float3 t1 = (boundsMax - rayOrigin) / rayDir;
                float3 tmin = min(t0, t1);
                float3 tmax = max(t0, t1);

                float dstA = max(max(tmin.x, tmin.y), tmin.z);
                float dstB = min(tmax.x, min(tmax.y, tmax.z));

                float dstToBox = max(0, dstA);
                float dstInsideBox = max(0, dstB - dstToBox);
                return float2(dstToBox, dstInsideBox);
            }

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                float3 viewVector = mul(unity_CameraInvProjection, float4(v.uv * 2.0 - 1.0, 0.0, -1.0)).xyz;
                o.viewVector = mul(unity_CameraToWorld, float4(viewVector, 0.0)).xyz;
                return o;
            }

            float SampleDensity(float3 pos)
            {
                float3 uvw = (pos + boundsSize * 0.5) / boundsSize;

                const float epsilon = 0.0001;
                bool isEdge = any(uvw >= 1.0 - epsilon || uvw <= epsilon);
                if (isEdge) return -volumeValueOffset;

                // Sample base density
                float density = DensityMap.SampleLevel(linearClampSampler, uvw, 0).r - volumeValueOffset;
                
                // Add noise variation for sand-like appearance
                float scale = _GrainScale * 0.1;
                float noise = sin(pos.x * scale) * sin(pos.y * scale) * sin(pos.z * scale);
                noise = noise * 0.5 + 0.5;  // Normalize to 0-1 range
                
                return density * (0.8 + noise * 0.4);
            }

            float3 CalculateNormalSand(float3 pos)
            {
                const float s = 0.1;
                float3 offsetX = float3(s, 0.0, 0.0);
                float3 offsetY = float3(0.0, s, 0.0);
                float3 offsetZ = float3(0.0, 0.0, s);

                float dx = SampleDensity(pos - offsetX) - SampleDensity(pos + offsetX);
                float dy = SampleDensity(pos - offsetY) - SampleDensity(pos + offsetY);
                float dz = SampleDensity(pos - offsetZ) - SampleDensity(pos + offsetZ);

                float3 normal = normalize(float3(dx, dy, dz));
                
                // Add grain detail to normal using triplanar noise
                float scale = _GrainScale * 0.1;
                float3 grainNormal = float3(
                    sin(pos.y * scale) * sin(pos.z * scale),
                    sin(pos.x * scale) * sin(pos.z * scale),
                    sin(pos.x * scale) * sin(pos.y * scale)
                );
                
                return normalize(lerp(normal, normalize(grainNormal), _SandRoughness * 0.3));
            }

            float3 CalculateSandLighting(float3 pos, float3 normal)
            {
                // Ambient light
                float3 ambient = _SandColor.rgb * 0.2;

                // Diffuse lighting
                float3 lightDir = normalize(dirToSun);
                float diff = max(dot(normal, lightDir), 0.0);
                float3 diffuse = _SandColor.rgb * diff;

                // Specular lighting
                float3 viewDir = normalize(_WorldSpaceCameraPos - pos);
                float3 reflectDir = reflect(-lightDir, normal);
                float spec = pow(max(dot(viewDir, reflectDir), 0.0), 32.0);
                float3 specular = float3(1.0, 1.0, 1.0) * spec * _SandSpecular;

                // Grain variation
                float scale = _GrainScale * 0.05;
                float grainVar = sin(pos.x * scale) * sin(pos.y * scale) * sin(pos.z * scale);
                grainVar = grainVar * 0.1 + 0.9;  // Normalize to 0.8-1.0 range

                return (ambient + diffuse + specular) * grainVar;
            }

            float3 RayMarchSand(float2 uv)
            {
                float3 rayDir = normalize(mul(unity_CameraToWorld, float4(mul(unity_CameraInvProjection, float4(uv * 2.0 - 1.0, 0.0, -1.0)).xyz, 0.0)).xyz);
                float3 rayPos = _WorldSpaceCameraPos;
                
                // Calculate intersection with bounding box
                float2 boundsDstInfo = RayBoxDst(-boundsSize * 0.5, boundsSize * 0.5, rayPos, rayDir);
                float dstToBox = boundsDstInfo.x;
                float dstInsideBox = boundsDstInfo.y;
                
                if (dstInsideBox <= 0) return float3(0.7, 0.8, 1.0); // Sky color if no intersection

                float dstTravelled = dstToBox;
                float3 entryPoint = rayPos + rayDir * (dstToBox + TinyNudge);
                float3 color = float3(0.0, 0.0, 0.0);
                bool hitSurface = false;

                // Ray march through volume
                for (int i = 0; i < 128 && dstTravelled < dstToBox + dstInsideBox; i++)
                {
                    float3 pos = entryPoint + rayDir * (dstTravelled - dstToBox);
                    float density = SampleDensity(pos);

                    if (density > 0.1) // Surface threshold
                    {
                        float3 normal = CalculateNormalSand(pos);
                        color = CalculateSandLighting(pos, normal);
                        hitSurface = true;
                        break;
                    }

                    dstTravelled += viewMarchStepSize;
                }

                // If we didn't hit the surface, return sky color
                if (!hitSurface)
                {
                    float skyGradient = pow(max(0.0, lerp(0.1, 1.0, rayDir.y)), 0.4);
                    color = lerp(float3(0.5, 0.6, 0.7), float3(0.7, 0.8, 1.0), skyGradient);
                }

                return color;
            }

            float4 frag(v2f i) : SV_Target
            {
                return float4(RayMarchSand(i.uv), 1.0);
            }
            
            ENDCG
        }
    }
}