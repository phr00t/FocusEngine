using Xenko.Animations;
using Xenko.Core.Mathematics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace Xenko.Importer.Assimp
{
    public static class Utils
    {
        public const int AI_MAX_NUMBER_OF_TEXTURECOORDS = 8;
        public const int AI_MAX_NUMBER_OF_COLOR_SETS = 8;

        public static Matrix ToXenkoMatrix(this Matrix4x4 matrix)
        {
            return new Matrix(
                matrix.M11, matrix.M12, matrix.M13, matrix.M14,
                matrix.M21, matrix.M22, matrix.M23, matrix.M24,
                matrix.M31, matrix.M32, matrix.M33, matrix.M34,
                matrix.M41, matrix.M42, matrix.M43, matrix.M44);
        }

        public static Core.Mathematics.Vector3 ToXenkoVector3(this System.Numerics.Vector3 v)
            => new Core.Mathematics.Vector3(v.X, v.Y, v.Z);

        public static Color ToXenkoColor(this System.Numerics.Vector4 v)
            => new Color(v.X, v.Y, v.Z, v.W);

        public static Core.Mathematics.Quaternion ToXenkoQuaternion(this System.Numerics.Quaternion q)
            => new Core.Mathematics.Quaternion(q.X, q.Y, q.Z, q.W);

        public static unsafe uint GetNumUVChannels(Silk.NET.Assimp.Mesh* mesh)
        {
            var n = 0;

            if (mesh->MTextureCoords.Element0 != null) n++;
            if (mesh->MTextureCoords.Element1 != null) n++;
            if (mesh->MTextureCoords.Element2 != null) n++;
            if (mesh->MTextureCoords.Element3 != null) n++;
            if (mesh->MTextureCoords.Element4 != null) n++;
            if (mesh->MTextureCoords.Element5 != null) n++;
            if (mesh->MTextureCoords.Element6 != null) n++;
            if (mesh->MTextureCoords.Element7 != null) n++;

            return (uint)n;
        }

        public static unsafe uint GetNumColorChannels(Silk.NET.Assimp.Mesh* mesh)
        {
            var n = 0;
            var AI_MAX_NUMBER_OF_COLOR_SETS = 8;
            while (n < AI_MAX_NUMBER_OF_COLOR_SETS && mesh->MColors[n] != null)
            {
                ++n;
            }

            return (uint)n;
        }

        public static CompressedTimeSpan AiTimeToXkTimeSpan(double time, double aiTickPerSecond)
        {
            var sdTime = CompressedTimeSpan.TicksPerSecond / aiTickPerSecond * time;
            return new CompressedTimeSpan((int)sdTime);
        }
    }
}
