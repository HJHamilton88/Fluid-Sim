using UnityEngine;
using Seb.Fluid.Simulation;

namespace Seb.Fluid.Rendering
{
    [ImageEffectAllowedInSceneView]
    public class RayMarchingTest : MonoBehaviour
    {
        [Header("Settings")]
        public float densityOffset = 150;
        public int numRefractions = 4;
        public Vector3 extinctionCoefficients;
        public float densityMultiplier = 0.001f;
        [Min(0.01f)] public float stepSize = 0.02f;
        public float lightStepSize = 0.4f;
        [Min(1)] public float indexOfRefraction = 1.33f;
        public Vector3 testParams;
        public EnvironmentSettings environmentSettings;

        [Header("References")]
        public UnifiedSim unifiedSim;
        public Transform cubeTransform;
        public Shader fluidShader;
        public Shader sandShader;
        public ComputeShader densityMapCompute;

        [Header("Density Map Settings")]
        public int densityMapResolution = 128;
        public float particleRadius = 0.1f;
        public float densityFalloff = 1.0f;

        [Header("Sand Settings")]
        public Color sandColor = new Color(0.76f, 0.7f, 0.5f, 1f);
        [Range(0, 1)] public float sandSpecular = 0.2f;
        [Range(0, 1)] public float sandRoughness = 0.8f;
        [Range(1, 100)] public float grainScale = 50f;

        Material fluidMaterial;
        Material sandMaterial;
        RenderTexture tempRT;
        RenderTexture fluidDensityMap;
        RenderTexture sandDensityMap;

        // Compute shader kernels
        private int updateDensityMapsKernel;

        void Start()
        {
            if (fluidShader != null) fluidMaterial = new Material(fluidShader);
            if (sandShader != null) sandMaterial = new Material(sandShader);
            Camera.main.depthTextureMode = DepthTextureMode.Depth;

            // Initialize compute shader
            if (densityMapCompute != null)
            {
                updateDensityMapsKernel = densityMapCompute.FindKernel("UpdateDensityMaps");
            }

            // Create density maps
            CreateDensityMaps();
        }

        void CreateDensityMaps()
        {
            // Create density maps with specified resolution
            fluidDensityMap = new RenderTexture(densityMapResolution, densityMapResolution, 0, RenderTextureFormat.RFloat);
            fluidDensityMap.dimension = UnityEngine.Rendering.TextureDimension.Tex3D;
            fluidDensityMap.volumeDepth = densityMapResolution;
            fluidDensityMap.enableRandomWrite = true;
            fluidDensityMap.Create();

            sandDensityMap = new RenderTexture(densityMapResolution, densityMapResolution, 0, RenderTextureFormat.RFloat);
            sandDensityMap.dimension = UnityEngine.Rendering.TextureDimension.Tex3D;
            sandDensityMap.volumeDepth = densityMapResolution;
            sandDensityMap.enableRandomWrite = true;
            sandDensityMap.Create();
        }

        void OnDestroy()
        {
            if (tempRT != null)
                tempRT.Release();
            if (fluidDensityMap != null)
                fluidDensityMap.Release();
            if (sandDensityMap != null)
                sandDensityMap.Release();
        }

        [ImageEffectOpaque]
        void OnRenderImage(RenderTexture src, RenderTexture target)
        {
            if (unifiedSim == null)
            {
                Graphics.Blit(src, target);
                return;
            }

            if (tempRT == null || tempRT.width != src.width || tempRT.height != src.height)
            {
                if (tempRT != null) tempRT.Release();
                tempRT = new RenderTexture(src.width, src.height, 0, src.format);
            }

            // Start with the source image
            Graphics.Blit(src, tempRT);

            // Update density maps
            UpdateDensityMaps();

            // Render fluid particles
            if (fluidMaterial != null)
            {
                SetShaderParams(fluidMaterial, fluidDensityMap);
                Graphics.Blit(tempRT, target, fluidMaterial);
                Graphics.Blit(target, tempRT);
            }

            // Render sand particles
            if (sandMaterial != null)
            {
                SetShaderParams(sandMaterial, sandDensityMap);
                SetSandSpecificParams(sandMaterial);
                Graphics.Blit(tempRT, target, sandMaterial);
            }
            else if (tempRT != target)
            {
                Graphics.Blit(tempRT, target);
            }
        }

