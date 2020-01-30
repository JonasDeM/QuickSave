// Author: Jonas De Maeseneer

using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;

namespace DotsPersistency
{
    public static unsafe class EntitiesExtensions
    {
        public static NativeArray<byte> GetComponentDataAsByteArray(this ArchetypeChunk archetypeChunk, ArchetypeChunkComponentTypeDynamic chunkComponentType)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (chunkComponentType.m_IsZeroSized)
                throw new ArgumentException($"ArchetypeChunk.GetComponentDataAsBytePtr cannot be called on zero-sized IComponentData");

            AtomicSafetyHandle.CheckReadAndThrow(chunkComponentType.m_Safety);
#endif
            var chunk = archetypeChunk.m_Chunk;
            
            var archetype = chunk->Archetype;
            ChunkDataUtility.GetIndexInTypeArray(chunk->Archetype, chunkComponentType.m_TypeIndex, ref chunkComponentType.m_TypeLookupCache);
            var typeIndexInArchetype = chunkComponentType.m_TypeLookupCache;
            if (typeIndexInArchetype == -1)
            {
                var emptyResult = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<byte>(null, 0, 0);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref emptyResult, chunkComponentType.m_Safety);
#endif
                return emptyResult;
            }

            int typeSize = archetype->SizeOfs[typeIndexInArchetype];
            var length = chunk->Count;
            int byteLen = length * typeSize;
            
            var buffer = chunk->Buffer;
            var startOffset = archetype->Offsets[typeIndexInArchetype];
            var result = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<byte>(buffer + startOffset, byteLen, Allocator.None);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref result, chunkComponentType.m_Safety);
