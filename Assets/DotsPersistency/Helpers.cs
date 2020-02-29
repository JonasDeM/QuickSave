// Author: Jonas De Maeseneer

using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace DotsPersistency
{
    public static unsafe class EntitiesExtensions
    {
        // todo move to entities package
        public static NativeArray<byte> GetComponentDataAsByteArray(this ArchetypeChunk archetypeChunk, ArchetypeChunkComponentTypeDynamic chunkComponentType)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (chunkComponentType.m_IsZeroSized)
                throw new ArgumentException($"ArchetypeChunk.GetComponentDataAsByteArray cannot be called on zero-sized IComponentData");

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
    
    [BurstCompile]
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

                UnsafeUtility.MemCpy((byte*)OutputData.GetUnsafePtr() + outputByteIndex, (byte*)byteArray.GetUnsafeReadOnlyPtr() + compDataByteIndex, TypeSize);
                OutputFound[persistenceState.ArrayIndex] = true;
            }
        }
    }
    
    public unsafe struct CopyBufferElementsToByteArray : IJobChunk
    {
        [NativeDisableParallelForRestriction, ReadOnly]
        public ArchetypeChunkBufferDataTypeDynamic ChunkBufferType;
        public int ElementSize;
        public int MaxElements;
        [ReadOnly] 
        public ArchetypeChunkComponentType<PersistenceState> PersistenceStateType;
        [WriteOnly, NativeDisableParallelForRestriction]
        public NativeArray<byte> OutputData;
        [WriteOnly, NativeDisableParallelForRestriction]
        public NativeArray<int> AmountPersisted;
            
        public void Execute(ArchetypeChunk chunk, int chunkIndex, int firstEntityIndex)
        {
            var untypedBufferAccessor = chunk.GetUntypedBufferAccessor(ChunkBufferType);
            var persistenceStateArray = chunk.GetNativeArray(PersistenceStateType);
            for (int i = 0; i < persistenceStateArray.Length; i++)
            {
                PersistenceState persistenceState = persistenceStateArray[i];
                var untypedBuffer = untypedBufferAccessor[i];
                int outputByteIndex = persistenceState.ArrayIndex * ElementSize * MaxElements;
                int amountElements = untypedBuffer.Length / ElementSize;
                
                // todo instead of clamp, Malloc when amountElements exceeds MaxElements (Rename MaxElements to InternalCapacity)
                // make sure OutputData can store a pointer per entity & then just store the pointer
                // be sure to dispose of the memory when AmountPersisted exceeds InternalCapacity
                int amountToCopy = math.clamp(amountElements, 0, MaxElements);
                
                // when safety check bug is fixed move this back to .GetUnsafePtr
                UnsafeUtility.MemCpy((byte*)OutputData.GetUnsafePtr() + outputByteIndex, NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(untypedBuffer), ElementSize * amountToCopy);
                AmountPersisted[persistenceState.ArrayIndex] = amountToCopy;
            }
        }
    }

    public struct FindPersistentEntities : IJobForEach<PersistenceState>
    {
        [WriteOnly, NativeDisableParallelForRestriction]
        public NativeArray<bool> OutputFound;

        public void Execute([ReadOnly] ref PersistenceState persistenceState)
        {
            OutputFound[persistenceState.ArrayIndex] = true;
        }
    }
    
    public struct AddMissingComponents : IJobChunk
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
                    if (TypeSize != 0) // todo micro optimization do check when scheduling & schedule different job that only Adds
                    {
                        Ecb.SetComponent(chunkIndex, entityArray[i], ComponentType, TypeSize, InputData.GetSubArray(inputByteIndex, TypeSize));
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
    
    public struct DestroyEntities : IJobForEachWithEntity<PersistenceState>
    {        
        public EntityCommandBuffer.Concurrent Ecb;
        [ReadOnly, NativeDisableParallelForRestriction]
        public NativeArray<bool> InputFound;
        
        public void Execute(Entity entity, int index, [ReadOnly] ref PersistenceState persistenceState)
        {
            if (!InputFound[persistenceState.ArrayIndex])
            {
                Ecb.DestroyEntity(index, entity);
            }
        }
    }
    
    public unsafe struct CreateEntities : IJobParallelFor
    {
        public EntityCommandBuffer.Concurrent Ecb;
        public SceneSection SceneSection;
        public PersistedTypes PersistedTypes;
        [ReadOnly]
        public NativeArray<bool>  EntitiesFound;
        
        [ReadOnly, DeallocateOnJobCompletion]
        public NativeArray<IntPtr>  ArrayOfInputFoundArrays; // [TypeIndex][ArrayIndex]
        [ReadOnly, DeallocateOnJobCompletion]
        public NativeArray<IntPtr>  ArrayOfInputDataArrays; // [TypeIndex][ArrayIndex]
        [ReadOnly, DeallocateOnJobCompletion]
        public NativeArray<ComponentType>  ComponentTypesToAdd;
        [ReadOnly, DeallocateOnJobCompletion]
        public NativeArray<int>  ComponentTypesSizes;
        [ReadOnly, DeallocateOnJobCompletion]
        public NativeArray<PersistenceState> ExistingEntities;
        
        public void Execute(int index)
        {
            var persistenceState = new PersistenceState {ArrayIndex = index};
            if (EntitiesFound[index] && !ExistingEntities.Contains(persistenceState))
            {
                var entity = Ecb.CreateEntity(index);
                for (int i = 0; i < ComponentTypesToAdd.Length; i++)
                {
                    bool foundComponent = *((bool*) ArrayOfInputFoundArrays[i].ToPointer() + index);
                    if (foundComponent)
                    {
                        Ecb.AddComponent(index, entity, ComponentTypesToAdd[i]);
                        var typeSize = ComponentTypesSizes[i];
                        if (typeSize > 0)
                        {
                            NativeArray<byte> data = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<byte>((byte*) ArrayOfInputDataArrays[i].ToPointer() + typeSize * index, typeSize, Allocator.None);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                            var safetyHandle = AtomicSafetyHandle.Create();
                            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref data, safetyHandle);
#endif
                            Ecb.SetComponent(index, entity, ComponentTypesToAdd[i], ComponentTypesSizes[i], data);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                            AtomicSafetyHandle.Release(safetyHandle);
#endif
                        }
                    }
                }
                Ecb.AddComponent<PersistenceState>(index, entity);
                Ecb.SetComponent(index, entity, persistenceState);
                Ecb.AddSharedComponent(index, entity, SceneSection);
                Ecb.AddSharedComponent(index, entity, PersistedTypes);
            }

            for (int i = 0; i < ExistingEntities.Length; i++)
            {
                Debug.Log($"Existing: {ExistingEntities[i].ArrayIndex}");
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

    public unsafe struct CopyByteArrayToBufferElements : IJobChunk
    {
        [NativeDisableContainerSafetyRestriction]
        public ArchetypeChunkBufferDataTypeDynamic ChunkBufferType;
        
        public int ElementSize;
        public int MaxElements;
        [ReadOnly] 
        public ArchetypeChunkComponentType<PersistenceState> PersistenceStateType;
        [ReadOnly, NativeDisableParallelForRestriction]
        public NativeArray<byte> InputData;
        [ReadOnly, NativeDisableParallelForRestriction]
        public NativeArray<int> AmountPersisted;

        public void Execute(ArchetypeChunk chunk, int chunkIndex, int firstEntityIndex)
        {
            var untypedBufferAccessor = chunk.GetUntypedBufferAccessor(ChunkBufferType);
            var persistenceStateArray = chunk.GetNativeArray(PersistenceStateType);
            for (int i = 0; i < persistenceStateArray.Length; i++)
            {
                PersistenceState persistenceState = persistenceStateArray[i];
                int inputByteIndex = persistenceState.ArrayIndex * ElementSize * MaxElements;
                
                int amountToCopy = AmountPersisted[persistenceState.ArrayIndex];
                untypedBufferAccessor.ResizeBufferUninitialized(i, amountToCopy);
                var untypedBuffer = untypedBufferAccessor[i];

                // when safety check bug is fixed move this back to .GetUnsafePtr
                UnsafeUtility.MemCpy(NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(untypedBuffer), (byte*)InputData.GetUnsafeReadOnlyPtr() + inputByteIndex, ElementSize * amountToCopy);
            }
        }
    }
}