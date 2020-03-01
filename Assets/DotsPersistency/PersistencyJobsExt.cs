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
    
    [BurstCompile]
    public struct RemoveComponents : IJobForEachWithEntity<PersistenceState>
    {        
        public EntityCommandBuffer.Concurrent Ecb;
        public ComponentTypes ComponentTypes;
        [ReadOnly, NativeDisableParallelForRestriction]
        public FixedArray16<NativeArray<bool>> InputFound;
        
        public void Execute(Entity entity, int index, [ReadOnly] ref PersistenceState persistenceState)
        {
            for (int i = 0; i < ComponentTypes.Length; i++)
            {
                Debug.Assert(!ComponentTypes.GetComponentType(i).IsBuffer); // removing buffers not supported
                
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