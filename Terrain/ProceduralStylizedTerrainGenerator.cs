using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(Terrain))]
public class ProceduralStylizedTerrainGenerator : MonoBehaviour
{
    private const int HeightmapResolution = 513;
    private const int AlphamapResolution = 512;
    private const int DetailResolution = 512;

    [Header("Terrain")]
    [SerializeField] private float terrainWidth = 1200f;
    [SerializeField] private float terrainLength = 1200f;
    [SerializeField] private float terrainHeight = 260f;

    [Header("Generation")]
    [SerializeField] private int seed = 1337;
    [SerializeField, Range(0f, 1f)] private float terrainRoughness = 0.35f;
    [SerializeField, Range(0f, 1f)] private float heightVariation = 0.45f;

    [Header("Walkability")]
    [SerializeField] private bool improveWalkability = true;
    [SerializeField, Range(0f, 1f)] private float walkabilityStrength = 0.5f;

    [Header("Layers")]
    [SerializeField] private List<TerrainLayer> terrainLayers = new List<TerrainLayer>();

    [Header("Preview")]
    [SerializeField] private bool regenerateOnValidate = false;

    private Terrain terrain;
    private readonly AnimationCurve heightCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    private const float BaseFrequency = 2.2f;
    private const int Octaves = 5;
    private const float Lacunarity = 2.0f;
    private const float Persistence = 0.5f;
    private const float RidgeStrength = 0.65f;
    private const float MacroWarp = 0.12f;
    private const float BowlStrength = 0.2f;
    private const bool ApplyThermalErosion = true;
    private const int ErosionIterations = 20;
    private const float Talus = 0.008f;
    private const float ErosionRate = 0.4f;
    private const float MaxWalkableSlope = 18f;
    private const float WalkableHeightLimit = 0.82f;
    private const int WalkableSmoothingPasses = 1;
    private const float SteepRockStart = 35f;
    private const float SteepRockFull = 55f;
    private const float HighlandStart = 0.58f;
    private const float HighlandFull = 0.82f;
    private void Awake()
    {
        CacheTerrain();
    }

    private void OnValidate()
    {
        if (!regenerateOnValidate)
        {
            return;
        }

        if (!Application.isPlaying)
        {
            Generate();
        }
    }

    [ContextMenu("Generate Stylized Terrain")]
    public void Generate()
    {
        CacheTerrain();
        if (terrain == null)
        {
            return;
        }

        TerrainData data = terrain.terrainData;
        if (data == null)
        {
            data = new TerrainData();
            terrain.terrainData = data;
        }

        int hmRes = Mathf.ClosestPowerOfTwo(Mathf.Max(33, HeightmapResolution - 1)) + 1;
        int amRes = Mathf.ClosestPowerOfTwo(Mathf.Max(16, AlphamapResolution));
        int dtRes = Mathf.ClosestPowerOfTwo(Mathf.Max(16, DetailResolution));

        data.heightmapResolution = hmRes;
        data.alphamapResolution = amRes;
        data.SetDetailResolution(dtRes, 8);
        data.size = new Vector3(
            Mathf.Max(1f, terrainWidth),
            Mathf.Max(1f, terrainHeight),
            Mathf.Max(1f, terrainLength));

        float[,] heights = BuildHeights(hmRes);
        if (ApplyThermalErosion)
        {
            RunThermalErosion(heights, ErosionIterations);
        }

        if (improveWalkability)
        {
            ImproveWalkability(heights, hmRes);
        }

        data.SetHeights(0, 0, heights);

        if (terrainLayers != null && terrainLayers.Count > 0)
        {
            data.terrainLayers = terrainLayers.ToArray();
            PaintLegacySplatmaps(data, heights);
        }
    }

    private void CacheTerrain()
    {
        if (terrain == null)
        {
            terrain = GetComponent<Terrain>();
        }
    }

    private float[,] BuildHeights(int resolution)
    {
        float[,] heights = new float[resolution, resolution];
        Vector2 offsetA = HashToOffset(seed);
        Vector2 offsetB = HashToOffset(seed * 31 + 17);

        for (int z = 0; z < resolution; z++)
        {
            float vz = z / (float)(resolution - 1);
            for (int x = 0; x < resolution; x++)
            {
                float vx = x / (float)(resolution - 1);

                Vector2 uv = new Vector2(vx, vz);
                float variation = Mathf.Lerp(0.02f, 0.3f, Mathf.Clamp01(heightVariation));

                // Smooth hills with visible elevation change, still no sharp ridges.
                float veryBroad = FractalNoise(uv, 0.28f, 2, 2.0f, 0.5f, offsetA);
                float broad = FractalNoise(uv, 0.62f, 3, 2.0f, 0.5f, offsetB);
                float mediumFreq = Mathf.Lerp(0.9f, 1.7f, Mathf.Clamp01(terrainRoughness));
                float medium = FractalNoise(uv, mediumFreq, 2, 2.0f, 0.5f, offsetA + Vector2.one * 17.23f);
                float combined = veryBroad * 0.48f + broad * 0.34f + medium * 0.18f;
                combined = Mathf.SmoothStep(0f, 1f, combined);
                float contrast = Mathf.Lerp(1.15f, 1.55f, Mathf.Clamp01(terrainRoughness));
                combined = Mathf.Clamp01((combined - 0.5f) * contrast + 0.5f);

                float h = 0.42f + (combined - 0.5f) * 2f * variation;
                h = Mathf.Clamp01(h);

                heights[z, x] = h;
            }
        }

        return heights;
    }

