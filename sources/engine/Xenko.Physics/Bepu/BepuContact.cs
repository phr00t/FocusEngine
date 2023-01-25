using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using Xenko.Engine;

namespace Xenko.Physics.Bepu
{
    public struct BepuContact
    {
        public BepuPhysicsComponent A, B;
        public Xenko.Core.Mathematics.Vector3 Normal, Offset;

        /// <summary>
        /// Swaps A and B so that B is always the "other" body, and A is myBody (if it wasn't already)
        /// </summary>
        /// <param name="myBody">The "host" body of the contact</param>
        /// <returns>Returns the "other" body that hit myBody</returns>
        public BepuPhysicsComponent SwapIfNeeded(BepuRigidbodyComponent myBody)
        {
            if (B == myBody) Swap();
            return B;
        }

        /// <summary>
        /// Swaps A and B. Also flips the normal and corrects offset
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Swap()
        {
            Normal.X = -Normal.X;
            Normal.Y = -Normal.Y;
            Normal.Z = -Normal.Z;
            Offset = B.Position - (A.Position + Offset);
            var C = A;
            A = B;
            B = C;
        }
    }
}
