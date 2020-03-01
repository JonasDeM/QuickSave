using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Unity.Entities
{
    //[NativeContainer]
    public unsafe struct UntypedBufferAccessor
    {
        [NativeDisableUnsafePtrRestriction]
        private byte* m_BasePointer;
        private int m_Length;
        private int m_Stride;
        private int m_InternalCapacity;
        private int m_ElementSize;
        private int m_Alignment;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        private bool m_IsReadOnly;
#endif

        public int Length => m_Length;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        private AtomicSafetyHandle m_Safety0;
        private AtomicSafetyHandle m_ArrayInvalidationSafety;

#pragma warning disable 0414 // assigned but its value is never used
        private int m_SafetyReadOnlyCount;
        private int m_SafetyReadWriteCount;
#pragma warning restore 0414

#endif

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        /// <summary>
        ///
        /// </summary>
        /// <param name="basePointer"></param>
        /// <param name="length"></param>
        /// <param name="stride"></param>
        /// <param name="readOnly"></param>
        /// <param name="safety"></param>
        /// <param name="arrayInvalidationSafety"></param>
        /// <param name="internalCapacity"></param>
        public UntypedBufferAccessor(byte* basePointer, int length, int stride, bool readOnly, int elementSize, int alignment, AtomicSafetyHandle safety, AtomicSafetyHandle arrayInvalidationSafety, int internalCapacity)
        {
            m_BasePointer = basePointer;
            m_Length = length * elementSize;
            m_Stride = stride;
            m_Safety0 = safety;
            m_ArrayInvalidationSafety = arrayInvalidationSafety;
            m_IsReadOnly = readOnly;
            m_ElementSize = elementSize;
            m_SafetyReadOnlyCount = m_IsReadOnly ? 2 : 0;
            m_SafetyReadWriteCount = m_IsReadOnly ? 0 : 2;
            m_InternalCapacity = internalCapacity;
            m_Alignment = alignment;
        }
#else
        public UntypedBufferAccessor(byte* basePointer, int length, int stride, int elementSize, int alignment, int internalCapacity)
        {
            m_BasePointer = basePointer;
            m_Length = length * elementSize;
            m_Stride = stride;
            m_ElementSize = elementSize;
            m_InternalCapacity = internalCapacity;
            m_Alignment = alignment;
        }
#endif

        /// <summary>
        ///
        /// </summary>
        /// <param name="index"></param>
        /// <exception cref="InvalidOperationException"></exception>
        public NativeArray<byte> this[int index]
        {
            get
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckReadAndThrow(m_Safety0);

                if (index < 0 || index >= Length)
                    throw new InvalidOperationException($"index {index} out of range in LowLevelBufferAccessor of length {Length}");
#endif
                BufferHeader* hdr = (BufferHeader*) (m_BasePointer + index * m_Stride);

                var shadow = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<byte>(BufferHeader.GetElementPointer(hdr), hdr->Length * m_ElementSize, Allocator.Invalid);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                var handle = m_ArrayInvalidationSafety;
                AtomicSafetyHandle.UseSecondaryVersion(ref handle);
                NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref shadow, handle);
#endif
                return shadow;
            }
        }

        // length should be the real length, not the byte length
        public void ResizeBufferUninitialized(int index, int length)
        {
            BufferHeader* hdr = (BufferHeader*) (m_BasePointer + index * m_Stride);
            BufferHeader.EnsureCapacity(hdr, length, m_ElementSize, m_Alignment, BufferHeader.TrashMode.RetainOldData, false, 0);
            hdr->Length = length;
        }
    }
    
    [NativeContainer]
    public struct ArchetypeChunkBufferDataTypeDynamic
    {
        internal readonly int m_TypeIndex;
        internal readonly uint m_GlobalSystemVersion;
        internal readonly bool m_IsReadOnly;
        
#pragma warning disable 0414
        private readonly int m_Length;
#pragma warning restore 0414
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        internal readonly AtomicSafetyHandle m_Safety;
        internal readonly AtomicSafetyHandle m_Safety2;
#endif
        public uint GlobalSystemVersion => m_GlobalSystemVersion;
        public bool IsReadOnly => m_IsReadOnly;
        
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        internal ArchetypeChunkBufferDataTypeDynamic(AtomicSafetyHandle safety, AtomicSafetyHandle arrayInvalidationSafety, ComponentType componentType, uint globalSystemVersion)
#else
        internal MyCustomContainer(ComponentType componentType, uint globalSystemVersion)
#endif
        {
            m_Length = 1;
            m_TypeIndex = componentType.TypeIndex;
            m_GlobalSystemVersion = globalSystemVersion;
            m_IsReadOnly = componentType.AccessModeType == ComponentType.AccessMode.ReadOnly;
            
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            m_Safety = safety;
            m_Safety2 = arrayInvalidationSafety;
#endif
        }
        
    }
    
    public static class UntypedAccessExtensionMethods
    {
        public static unsafe NativeArray<byte> GetComponentDataAsByteArray(this ref ArchetypeChunk archetypeChunk, ArchetypeChunkComponentTypeDynamic chunkComponentType)
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
        
        public static unsafe ArchetypeChunkBufferDataTypeDynamic GetArchetypeChunkBufferTypeDynamic(this EntityManager entityManager, ComponentType componentType)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            var typeIndex = componentType.TypeIndex;
            return new ArchetypeChunkBufferDataTypeDynamic(
                entityManager.SafetyHandles->GetSafetyHandle(typeIndex, componentType.AccessModeType == ComponentType.AccessMode.ReadOnly),
                entityManager.SafetyHandles->GetBufferSafetyHandle(typeIndex),
                componentType, entityManager.GlobalSystemVersion);
#else
            return new ArchetypeChunkBufferTypeDynamic(componentType, GlobalSystemVersion);
#endif
        }

        public static ArchetypeChunkBufferDataTypeDynamic GetArchetypeChunkBufferTypeDynamic(this ComponentSystemBase system, ComponentType componentType)
        {
            system.AddReaderWriter(componentType);
            return system.EntityManager.GetArchetypeChunkBufferTypeDynamic(componentType);
        }
        
        public static unsafe UntypedBufferAccessor GetUntypedBufferAccessor(this ref ArchetypeChunk chunk, ArchetypeChunkBufferDataTypeDynamic bufferComponentType)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckReadAndThrow(bufferComponentType.m_Safety);
