// Author: Jonas De Maeseneer

using System;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Entities;

namespace DotsPersistency
{
    // A persisting entity needs this component
    // It holds which data it needs to persist
    public struct PersistedTypes : ISharedComponentData
    {
        // Todo optimization during runtime we should have mapped these to indices
        public FixedList128<ulong> ComponentDataTypeHashList; // can store 15 type hashes
        public FixedList64<ulong> BufferElementTypeHashList; // can store 7 type hashes
    }
    
    // A persisting entity needs this component
    // It holds the index into the arrays which hold the persisted data
    // Entities their data will reside in the same arrays if they have the same SharedComponentData for SceneSection & PersistedTypes
    public struct PersistenceState : IComponentData, IEquatable<PersistenceState>
    {
        public int ArrayIndex;

        public bool Equals(PersistenceState other)
        {
            return ArrayIndex == other.ArrayIndex;
        }
    }
}