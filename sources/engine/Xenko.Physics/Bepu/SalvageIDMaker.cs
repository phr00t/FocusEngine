using System;
using System.Collections.Generic;
using System.Text;

namespace Xenko.Physics.Salvage
{
    public static class SalvageIDMaker
    {
        //sets it to the int min
        static int nextID = -2147483648;
        //this method returns the next int. Technically misses the minimum...but shouldn't be a problem.
        public static int getNextID()
        {
            nextID += 1;
            return nextID;
        }
    }
}
