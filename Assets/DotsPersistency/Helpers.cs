// Author: Jonas De Maeseneer

using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;

namespace DotsPersistency
{
    public static unsafe class EntitiesExtensions
    {
        public static byte* GetComponentDataAsBytePtr(this ArchetypeChunk archetypeChunk, ArchetypeChunkComponentTypeDynamic chunkComponentType
            , out int typeSize, out int byteLen)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (chunkComponentType.m_IsZeroSized)
                throw new ArgumentException($"ArchetypeChunk.GetComponentDataAsBytePtr cannot be called on zero-sized IComponentData");

            AtomicSafetyHandle.CheckReadAndThrow(chunkComponentType.m_Safety);
#endif
            var m_Chunk = archetypeChunk.m_Chunk;
            
            var archetype = m_Chunk->Archetype;
            ChunkDataUtility.GetIndexInTypeArray(m_Chunk->Archetype, chunkComponentType.m_TypeIndex, ref chunkComponentType.m_TypeLookupCache);
            var typeIndexInArchetype = chunkComponentType.m_TypeLookupCache;
            if (typeIndexInArchetype == -1)
            {
                byteLen = 0;
                typeSize = 0;
                return (byte*) 0;
            }

            typeSize = archetype->SizeOfs[typeIndexInArchetype];
            var length = m_Chunk->Count;
            byteLen = length * typeSize;
            
            var buffer = m_Chunk->Buffer;
            var startOffset = archetype->Offsets[typeIndexInArchetype];
            
            if (!chunkComponentType.IsReadOnly)
                m_Chunk->SetChangeVersion(typeIndexInArchetype, chunkComponentType.GlobalSystemVersion);
            return buffer + startOffset;
        }
        
    }
    
    public unsafe struct CopyComponentDataToByteArray : IJobChunk
    {
        [NativeDisableParallelForRestriction]
        [ReadOnly] public ArchetypeChunkComponentTypeDynamic ChunkComponentType;
        [ReadOnly] public ArchetypeChunkComponentType<PersistenceState> PersistenceStateType;
        [NativeDisableParallelForRestriction]
        public NativeArray<byte> Output;
            
        public void Execute(ArchetypeChunk chunk, int chunkIndex, int firstEntityIndex)
        {
            var ptr = chunk.GetComponentDataAsBytePtr(ChunkComponentType, out int typeSize, out int byteLen);
            var persistenceStateArray = chunk.GetNativeArray(PersistenceStateType);
            for (int i = 0; i < persistenceStateArray.Length; i++)
            {
                PersistenceState persistenceState = persistenceStateArray[i];
                int outputByteIndex = persistenceState.ArrayIndex * typeSize;
                int compDataByteIndex = i * typeSize;

                var value1 = UnsafeUtility.ReadArrayElement<Translation>(ptr, i).Value;
                UnsafeUtility.MemCpy((byte*)Output.GetUnsafePtr() + outputByteIndex, ptr + compDataByteIndex, typeSize);
                var value2 = UnsafeUtility.ReadArrayElement<Translation>((byte*)Output.GetUnsafeReadOnlyPtr(), persistenceState.ArrayIndex).Value;
                //Debug.Log(persistenceState.ArrayIndex + " - " + value1 + " - " + value2);
            }
        }
    }
    
    public unsafe struct CopyByteArrayToComponentData : IJobChunk
    {
        [NativeDisableContainerSafetyRestriction]
        public ArchetypeChunkComponentTypeDynamic ChunkComponentType;
        [ReadOnly] 
        public ArchetypeChunkComponentType<PersistenceState> PersistenceStateType;
        [ReadOnly]
        public NativeArray<byte> Input;
            
        public void Execute(ArchetypeChunk chunk, int chunkIndex, int firstEntityIndex)
        {
            var ptr = chunk.GetComponentDataAsBytePtr(ChunkComponentType, out int typeSize, out int byteLen);
            var persistenceStateArray = chunk.GetNativeArray(PersistenceStateType);
            
            for (int i = 0; i < persistenceStateArray.Length; i++)
            {
                PersistenceState persistenceState = persistenceStateArray[i];
                int inputByteIndex = persistenceState.ArrayIndex * typeSize;
                int compDataByteIndex = i * typeSize;

                var value1 = UnsafeUtility.ReadArrayElement<Translation>((byte*)Input.GetUnsafeReadOnlyPtr(), persistenceState.ArrayIndex).Value;
                UnsafeUtility.MemCpy(ptr + compDataByteIndex, (byte*)Input.GetUnsafeReadOnlyPtr() + inputByteIndex, typeSize);
                var value2 = UnsafeUtility.ReadArrayElement<Translation>(ptr, i).Value;
                Debug.Log(persistenceState.ArrayIndex + " - " + value1 + " - " + value2);
            }
        }
    }
}