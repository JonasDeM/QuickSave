using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Unity.Burst.CompilerServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;

namespace QuickSave
{
    public static class UntypedAccessExtensionMethods
    {
        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        public static void DisableSafetyChecks(ref DynamicComponentTypeHandle chunkComponentTypeHandle)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            chunkComponentTypeHandle.m_Safety0 = AtomicSafetyHandle.Create();
            chunkComponentTypeHandle.m_Safety1 = AtomicSafetyHandle.Create();
#endif
        }
        
        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private static void CheckZeroSizedComponentData(DynamicComponentTypeHandle chunkComponentTypeHandle)
        {
            if (Hint.Unlikely(chunkComponentTypeHandle.IsZeroSized))
                throw new ArgumentException($"ArchetypeChunk.GetComponentDataAsByteArray cannot be called on zero-sized IComponentData");
        }

        // based on ArchetypeChunk::GetEnabledMask<T>
        public static unsafe EnabledMask GetEnabledMask(this in ArchetypeChunk archetypeChunk, ref DynamicComponentTypeHandle chunkComponentTypeHandle)
        {
            var m_Chunk = archetypeChunk.m_Chunk;
            ChunkDataUtility.GetIndexInTypeArray(archetypeChunk.Archetype.Archetype, chunkComponentTypeHandle.m_TypeIndex, ref chunkComponentTypeHandle.m_TypeLookupCache);
            var indexInArchetypeTypeArray = chunkComponentTypeHandle.m_TypeLookupCache;
            
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckReadAndThrow(chunkComponentTypeHandle.m_Safety0);
#endif
            // if (Hint.Unlikely(chunkComponentTypeHandle.m_LookupCache.Archetype != m_Chunk->Archetype))
            // {
            //     chunkComponentTypeHandle.m_LookupCache.Update(m_Chunk->Archetype, chunkComponentTypeHandle.m_TypeIndex);
            // }
            
            // In case the chunk does not contains the component type (or the internal TypeIndex lookup fails to find a
            // match), the LookupCache.Update will invalidate the IndexInArchetype.
            // In such a case, we return an empty EnabledMask.
            if (Hint.Unlikely(indexInArchetypeTypeArray == -1))
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                return new EnabledMask(new SafeBitRef(null, 0, chunkComponentTypeHandle.m_Safety0), null);
#else
                return new EnabledMask(SafeBitRef.Null, null);
#endif
            }
            int* ptrChunkDisabledCount = default;
            var ptr = (chunkComponentTypeHandle.IsReadOnly)
                ? ChunkDataUtility.GetEnabledRefRO(m_Chunk, indexInArchetypeTypeArray).Ptr
                : ChunkDataUtility.GetEnabledRefRW(m_Chunk, indexInArchetypeTypeArray,
                    chunkComponentTypeHandle.GlobalSystemVersion, out ptrChunkDisabledCount).Ptr;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            var result = new EnabledMask(new SafeBitRef(ptr, 0, chunkComponentTypeHandle.m_Safety0), ptrChunkDisabledCount);
#else
            var result = new EnabledMask(new SafeBitRef(ptr, 0), ptrChunkDisabledCount);
#endif
            return result;
        }
        
        // based on ArchetypeChunk::GetDynamicComponentDataArrayReinterpret
        public static unsafe NativeArray<byte> GetComponentDataAsByteArray(this in ArchetypeChunk archetypeChunk, ref DynamicComponentTypeHandle chunkComponentType)
        {
            CheckZeroSizedComponentData(chunkComponentType);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckReadAndThrow(chunkComponentType.m_Safety0);
#endif
            var chunk = archetypeChunk.m_Chunk;
            var archetype = chunk->Archetype;
            ChunkDataUtility.GetIndexInTypeArray(chunk->Archetype, chunkComponentType.m_TypeIndex, ref chunkComponentType.m_TypeLookupCache);
            var typeIndexInArchetype = chunkComponentType.m_TypeLookupCache;
            if (typeIndexInArchetype == -1)
            {
                var emptyResult = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<byte>(null, 0, 0);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref emptyResult, chunkComponentType.m_Safety0);
#endif
                return emptyResult;
            }
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (archetype->Types[typeIndexInArchetype].IsBuffer)
                throw new ArgumentException($"ArchetypeChunk.GetComponentDataAsByteArray cannot be called for IBufferElementData {TypeManager.GetType(chunkComponentType.m_TypeIndex)}");
#endif
            
            var typeSize = archetype->SizeOfs[typeIndexInArchetype];
            var length = archetypeChunk.Count;
            var byteLen = length * typeSize;
            // var outTypeSize = 1;
            // var outLength = byteLen / outTypeSize;
            
            byte* ptr = (chunkComponentType.IsReadOnly)
                ? ChunkDataUtility.GetComponentDataRO(chunk, 0, typeIndexInArchetype)
                : ChunkDataUtility.GetComponentDataRW(chunk, 0, typeIndexInArchetype, chunkComponentType.GlobalSystemVersion);
            
            var result = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<byte>(ptr, byteLen, Allocator.None);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref result, chunkComponentType.m_Safety0);
#endif
            
#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !DISABLE_ENTITIES_JOURNALING
            if (Hint.Unlikely(archetypeChunk.m_EntityComponentStore->m_RecordToJournal != 0) && !chunkComponentType.IsReadOnly)
                JournalAddRecordGetComponentDataRW(archetypeChunk, ref chunkComponentType, ptr, byteLen);
#endif
            return result;
        }
        
#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !DISABLE_ENTITIES_JOURNALING
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static unsafe void JournalAddRecord(in ArchetypeChunk thisArchetypeChunk, EntitiesJournaling.RecordType recordType, TypeIndex typeIndex, uint globalSystemVersion, void* data = null, int dataLength = 0)
        {
            fixed (ArchetypeChunk* archetypeChunk = &thisArchetypeChunk)
            {
                EntitiesJournaling.AddRecord(
                    recordType: recordType,
                    entityComponentStore: archetypeChunk->m_EntityComponentStore,
                    globalSystemVersion: globalSystemVersion,
                    chunks: archetypeChunk,
                    chunkCount: 1,
                    types: &typeIndex,
                    typeCount: 1,
                    data: data,
                    dataLength: dataLength);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static unsafe void JournalAddRecordGetComponentDataRW(in ArchetypeChunk thisArchetypeChunk, ref DynamicComponentTypeHandle typeHandle, void* data, int dataLength) =>
            JournalAddRecord(in thisArchetypeChunk, EntitiesJournaling.RecordType.GetComponentDataRW, typeHandle.m_TypeIndex, typeHandle.m_GlobalSystemVersion, data, dataLength);
#endif
        
        public static TypeIndex GetTypeIndex(this ref DynamicComponentTypeHandle componentTypeHandle)
        {
            return componentTypeHandle.m_TypeIndex;
        }
        
        public static unsafe void AddComponent(ref this EntityCommandBuffer.ParallelWriter ecb, int jobIndex, Entity e, ComponentType cType, int typeSize, NativeArray<byte> byteArray)
        {
            ecb.UnsafeAddComponent(jobIndex, e, cType.TypeIndex, typeSize, byteArray.GetUnsafeReadOnlyPtr());
        }
        
        public static ref UnsafeList<EntityCommandBuffer> GetPendingBuffersRef(this EntityCommandBufferSystem ecbSystem)
        {
            return ref ecbSystem.PendingBuffers;
        }
    }
}