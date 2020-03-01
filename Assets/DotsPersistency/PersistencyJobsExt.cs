// Author: Jonas De Maeseneer

using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using DotsPersistency.Containers;
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
    [BurstCompile]
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
        public NativeArray<bool>  EntityFoundArray; // whether the entity existed at time of persisting
        
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
            if (EntityFoundArray[index] && !ExistingEntities.Contains(persistenceState))
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
        }
    }
    
    [BurstCompile]
    public struct CopyComponentDataToByteArrays : IJobChunk
    {
        [ReadOnly, NativeDisableParallelForRestriction]
        public FixedArray16<ArchetypeChunkComponentTypeDynamic> ChunkComponentTypeArray;
        public FixedListInt64 TypeSizeArray;
        [ReadOnly] 
        public ArchetypeChunkComponentType<PersistenceState> PersistenceStateType;
        [WriteOnly, NativeDisableParallelForRestriction]
        public FixedArray16<NativeArray<byte>> OutputDataArrays;
        [WriteOnly, NativeDisableParallelForRestriction]
        public FixedArray16<NativeArray<bool>> OutputFoundArrays;
            
        public void Execute(ArchetypeChunk chunk, int chunkIndex, int firstEntityIndex)
        {
            for (int i = 0; i < TypeSizeArray.Length; i++)
            {
                new CopyComponentDataToByteArray()
                {
                    ChunkComponentType = ChunkComponentTypeArray[i],
                    TypeSize = TypeSizeArray[i],
                    PersistenceStateType = PersistenceStateType,
                    OutputData = OutputDataArrays[i],
                    OutputFound = OutputFoundArrays[i]
                }.Execute(chunk, chunkIndex, firstEntityIndex);
            }
        }
    }
    
    // [BurstCompile] todo make burstCompilable
    public struct RemoveComponents : IJobForEachWithEntity<PersistenceState>
    {        
        public EntityCommandBuffer.Concurrent Ecb;
        public ComponentTypes ComponentTypes; // todo replace with list/array of componentType
        [ReadOnly, NativeDisableParallelForRestriction]
        public FixedArray16<NativeArray<bool>> InputFound;
        
        public void Execute(Entity entity, int index, [ReadOnly] ref PersistenceState persistenceState)
        {
            for (int i = 0; i < ComponentTypes.Length; i++)
            {
                new RemoveComponent()
                {
                    Ecb = Ecb,
                    ComponentType = ComponentTypes.GetComponentType(i),
                    InputFound = InputFound[i]
                }.Execute(entity, index, ref persistenceState);
            }
        }
    }
    
}