#endif
            var archetype = chunk.m_Chunk->Archetype;
            var typeIndex = bufferComponentType.m_TypeIndex;
            var typeIndexInArchetype = ChunkDataUtility.GetIndexInTypeArray(archetype, typeIndex);
            if (typeIndexInArchetype == -1)
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                return new UntypedBufferAccessor(null, 0, 0, bufferComponentType.IsReadOnly, 0, 0, bufferComponentType.m_Safety, bufferComponentType.m_Safety2, 0);
#else
                return new UntypedBufferAccessor(null, 0, 0, 0, 0, 0);
#endif
            }

            int internalCapacity = archetype->BufferCapacities[typeIndexInArchetype];

            if (!bufferComponentType.IsReadOnly)
                chunk.m_Chunk->SetChangeVersion(typeIndexInArchetype, bufferComponentType.GlobalSystemVersion);

            var buffer = chunk.m_Chunk->Buffer;
            var length = chunk.m_Chunk->Count;
            var startOffset = archetype->Offsets[typeIndexInArchetype];
            int stride = archetype->SizeOfs[typeIndexInArchetype];
            var typeInfo = TypeManager.GetTypeInfo(bufferComponentType.m_TypeIndex);
            int elementSize = typeInfo.ElementSize;
            int alignment = typeInfo.AlignmentInChunkInBytes;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            return new UntypedBufferAccessor(buffer + startOffset, length, stride, bufferComponentType.IsReadOnly, elementSize, alignment, bufferComponentType.m_Safety, bufferComponentType.m_Safety2, internalCapacity);
#else
            return new UntypedBufferAccessor(buffer + startOffset, length, stride, elementSize, alignment, internalCapacity);
#endif
        }
    }
}
