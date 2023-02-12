// Author: Jonas De Maeseneer

using Unity.Entities;
using Hash128 = Unity.Entities.Hash128;

namespace QuickSave
{
    // Container components
    public struct QuickSaveDataContainer : IComponentData
    {
        public Hash128 GUID;
        public ulong DataLayoutHash;
        public int FrameIdentifier;
        public int EntityCapacity;
        public Entity InitialContainer;

        public bool ValidData;
        
        [InternalBufferCapacity(16)]
        public struct Data : IBufferElementData
        {
            public byte Byte;
        }
    }
    
    // This structure has all the details needed to write/read from a sub-array of a large contiguous array for 1 specific QuickSaveArchetype & an amount
    public struct QuickSaveArchetypeDataLayout : IBufferElementData
    {
        public BlobAssetReference<BlobArray<TypeInfo>> TypeInfoArrayRef;
        public int Amount;
        public int Offset;
        public int SizePerEntity;

        public struct TypeInfo
        {
            public int ElementSize;
            public int MaxElements;
            public int Offset;
            public QuickSaveTypeHandle QuickSaveTypeHandle;
            public bool IsBuffer;
            public int SizePerEntityForThisType => (ElementSize * MaxElements) + QuickSaveMetaData.SizeOfStruct;
        }
    }

    public struct DataTransferRequest : IBufferElementData
    {
        public enum Type : byte
        {
            FromEntitiesToDataContainer, // = Persist
            FromDataContainerToEntities  // = Apply
        }
    
        public Type RequestType;
        public SystemHandle ExecutingSystem; // Specify which system needs to execute this request
    }
}