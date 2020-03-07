// Author: Jonas De Maeseneer

using System;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Entities;

namespace DotsPersistency
{
    // A persisting entity needs this component
    // It holds which data it needs to persist
    public struct PersistenceArchetype : ISharedComponentData
    {
        public BlobAssetReference<BlobArray<PersistedTypeInfo>> PersistedTypeInfoArrayRef;
        public int Amount;
        public int Offset; // Byte Offset in the Byte Array which contains all data for 1 SceneSection
        public int ArchetypeIndex; // Index specifically for this SceneSection
        public int SizePerEntity;
        
        // Todo optimization during runtime we should have mapped these to indices
        public FixedList128<ulong> ComponentDataTypeHashList; // can store 15 type hashes
        public FixedList64<ulong> BufferElementTypeHashList; // can store 7 type hashes
    }

    public struct PersistedTypeInfo
    {
        public ulong StableHash;
        public int ElementSize;
        public int MaxElements;
        public bool IsBuffer;
        public int Offset; // Byte Offset in the Byte Sub Array which contains all data for a PersistenceArchetype in 1 SceneSection
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