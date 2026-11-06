using System.Numerics;

namespace Tessera.Models;

public enum MaterialMapKind { Height, Normal, AO }

public sealed record MaterialMapSnapshot(float[] Heights, Vector3[] Normals, float[] NormalMask,
    float[] Occlusion, float[] OcclusionMask, float NormalStrength, float AoStrength, int AoRadius, bool DirectX);