    private void RunThermalErosion(float[,] heights, int iterations)
    {
        int size = heights.GetLength(0);
        float localTalus = Mathf.Max(0f, Talus);
        float localRate = Mathf.Clamp01(ErosionRate);

        for (int it = 0; it < Mathf.Max(0, iterations); it++)
        {
            for (int z = 1; z < size - 1; z++)
            {
                for (int x = 1; x < size - 1; x++)
                {
                    float h = heights[z, x];
                    int nx = x;
                    int nz = z;
                    float maxDrop = 0f;

                    for (int oz = -1; oz <= 1; oz++)
                    {
                        for (int ox = -1; ox <= 1; ox++)
                        {
                            if (ox == 0 && oz == 0)
                            {
                                continue;
                            }

                            float n = heights[z + oz, x + ox];
                            float drop = h - n;
                            if (drop > maxDrop)
                            {
                                maxDrop = drop;
                                nx = x + ox;
                                nz = z + oz;
                            }
                        }
                    }

                    if (maxDrop <= localTalus)
                    {
                        continue;
                    }

                    float amount = (maxDrop - localTalus) * 0.5f * localRate;
                    heights[z, x] -= amount;
                    heights[nz, nx] += amount;
                }
            }
        }
    }

    private void ImproveWalkability(float[,] heights, int resolution)
    {
        float strength = Mathf.Clamp01(walkabilityStrength);
        if (strength <= 0.0001f)
        {
            return;
        }

        float stepX = terrainWidth / Mathf.Max(1f, resolution - 1f);
        float stepZ = terrainLength / Mathf.Max(1f, resolution - 1f);
        float heightScale = Mathf.Max(1f, terrainHeight);
        float limitHeight = Mathf.Clamp01(WalkableHeightLimit);
        float slopeLimit = Mathf.Max(1f, MaxWalkableSlope);

        for (int z = 1; z < resolution - 1; z++)
        {
            for (int x = 1; x < resolution - 1; x++)
            {
                float h = heights[z, x];
                if (h > limitHeight)
                {
                    continue;
                }

                float hx0 = heights[z, x - 1] * heightScale;
                float hx1 = heights[z, x + 1] * heightScale;
                float hz0 = heights[z - 1, x] * heightScale;
                float hz1 = heights[z + 1, x] * heightScale;

                float dx = (hx1 - hx0) / (2f * Mathf.Max(0.001f, stepX));
                float dz = (hz1 - hz0) / (2f * Mathf.Max(0.001f, stepZ));
                float slopeDeg = Mathf.Atan(Mathf.Sqrt(dx * dx + dz * dz)) * Mathf.Rad2Deg;
                if (slopeDeg <= slopeLimit)
                {
                    continue;
                }

                float nAvg =
                    heights[z - 1, x - 1] +
                    heights[z - 1, x] +
                    heights[z - 1, x + 1] +
                    heights[z, x - 1] +
                    heights[z, x + 1] +
                    heights[z + 1, x - 1] +
                    heights[z + 1, x] +
                    heights[z + 1, x + 1];
                nAvg /= 8f;

                float steepFactor = Mathf.InverseLerp(slopeLimit, slopeLimit + 20f, slopeDeg);
                float blend = strength * steepFactor;
                heights[z, x] = Mathf.Lerp(h, nAvg, blend);
            }
        }

        for (int pass = 0; pass < Mathf.Max(0, WalkableSmoothingPasses); pass++)
        {
            float[,] tmp = (float[,])heights.Clone();
            for (int z = 1; z < resolution - 1; z++)
            {
                for (int x = 1; x < resolution - 1; x++)
                {
                    float h = tmp[z, x];
                    if (h > limitHeight)
                    {
                        continue;
                    }

                    float hx0 = tmp[z, x - 1] * heightScale;
                    float hx1 = tmp[z, x + 1] * heightScale;
                    float hz0 = tmp[z - 1, x] * heightScale;
                    float hz1 = tmp[z + 1, x] * heightScale;
                    float dx = (hx1 - hx0) / (2f * Mathf.Max(0.001f, stepX));
                    float dz = (hz1 - hz0) / (2f * Mathf.Max(0.001f, stepZ));
                    float localSlopeDeg = Mathf.Atan(Mathf.Sqrt(dx * dx + dz * dz)) * Mathf.Rad2Deg;
                    if (localSlopeDeg <= slopeLimit * 0.65f)
                    {
                        continue;
                    }

                    float nAvg =
                        tmp[z - 1, x - 1] +
                        tmp[z - 1, x] +
                        tmp[z - 1, x + 1] +
                        tmp[z, x - 1] +
                        tmp[z, x] * 2f +
                        tmp[z, x + 1] +
                        tmp[z + 1, x - 1] +
                        tmp[z + 1, x] +
                        tmp[z + 1, x + 1];
                    nAvg /= 10f;

                    float slopeFactor = Mathf.InverseLerp(slopeLimit * 0.65f, slopeLimit + 20f, localSlopeDeg);
                    heights[z, x] = Mathf.Lerp(h, nAvg, strength * 0.45f * slopeFactor);
                }
            }
        }
    }

