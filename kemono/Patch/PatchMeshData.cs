using System;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace kemono;

// https://github.com/anegostudios/vsapi/blob/master/Client/Model/Mesh/MeshData.cs
// Fixing normalization in Flags
[HarmonyPatch(typeof(MeshData), "MatrixTransform", new Type[] { typeof(double[]) })]
class PatchMeshData
{
    static bool Prefix(MeshData __instance, double[] matrix, ref MeshData __result)
    {
        // For performance, before proceeding with a full-scale matrix operation on the whole mesh, we test whether the matrix is a translation-only matrix, meaning a matrix with no scaling or rotation.
        // (This can also include an identity matrix as a special case, as an identity matrix also has no scaling and no rotation: the "translation" in that case is (0,0,0))
        if (Mat4d.IsTranslationOnly(matrix))
        {
            __instance.Translate((float)matrix[12], (float)matrix[13], (float)matrix[14]);    // matrix[12] is dX, matrix[13] is dY,  matrix[14] is dZ
            __result = __instance;
            return false;
        }

        // stack allocated normal float4 (x,y,z,w)
        Span<float> nf = stackalloc float[4];

        var xyz = __instance.xyz;
        var Normals = __instance.Normals;

        for (int i = 0; i < __instance.VerticesCount; i++)
        {
            // Keep this code - it's more readable than below inlined method. It's worth inlining because this method is called during Shape tesselation, which has *a lot* of shapes to load
            //double[] pos = new double[] { 0, 0, 0, 1 };
            /*pos[0] = xyz[i * 3];
            pos[1] = xyz[i * 3 + 1];
            pos[2] = xyz[i * 3 + 2];

            pos = Mat4d.MulWithVec4(matrix, pos);

            xyz[i * 3] = (float)pos[0];
            xyz[i * 3 + 1] = (float)pos[1];
            xyz[i * 3 + 2] = (float)pos[2];*/

            // Inlined version of above code
            float x = xyz[i * 3];
            float y = xyz[i * 3 + 1];
            float z = xyz[i * 3 + 2];
            xyz[i * 3 + 0] = (float)(matrix[4 * 0 + 0] * x + matrix[4 * 1 + 0] * y + matrix[4 * 2 + 0] * z + matrix[4 * 3 + 0]);
            xyz[i * 3 + 1] = (float)(matrix[4 * 0 + 1] * x + matrix[4 * 1 + 1] * y + matrix[4 * 2 + 1] * z + matrix[4 * 3 + 1]);
            xyz[i * 3 + 2] = (float)(matrix[4 * 0 + 2] * x + matrix[4 * 1 + 2] * y + matrix[4 * 2 + 2] * z + matrix[4 * 3 + 2]);


            if (Normals != null)
            {
                FromPackedNormal(Normals[i], nf);
                MulWithVec4(matrix, nf);
                Normals[i] = PackNormal(nf[0], nf[1], nf[2]);
            }
        }

        if (__instance.XyzFaces != null)
        {
            var XyzFaces = __instance.XyzFaces;
            for (int i = 0; i < XyzFaces.Length; i++)
            {
                byte meshFaceIndex = XyzFaces[i];
                if (meshFaceIndex == 0) continue;

                Vec3d normald = BlockFacing.ALLFACES[meshFaceIndex - 1].Normald;

                // Inlined version of above code
                double ox = matrix[4 * 0 + 0] * normald.X + matrix[4 * 1 + 0] * normald.Y + matrix[4 * 2 + 0] * normald.Z;
                double oy = matrix[4 * 0 + 1] * normald.X + matrix[4 * 1 + 1] * normald.Y + matrix[4 * 2 + 1] * normald.Z;
                double oz = matrix[4 * 0 + 2] * normald.X + matrix[4 * 1 + 2] * normald.Y + matrix[4 * 2 + 2] * normald.Z;

                BlockFacing rotatedFacing = BlockFacing.FromVector(ox, oy, oz);

                XyzFaces[i] = rotatedFacing.MeshDataIndex;
            }
        }

        if (__instance.Flags != null)
        {
            Span<double> n = stackalloc double[3];
            var flags = __instance.Flags;
            for (int i = 0; i < flags.Length; i++)
            {
                UnpackNormalFromFlags(flags[i], n);

                // Inlined version of above code
                double ox = matrix[4 * 0 + 0] * n[0] + matrix[4 * 1 + 0] * n[1] + matrix[4 * 2 + 0] * n[2];
                double oy = matrix[4 * 0 + 1] * n[0] + matrix[4 * 1 + 1] * n[1] + matrix[4 * 2 + 1] * n[2];
                double oz = matrix[4 * 0 + 2] * n[0] + matrix[4 * 1 + 2] * n[1] + matrix[4 * 2 + 2] * n[2];

                double invLen = 1.0 / Math.Sqrt(ox * ox + oy * oy + oz * oz);

                flags[i] = (flags[i] & ~VertexFlags.NormalBitMask) | (VertexFlags.PackNormal(ox * invLen, oy * invLen, oz * invLen));
            }
        }

        __result = __instance;

        return false;
    }