        void UpdateDensityMaps()
        {
            if (densityMapCompute == null || unifiedSim == null) return;

            // Set compute shader parameters
            densityMapCompute.SetTexture(updateDensityMapsKernel, "FluidDensityMap", fluidDensityMap);
            densityMapCompute.SetTexture(updateDensityMapsKernel, "SandDensityMap", sandDensityMap);
            densityMapCompute.SetBuffer(updateDensityMapsKernel, "Particles", unifiedSim.GetParticleBuffer());
            densityMapCompute.SetInt("NumParticles", unifiedSim.GetParticleCount());
            densityMapCompute.SetInt("Resolution", densityMapResolution);
            densityMapCompute.SetFloat("ParticleRadius", particleRadius);
            densityMapCompute.SetFloat("DensityFalloff", densityFalloff);
            densityMapCompute.SetVector("BoundsSize", unifiedSim.boundsSize);
            densityMapCompute.SetVector("BoundsCenter", unifiedSim.transform.position);

            // Calculate dispatch size
            int numThreadGroups = Mathf.CeilToInt(unifiedSim.GetParticleCount() / 256f);
            densityMapCompute.Dispatch(updateDensityMapsKernel, numThreadGroups, 1, 1);
        }

        void SetShaderParams(Material material, RenderTexture densityMap)
        {
            SetEnvironmentParams(material, environmentSettings);
            material.SetTexture("DensityMap", densityMap);
            material.SetVector("boundsSize", unifiedSim.boundsSize);
            material.SetFloat("volumeValueOffset", densityOffset);
            material.SetVector("testParams", testParams);
            material.SetFloat("indexOfRefraction", indexOfRefraction);
            material.SetFloat("densityMultiplier", densityMultiplier / 1000);
            material.SetFloat("viewMarchStepSize", stepSize);
            material.SetFloat("lightStepSize", lightStepSize);
            material.SetInt("numRefractions", numRefractions);
            material.SetVector("extinctionCoeff", extinctionCoefficients);

            material.SetMatrix("cubeLocalToWorld", Matrix4x4.TRS(cubeTransform.position, cubeTransform.rotation, cubeTransform.localScale / 2));
            material.SetMatrix("cubeWorldToLocal", Matrix4x4.TRS(cubeTransform.position, cubeTransform.rotation, cubeTransform.localScale / 2).inverse);

            Vector3 floorSize = new Vector3(30, 0.05f, 30);
            float floorHeight = -unifiedSim.boundsSize.y / 2 + unifiedSim.transform.position.y - floorSize.y / 2;
            material.SetVector("floorPos", new Vector3(0, floorHeight, 0));
            material.SetVector("floorSize", floorSize);
        }

        void SetSandSpecificParams(Material material)
        {
            material.SetColor("_SandColor", sandColor);
            material.SetFloat("_SandSpecular", sandSpecular);
            material.SetFloat("_SandRoughness", sandRoughness);
            material.SetFloat("_GrainScale", grainScale);
        }

        public static void SetEnvironmentParams(Material mat, EnvironmentSettings environmentSettings)
        {
            mat.SetColor("tileCol1", environmentSettings.tileCol1);
            mat.SetColor("tileCol2", environmentSettings.tileCol2);
            mat.SetColor("tileCol3", environmentSettings.tileCol3);
            mat.SetColor("tileCol4", environmentSettings.tileCol4);
            mat.SetVector("tileColVariation", environmentSettings.tileColVariation);
            mat.SetFloat("tileScale", environmentSettings.tileScale);
            mat.SetFloat("tileDarkOffset", environmentSettings.tileDarkOffset);
            mat.SetVector("dirToSun", -environmentSettings.light.transform.forward);
        }

        [System.Serializable]
        public struct EnvironmentSettings
        {
            public Color tileCol1;
            public Color tileCol2;
            public Color tileCol3;
            public Color tileCol4;
            public Vector3 tileColVariation;
            public float tileScale;
            public float tileDarkOffset;
            public Light light;
        }
    }
}