#endif
            if (!chunkComponentType.IsReadOnly)
                chunk->SetChangeVersion(typeIndexInArchetype, chunkComponentType.GlobalSystemVersion);
            return result;
        }
    }
    
    public unsafe struct CopyComponentDataToByteArray : IJobChunk
    {
        [ReadOnly, NativeDisableParallelForRestriction]
        public ArchetypeChunkComponentTypeDynamic ChunkComponentType;
        public int TypeSize;
        [ReadOnly] 
        public ArchetypeChunkComponentType<PersistenceState> PersistenceStateType;
        [WriteOnly, NativeDisableParallelForRestriction]
        public NativeArray<byte> OutputData;
        [WriteOnly, NativeDisableParallelForRestriction]
        public NativeArray<bool> OutputFound;
            
        public void Execute(ArchetypeChunk chunk, int chunkIndex, int firstEntityIndex)
        {
            var byteArray = chunk.GetComponentDataAsByteArray(ChunkComponentType);
            var persistenceStateArray = chunk.GetNativeArray(PersistenceStateType);
            for (int i = 0; i < persistenceStateArray.Length; i++)
            {
                PersistenceState persistenceState = persistenceStateArray[i];
                int outputByteIndex = persistenceState.ArrayIndex * TypeSize;
                int compDataByteIndex = i * TypeSize;

                //var value1 = UnsafeUtility.ReadArrayElement<Translation>(ptr, i).Value;
                UnsafeUtility.MemCpy((byte*)OutputData.GetUnsafePtr() + outputByteIndex, (byte*)byteArray.GetUnsafeReadOnlyPtr() + compDataByteIndex, TypeSize);
                OutputFound[persistenceState.ArrayIndex] = true;
                //var value2 = UnsafeUtility.ReadArrayElement<Translation>((byte*)OutputData.GetUnsafeReadOnlyPtr(), persistenceState.ArrayIndex).Value;
                //Debug.Log(persistenceState.ArrayIndex + " - " + value1 + " - " + value2);
            }
        }
    }

    public struct FindTagComponentsOnPersistentEntities : IJobForEach<PersistenceState>
    {
        [WriteOnly, NativeDisableParallelForRestriction]
        public NativeArray<bool> OutputFound;

        public void Execute([ReadOnly] ref PersistenceState persistenceState)
        {
            OutputFound[persistenceState.ArrayIndex] = true;
        }
    }
    
    public unsafe struct AddMissingComponents : IJobChunk
    {        
        [ReadOnly, NativeDisableParallelForRestriction]
        public ArchetypeChunkEntityType EntityType;
        [ReadOnly] 
        public ArchetypeChunkComponentType<PersistenceState> PersistenceStateType;
        public EntityCommandBuffer.Concurrent Ecb;
        public ComponentType ComponentType;
        public int TypeSize;
        [ReadOnly, NativeDisableParallelForRestriction]
        public NativeArray<byte> InputData;
        [ReadOnly, NativeDisableParallelForRestriction]
        public NativeArray<bool> InputFound;
            
        public void Execute(ArchetypeChunk chunk, int chunkIndex, int firstEntityIndex)
        {
            var entityArray = chunk.GetNativeArray(EntityType);
            var persistenceStateArray = chunk.GetNativeArray(PersistenceStateType);
            for (int i = 0; i < entityArray.Length; i++)
            {
                PersistenceState persistenceState = persistenceStateArray[i];
                int inputByteIndex = persistenceState.ArrayIndex * TypeSize;

                if (InputFound[persistenceState.ArrayIndex])
                {
                    Ecb.AddComponent(chunkIndex, entityArray[i], ComponentType); 
                    if (TypeSize != 0) // todo optimization do check when scheduling & schedule different job that only Adds
                    {
                        Ecb.SetComponent(chunkIndex, entityArray[i], ComponentType, TypeSize, NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<byte>((byte*)InputData.GetUnsafeReadOnlyPtr() + inputByteIndex, TypeSize, Allocator.None));
                    }
                }
            }
        }
    }
    
    public struct RemoveComponents : IJobForEachWithEntity<PersistenceState>
    {        
        public EntityCommandBuffer.Concurrent Ecb;
        public ComponentType ComponentType;
        [ReadOnly, NativeDisableParallelForRestriction]
        public NativeArray<bool> InputFound;
        
        public void Execute(Entity entity, int index, [ReadOnly] ref PersistenceState persistenceState)
        {
            if (!InputFound[persistenceState.ArrayIndex])
            {
                Ecb.RemoveComponent(index, entity, ComponentType);
            }
        }
    }
    
    public unsafe struct CopyByteArrayToComponentData : IJobChunk
    {
        [NativeDisableContainerSafetyRestriction]
        public ArchetypeChunkComponentTypeDynamic ChunkComponentType;
        public int TypeSize;
        [ReadOnly] 
        public ArchetypeChunkComponentType<PersistenceState> PersistenceStateType;
        [ReadOnly]
        public NativeArray<byte> Input;
            
        public void Execute(ArchetypeChunk chunk, int chunkIndex, int firstEntityIndex)
        {
            var byteArray = chunk.GetComponentDataAsByteArray(ChunkComponentType);
            var persistenceStateArray = chunk.GetNativeArray(PersistenceStateType);
            
            for (int i = 0; i < persistenceStateArray.Length; i++)
            {
                PersistenceState persistenceState = persistenceStateArray[i];
                int inputByteIndex = persistenceState.ArrayIndex * TypeSize;
                int compDataByteIndex = i * TypeSize;

                //var value1 = UnsafeUtility.ReadArrayElement<Translation>((byte*)Input.GetUnsafeReadOnlyPtr(), persistenceState.ArrayIndex).Value;
                UnsafeUtility.MemCpy((byte*)byteArray.GetUnsafePtr() + compDataByteIndex, (byte*)Input.GetUnsafeReadOnlyPtr() + inputByteIndex, TypeSize);
                //var value2 = UnsafeUtility.ReadArrayElement<Translation>(ptr, i).Value;
                //Debug.Log(persistenceState.ArrayIndex + " - " + value1 + " - " + value2);
            }
        }
    }
}