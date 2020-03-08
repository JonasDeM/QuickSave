// Author: Jonas De Maeseneer

using System.Runtime.CompilerServices;
using System;
using Unity.Collections;
using Unity.Entities;

[assembly:InternalsVisibleTo("io.jonasdem.dotspersistency.hybrid")]

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
        
        // todo remove
        public FixedList128<ulong> ComponentDataTypeHashList; // can store 15 type hashes
        public FixedList64<ulong> BufferElementTypeHashList; // can store 7 type hashes
    }
    
    public struct TypeHashesToPersist : ISharedComponentData
    {
        public FixedList128<ulong> TypeHashList; // can store 15 type hashes
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
    // It holds the index into the sub array that holds the persisted data
    // Entities their data will reside in the same arrays if they have the same SharedComponentData for SceneSection & PersistenceArchetype
    public struct PersistenceState : IComponentData, IEquatable<PersistenceState>
    {
        public int ArrayIndex;

        public bool Equals(PersistenceState other)
        {
            return ArrayIndex == other.ArrayIndex;
        }
    }
}































