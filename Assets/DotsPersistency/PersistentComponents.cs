// Author: Jonas De Maeseneer

using System;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Entities;

namespace DotsPersistency
{
    public struct PersistentComponents : ISharedComponentData
    {
        // Todo optimization during runtime we should have mapped these to indices
        public FixedList128<ulong> TypeHashList; // can store 15 type hashes
    }
    
    public struct PersistenceState : IComponentData, IEquatable<PersistenceState>
    {
        public int ArrayIndex;

        public bool Equals(PersistenceState other)
        {
            return ArrayIndex == other.ArrayIndex;
        }
    }
    
    public struct PersistenceSystemState : ISystemStateComponentData
    {
    }
}