    private void PaintLegacySplatmaps(TerrainData data, float[,] heights)
    {
        int aRes = data.alphamapResolution;
        int layerCount = data.terrainLayers.Length;
        if (layerCount == 0)
        {
            return;
        }

        float[,,] splat = new float[aRes, aRes, layerCount];
        for (int z = 0; z < aRes; z++)
        {
            float vz = z / (float)(aRes - 1);
            for (int x = 0; x < aRes; x++)
            {
                float vx = x / (float)(aRes - 1);
                float h01 = SampleHeights01(heights, vx, vz);
                float steep = data.GetSteepness(vx, vz);

                float rock = Mathf.InverseLerp(SteepRockStart, SteepRockFull, steep);
                float high = Mathf.InverseLerp(HighlandStart, HighlandFull, h01);
                float low = Mathf.Clamp01(1f - high);

                float w0 = Mathf.Clamp01(low * (1f - rock));
                float w1 = Mathf.Clamp01(rock);
                float w2 = Mathf.Clamp01(high * (1f - rock));

                float sum = w0 + w1 + w2;
                if (sum <= 0.0001f)
                {
                    w0 = 1f;
                    sum = 1f;
                }

                w0 /= sum;
                w1 /= sum;
                w2 /= sum;

                if (layerCount >= 1) splat[z, x, 0] = w0;
                if (layerCount >= 2) splat[z, x, 1] = w1;
                if (layerCount >= 3) splat[z, x, 2] = w2;
                for (int i = 3; i < layerCount; i++)
                {
                    splat[z, x, i] = 0f;
                }
            }
        }

        data.SetAlphamaps(0, 0, splat);
    }

    private static float SampleHeights01(float[,] heights, float u, float v)
    {
        int size = heights.GetLength(0);
        float fx = Mathf.Clamp01(u) * (size - 1);
        float fz = Mathf.Clamp01(v) * (size - 1);

        int x0 = Mathf.FloorToInt(fx);
        int z0 = Mathf.FloorToInt(fz);
        int x1 = Mathf.Min(size - 1, x0 + 1);
        int z1 = Mathf.Min(size - 1, z0 + 1);

        float tx = fx - x0;
        float tz = fz - z0;

        float a = Mathf.Lerp(heights[z0, x0], heights[z0, x1], tx);
        float b = Mathf.Lerp(heights[z1, x0], heights[z1, x1], tx);
        return Mathf.Lerp(a, b, tz);
    }

    private static Vector2 Noise2D(Vector2 p)
    {
        return new Vector2(
            Mathf.PerlinNoise(p.x, p.y),
            Mathf.PerlinNoise(p.x + 83.41f, p.y + 17.13f));
    }

    private static float FractalNoise(Vector2 uv, float frequency, int octs, float lac, float pers, Vector2 offset)
    {
        int count = Mathf.Max(1, octs);
        float amp = 1f;
        float sum = 0f;
        float norm = 0f;
        Vector2 p = uv + offset;
        float f = Mathf.Max(0.0001f, frequency);

        for (int i = 0; i < count; i++)
        {
            float n = Mathf.PerlinNoise(p.x * f, p.y * f);
            sum += n * amp;
            norm += amp;
            amp *= Mathf.Clamp01(pers);
            f *= Mathf.Max(1.01f, lac);
        }

        return norm > 0f ? sum / norm : 0f;
    }

    private static float RidgedNoise(Vector2 uv, float frequency, int octs, float lac, float pers, Vector2 offset)
    {
        int count = Mathf.Max(1, octs);
        float amp = 1f;
        float sum = 0f;
        float norm = 0f;
        Vector2 p = uv + offset;
        float f = Mathf.Max(0.0001f, frequency);

        for (int i = 0; i < count; i++)
        {
            float n = Mathf.PerlinNoise(p.x * f, p.y * f);
            n = 1f - Mathf.Abs(n * 2f - 1f);
            n *= n;
            sum += n * amp;
            norm += amp;
            amp *= Mathf.Clamp01(pers);
            f *= Mathf.Max(1.01f, lac);
        }

        return norm > 0f ? sum / norm : 0f;
    }

    private static Vector2 HashToOffset(int value)
    {
        uint x = (uint)value;
        x ^= x >> 17;
        x *= 0xed5ad4bb;
        x ^= x >> 11;
        x *= 0xac4c1b51;
        x ^= x >> 15;
        x *= 0x31848bab;
        x ^= x >> 14;
        float ox = (x & 0xFFFF) / 65535f * 1000f;
        float oy = ((x >> 16) & 0xFFFF) / 65535f * 1000f;
        return new Vector2(ox, oy);
    }
}
