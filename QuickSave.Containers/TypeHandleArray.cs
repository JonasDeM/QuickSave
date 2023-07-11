﻿// Author: Jonas De Maeseneer

using System;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;

[assembly:InternalsVisibleTo("com.studioaurelius.quicksave")]

// This container is intentionally super specific.
// It should only be used to get around the fact that NativeContainer doesn't want to hold DynamicTypeHandles.

namespace QuickSave.Containers
{
    [NativeContainer]
    internal struct TypeHandleArrayDisposeJobData
    {
        [NativeDisableUnsafePtrRestriction]
        public unsafe void* m_Buffer;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        public AtomicSafetyHandle m_Safety;
#endif
        public Allocator m_AllocatorLabel;

        public unsafe void Dispose()
        {
            UnsafeUtility.Free(m_Buffer, m_AllocatorLabel);
        }
    }
        
    internal struct TypeHandleArrayDisposeJob : IJob
    {
        public TypeHandleArrayDisposeJobData DisposeJobData;

        public void Execute()
        {
            DisposeJobData.Dispose();
        }
    }
    
    [NativeContainer]
    [NativeContainerSupportsDeallocateOnJobCompletion]
    internal struct ComponentTypeHandleArray : IDisposable
    {
        [NativeDisableUnsafePtrRestriction] private unsafe void* m_Buffer;
        private int m_Length;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        private AtomicSafetyHandle m_Safety;
        [NativeSetClassTypeToNullOnSchedule]
        private DisposeSentinel m_DisposeSentinel;
#endif
        private Allocator m_AllocatorLabel;

        private static readonly int ElementSize = UnsafeUtility.SizeOf<DynamicComponentTypeHandle>();
        private static readonly int Alignment = UnsafeUtility.AlignOf<DynamicComponentTypeHandle>();

        public unsafe ComponentTypeHandleArray(int length, Allocator allocator)
        {
            long size = ElementSize * (long) length;
        
            if (allocator <= Allocator.None)
                throw new ArgumentException("Allocator must be Temp, TempJob or Persistent", nameof (allocator));
            if (length < 0)
                throw new ArgumentOutOfRangeException(nameof (length), "Length must be >= 0");
            if (size > (long) int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof (length), $"Length * sizeof(DynamicComponentTypeHandle) cannot exceed {int.MaxValue.ToString()} bytes");
        
            m_Buffer = UnsafeUtility.Malloc(size, Alignment, allocator);
            m_Length = length;
            m_AllocatorLabel = allocator;
            
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            DisposeSentinel.Create(out m_Safety, out m_DisposeSentinel, 1, allocator);
#endif
        }
    
        public unsafe void Dispose()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (!UnsafeUtility.IsValidAllocator(m_AllocatorLabel))
                throw new InvalidOperationException("The ComponentTypeHandleArray can not be disposed because it was not allocated with a valid allocator.");
            DisposeSentinel.Dispose(ref this.m_Safety, ref this.m_DisposeSentinel);
#endif
            UnsafeUtility.Free(m_Buffer, m_AllocatorLabel);
            m_Buffer = (void*) null;
            m_Length = 0;
        }
        
        public unsafe JobHandle Dispose(JobHandle inputDeps)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            DisposeSentinel.Clear(ref m_DisposeSentinel);
#endif
            JobHandle jobHandle = new TypeHandleArrayDisposeJob()
            {
                DisposeJobData = new TypeHandleArrayDisposeJobData()
                {
                    m_Buffer = m_Buffer,
                    m_AllocatorLabel = m_AllocatorLabel,
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                    m_Safety = m_Safety
#endif
                }
            }.Schedule(inputDeps);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.Release(m_Safety);
#endif
            m_Buffer = (void*) null;
            m_Length = 0;
            return jobHandle;
        }
        
        public unsafe DynamicComponentTypeHandle this[int index]
        {
            set
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                if (index < 0 || index >= m_Length)
                    throw new IndexOutOfRangeException();
#endif
                UnsafeUtility.WriteArrayElement(m_Buffer, index, value);
            }
        }

        public unsafe bool GetByTypeIndex(TypeIndex typeIndex, out DynamicComponentTypeHandle typeHandle)
        {
            for (int i = 0; i < m_Length; i++)
            {
                DynamicComponentTypeHandle element = UnsafeUtility.ReadArrayElement<DynamicComponentTypeHandle>(m_Buffer, i);
                if (element.GetTypeIndex() == typeIndex)
                {
                    typeHandle = element;
                    return true;
                }
            }
            typeHandle = default;
            return false;
        }
    }
}