    const int nValueBitMask = 0b1110;
    const int nXValueBitMask = nValueBitMask << (VertexFlags.NormalBitPos);
    const int nYValueBitMask = nValueBitMask << (VertexFlags.NormalBitPos + 4);
    const int nZValueBitMask = nValueBitMask << (VertexFlags.NormalBitPos + 8);

    const int nXSignBitPos = VertexFlags.NormalBitPos - 1;
    const int nYSignBitPos = VertexFlags.NormalBitPos + 3;
    const int nZSignBitPos = VertexFlags.NormalBitPos + 7;


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void UnpackNormalFromFlags(int vertexFlags, Span<double> n)
    {
        int x = vertexFlags & nXValueBitMask;
        int y = vertexFlags & nYValueBitMask;
        int z = vertexFlags & nZValueBitMask;

        int signx = 1 - ((vertexFlags >> nXSignBitPos) & 2);
        int signy = 1 - ((vertexFlags >> nYSignBitPos) & 2);
        int signz = 1 - ((vertexFlags >> nZSignBitPos) & 2);

        n[0] = signx * x / (14f * (1 << VertexFlags.NormalBitPos));
        n[1] = signy * y / (14f * 16 * (1 << VertexFlags.NormalBitPos));
        n[2] = signz * z / (14f * 256 * (1 << VertexFlags.NormalBitPos));
    }


    // https://github.com/anegostudios/vsapi/blob/master/Client/Model/Mesh/NormalUtil.cs
    const int NegBit = 1 << 9;
    const int tenBitMask = 0x3FF;
    const int nineBitMask = 0x1FF;
    const int tenthBitMask = 0x200;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void FromPackedNormal(int normal, Span<float> nf)
    {
        int normal0 = normal;
        int normal1 = normal >> 10;
        int normal2 = normal >> 20;

        bool xNeg = (tenthBitMask & normal0) > 0;
        bool yNeg = (tenthBitMask & normal1) > 0;
        bool zNeg = (tenthBitMask & normal2) > 0;

        nf[0] = (xNeg ? (~normal0 & nineBitMask) : normal0 & nineBitMask) / 512f;
        nf[1] = (yNeg ? (~normal1 & nineBitMask) : normal1 & nineBitMask) / 512f;
        nf[2] = (zNeg ? (~normal2 & nineBitMask) : normal2 & nineBitMask) / 512f;
        nf[3] = normal >> 30;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int PackNormal(float x, float y, float z)
    {
        bool xNeg = x < 0;
        bool yNeg = y < 0;
        bool zNeg = z < 0;

        int normalX = xNeg ? (NegBit | ~(int)Math.Abs(x * 511) & nineBitMask) : ((int)(x * 511) & nineBitMask);
        int normalY = yNeg ? (NegBit | ~(int)Math.Abs(y * 511) & nineBitMask) : ((int)(y * 511) & nineBitMask);
        int normalZ = zNeg ? (NegBit | ~(int)Math.Abs(z * 511) & nineBitMask) : ((int)(z * 511) & nineBitMask);

        return (normalX << 0) | (normalY << 10) | (normalZ << 20);
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void MulWithVec4(double[] matrix, Span<float> nf)
    {
        // cast here once
        double nx = nf[0];
        double ny = nf[1];
        double nz = nf[2];
        double nw = nf[3];

        Span<double> output = stackalloc double[4];

        for (int row = 0; row < 4; row++)
        {
            // unrolled
            output[row] += matrix[4 * 0 + row] * nx;
            output[row] += matrix[4 * 1 + row] * ny;
            output[row] += matrix[4 * 2 + row] * nz;
            output[row] += matrix[4 * 3 + row] * nw;
        }

        nf[0] = (float)output[0];
        nf[1] = (float)output[1];
        nf[2] = (float)output[2];
        nf[3] = (float)output[3];
    }
}
