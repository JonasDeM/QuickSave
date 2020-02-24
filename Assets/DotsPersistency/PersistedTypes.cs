// Author: Jonas De Maeseneer

using System;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Entities;

namespace DotsPersistency
{
    public struct PersistedTypes : ISharedComponentData
    {
        // Todo optimization during runtime we should have mapped these to indices
        public FixedList128<ulong> ComponentDataTypeHashList; // can store 15 type hashes
        public FixedList64<ulong> BufferElementTypeHashList; // can store 7 type hashes
    }
    
    public struct PersistenceState : IComponentData, IEquatable<PersistenceState>
    {
        public int ArrayIndex;

        public bool Equals(PersistenceState other)
        {
            return ArrayIndex == other.ArrayIndex;
        }
    }
}