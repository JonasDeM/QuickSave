// Author: Jonas De Maeseneer

using System;
using System.Diagnostics;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Entities.LowLevel.Unsafe;
using Unity.Mathematics;

namespace QuickSave
{
    [BurstCompile]
    internal unsafe struct CopyComponentDataToByteArray : IJobChunk
    {
        [ReadOnly, NativeDisableParallelForRestriction]
        public DynamicComponentTypeHandle ComponentTypeHandle;
        public int TypeSize;
        [ReadOnly] 
        public ComponentTypeHandle<LocalIndexInContainer> LocalIndexInContainerType;

        [WriteOnly, NativeDisableContainerSafetyRestriction]
        public BufferLookup<QuickSaveDataContainer.Data> ByteArrayLookup;
        public Entity ContainerEntity;
        public int SubArrayOffset;
        public int SubArrayByteSize;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            var persistenceStateArray = chunk.GetNativeArray(ref LocalIndexInContainerType);

            SafetyChecks.CheckEntityHasDataBuffer(ContainerEntity, ByteArrayLookup);
            var outputDataSubArray = ByteArrayLookup[ContainerEntity].Reinterpret<byte>().AsNativeArray().GetSubArray(SubArrayOffset, SubArrayByteSize);
            
            if (chunk.Has(ref ComponentTypeHandle))
            {
                EnabledMask enabledMask = default;
                bool checkEnabled = ComponentTypeHandle.GetTypeIndex().IsEnableable;
                if (checkEnabled)
                {
                    enabledMask = chunk.GetEnabledMask(ref ComponentTypeHandle);
                }
                
                if (TypeSize > 0)
                {
                    var byteArray = chunk.GetComponentDataAsByteArray(ref ComponentTypeHandle);
                    // This execute method also updates meta data
                    Execute(outputDataSubArray, TypeSize, byteArray, persistenceStateArray, checkEnabled, enabledMask);
                }
                else
                {
                    UpdateMetaDataForComponent.Execute(outputDataSubArray, TypeSize + QuickSaveMetaData.SizeOfStruct, persistenceStateArray, 1, checkEnabled, enabledMask);
                }
            }
            else
            {
                UpdateMetaDataForComponent.Execute(outputDataSubArray, TypeSize + QuickSaveMetaData.SizeOfStruct, persistenceStateArray, 0, false, default);
            }
        }

        public static void Execute(NativeArray<byte> outputData, int typeSize, NativeArray<byte> componentByteArray, 
            NativeArray<LocalIndexInContainer> persistenceStateArray, bool checkEnabled, EnabledMask enabledMask)
        {
            const int amountFound = 1;
            int totalElementSize = typeSize + QuickSaveMetaData.SizeOfStruct;
            
            for (int i = 0; i < persistenceStateArray.Length; i++)
            {
                LocalIndexInContainer persistenceState = persistenceStateArray[i];

                SafetyChecks.CheckOutOfRangeAccess(persistenceState, totalElementSize, outputData);
                
                QuickSaveMetaData* outputMetaDataBytePtr = (QuickSaveMetaData*)((byte*)outputData.GetUnsafePtr() + (persistenceState.LocalIndex * totalElementSize));
                void* outputDataBytePtr = outputMetaDataBytePtr + 1;
                void* compDataBytePtr = (byte*)componentByteArray.GetUnsafeReadOnlyPtr() + (i * typeSize);
                
                // Diff
                int diff = outputMetaDataBytePtr->AmountFound - amountFound;
                diff |= UnsafeUtility.MemCmp(outputDataBytePtr, compDataBytePtr, typeSize);
                
                // Enabled
                bool enabled = true;
                if (checkEnabled)
                {
                    enabled = enabledMask.GetBit(i);
                }

                // Write Meta Data
                *outputMetaDataBytePtr = new QuickSaveMetaData(diff, amountFound, enabled); // 1 branch in this constructor
                
                // Write Data
                UnsafeUtility.MemCpy(outputDataBytePtr, compDataBytePtr, typeSize);
            }
        }
    }

    [BurstCompile]
    internal unsafe struct CopyByteArrayToComponentData : IJobChunk
    {
        [WriteOnly, NativeDisableContainerSafetyRestriction]
        public DynamicComponentTypeHandle ComponentTypeHandle;
        public ComponentType ComponentType;
        public int TypeSize;
        [ReadOnly] 
        public ComponentTypeHandle<LocalIndexInContainer> LocalIndexInContainerType;
        public EntityCommandBuffer.ParallelWriter Ecb;
        [ReadOnly]
        public EntityTypeHandle EntityType;
        
        [ReadOnly]
        public BufferLookup<QuickSaveDataContainer.Data> ByteArrayLookup;
        public Entity ContainerEntity;
        public int SubArrayOffset;
        public int SubArrayByteSize;
            
        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            var persistenceStateArray = chunk.GetNativeArray(ref LocalIndexInContainerType);
            var entityArray = chunk.GetNativeArray(EntityType);
            
            SafetyChecks.CheckEntityHasDataBuffer(ContainerEntity, ByteArrayLookup);
            var inputDataSubArray = ByteArrayLookup[ContainerEntity].Reinterpret<byte>().AsNativeArray().GetSubArray(SubArrayOffset, SubArrayByteSize);

            if (chunk.Has(ref ComponentTypeHandle))
            {
                if (TypeSize > 0)
                {
                    var byteArray = chunk.GetComponentDataAsByteArray(ref ComponentTypeHandle);
                    Execute(inputDataSubArray, TypeSize, byteArray, persistenceStateArray);
                }

                EnabledMask enabledMask = ComponentType.IsEnableable ? chunk.GetEnabledMask(ref ComponentTypeHandle) : default;
                RemoveEnableOrDisableExistingComponent.Execute(inputDataSubArray, ComponentType, TypeSize, entityArray, persistenceStateArray, enabledMask, Ecb, unfilteredChunkIndex);
            }
            else
            {
                AddMissingComponent.Execute(inputDataSubArray, ComponentType, TypeSize, entityArray, persistenceStateArray, Ecb, unfilteredChunkIndex);
            }
        }

        public static void Execute(NativeArray<byte> inputData, int typeSize, NativeArray<byte> componentByteArray, NativeArray<LocalIndexInContainer> persistenceStateArray)
        {
            int totalElementSize = typeSize + QuickSaveMetaData.SizeOfStruct;
            
            for (int i = 0; i < persistenceStateArray.Length; i++)
            {
                LocalIndexInContainer persistenceState = persistenceStateArray[i];
                SafetyChecks.CheckOutOfRangeAccess(persistenceState, totalElementSize, inputData);
                
                byte* inputDataPtr = (byte*)inputData.GetUnsafeReadOnlyPtr() + (persistenceState.LocalIndex * totalElementSize + QuickSaveMetaData.SizeOfStruct);
                byte* compDataBytePtr =(byte*)componentByteArray.GetUnsafePtr() + (i * typeSize);
                
                // Write Data
                UnsafeUtility.MemCpy(compDataBytePtr, inputDataPtr, typeSize);
            }
        }
    }
    
    [BurstCompile]
    internal unsafe struct CopyBufferElementsToByteArray : IJobChunk
    {
        [NativeDisableContainerSafetyRestriction, ReadOnly]
        public DynamicComponentTypeHandle BufferTypeHandle;

        public int MaxElements;
        [ReadOnly] 
        public ComponentTypeHandle<LocalIndexInContainer> LocalIndexInContainerType;
        
        [WriteOnly, NativeDisableContainerSafetyRestriction]
        public BufferLookup<QuickSaveDataContainer.Data> ByteArrayLookup;
        public Entity ContainerEntity;
        public int SubArrayOffset;
        public int SubArrayByteSize;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            var persistenceStateArray = chunk.GetNativeArray(ref LocalIndexInContainerType);
            
            SafetyChecks.CheckEntityHasDataBuffer(ContainerEntity, ByteArrayLookup);
            var outputDataSubArray = ByteArrayLookup[ContainerEntity].Reinterpret<byte>().AsNativeArray().GetSubArray(SubArrayOffset, SubArrayByteSize);
            
            SafetyChecks.CheckHasBufferComponent(chunk, ref BufferTypeHandle); // Removing/Adding buffer data is not supported
            
            bool checkEnabled = BufferTypeHandle.GetTypeIndex().IsEnableable;
            EnabledMask enabledMask = default;
            if (checkEnabled)
            {
                enabledMask = chunk.GetEnabledMask(ref BufferTypeHandle);
            }
            var untypedBufferAccessor = chunk.GetUntypedBufferAccessor(ref BufferTypeHandle);
            // This execute method also updates meta data
            Execute(outputDataSubArray, MaxElements, untypedBufferAccessor, persistenceStateArray, checkEnabled, enabledMask);
        }

        public static void Execute(NativeArray<byte> outputData, int maxElements, UnsafeUntypedBufferAccessor untypedBufferAccessor,
            NativeArray<LocalIndexInContainer> persistenceStateArray, bool checkEnabled, EnabledMask enabledMask)
        {
            int elementSize = untypedBufferAccessor.ElementSize;
            int sizePerEntity = elementSize * maxElements + QuickSaveMetaData.SizeOfStruct;
            
            for (int i = 0; i < persistenceStateArray.Length; i++)
            {
                LocalIndexInContainer persistenceState = persistenceStateArray[i];
                SafetyChecks.CheckOutOfRangeAccess(persistenceState, sizePerEntity, outputData);
                
                void* bufferDataPtr = untypedBufferAccessor.GetUnsafeReadOnlyPtrAndLength(i, out int length);; 
                
                QuickSaveMetaData* outputMetaBytePtr = (QuickSaveMetaData*)((byte*) outputData.GetUnsafePtr() + persistenceState.LocalIndex * sizePerEntity);
                void* outputDataBytePtr = outputMetaBytePtr + 1;
                int amountToCopy = math.clamp(length, 0, maxElements);
                
                // Diff
                int diff = outputMetaBytePtr->AmountFound - amountToCopy;
                diff |= UnsafeUtility.MemCmp(outputDataBytePtr, bufferDataPtr, amountToCopy * elementSize);
                
                // Enabled
                bool enabled = true;
                if (checkEnabled)
                {
                    enabled = enabledMask.GetBit(i);
                }
                
                // Write Meta Data
                *outputMetaBytePtr = new QuickSaveMetaData(diff, (ushort)amountToCopy, enabled); // 1 branch in this constructor

                // Write Data
                UnsafeUtility.MemCpy(outputDataBytePtr, bufferDataPtr, elementSize * amountToCopy);
            }
        }
    }
    
    [BurstCompile]
    internal unsafe struct CopyByteArrayToBufferElements : IJobChunk
    {
        [WriteOnly, NativeDisableContainerSafetyRestriction]
        public DynamicComponentTypeHandle BufferTypeHandle;

        public int MaxElements;
        [ReadOnly] 
        public ComponentTypeHandle<LocalIndexInContainer> LocalIndexInContainerType;
        
        [ReadOnly]
        public BufferLookup<QuickSaveDataContainer.Data> ByteArrayLookup;
        public Entity ContainerEntity;
        public int SubArrayOffset;
        public int SubArrayByteSize;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            SafetyChecks.CheckHasBufferComponent(chunk, ref BufferTypeHandle); // Removing/Adding buffer data is not supported
            
            var untypedBufferAccessor = chunk.GetUntypedBufferAccessor(ref BufferTypeHandle);
            var persistenceStateArray = chunk.GetNativeArray(ref LocalIndexInContainerType);
            
            SafetyChecks.CheckEntityHasDataBuffer(ContainerEntity, ByteArrayLookup);
            var inputDataSubArray = ByteArrayLookup[ContainerEntity].Reinterpret<byte>().AsNativeArray().GetSubArray(SubArrayOffset, SubArrayByteSize);

            bool enableable = BufferTypeHandle.GetTypeIndex().IsEnableable;
            EnabledMask enabledMask = enableable ? chunk.GetEnabledMask(ref BufferTypeHandle) : default;
            
            Execute(inputDataSubArray, MaxElements, untypedBufferAccessor, persistenceStateArray, enabledMask, enableable);
        }
        
        public static void Execute(NativeArray<byte> inputData, int maxElements, UnsafeUntypedBufferAccessor untypedBufferAccessor,
            NativeArray<LocalIndexInContainer> persistenceStateArray, EnabledMask enabledMask, bool enableable)
        {
            SafetyChecks.CheckBufferSize(maxElements);
            int elementSize = untypedBufferAccessor.ElementSize;
            int sizePerEntity = elementSize * maxElements + QuickSaveMetaData.SizeOfStruct;
            
            for (int i = 0; i < persistenceStateArray.Length; i++)
            {
                LocalIndexInContainer persistenceState = persistenceStateArray[i];
                SafetyChecks.CheckOutOfRangeAccess(persistenceState, sizePerEntity, inputData);
                
                QuickSaveMetaData* inputMetaDataPtr = (QuickSaveMetaData*)((byte*) inputData.GetUnsafeReadOnlyPtr() + persistenceState.LocalIndex * sizePerEntity);
                void* inputDataBytePtr = inputMetaDataPtr + 1; // + 1 because it's a QuickSaveMetaData pointer
                
                // Enable/Disable
                if (enableable)
                    enabledMask[i] = inputMetaDataPtr->Enabled;

                // Resize
                int amountToCopy = inputMetaDataPtr->AmountFound;
                untypedBufferAccessor.ResizeUninitialized(i, amountToCopy);
                
                // Get (Possibly modified because of resize) ptr to buffer data
                void* bufferDataPtr = untypedBufferAccessor.GetUnsafePtrAndLength(i, out int length);
                SafetyChecks.CheckBufferResizedCorrectly(length, amountToCopy);
                
                // Write Data
                UnsafeUtility.MemCpy(bufferDataPtr, inputDataBytePtr, elementSize * amountToCopy);
            }
        }
    }
    
    [BurstCompile]
    internal unsafe struct UpdateMetaDataForComponent
    {
        public static void Execute(NativeArray<byte> outputData, int stride, NativeArray<LocalIndexInContainer> localIndices, int amountFound,
            bool checkEnabled, EnabledMask enabledMask)
        {
            SafetyChecks.CheckCanGrabEnabled(checkEnabled, amountFound);
            
            for (int i = 0; i < localIndices.Length; i++)
            {
                LocalIndexInContainer localIndexInContainer = localIndices[i];
                SafetyChecks.CheckOutOfRangeAccess(localIndexInContainer, stride, outputData);
                
                var metaData = UnsafeUtility.ReadArrayElementWithStride<QuickSaveMetaData>(outputData.GetUnsafeReadOnlyPtr(), localIndexInContainer.LocalIndex, stride);
                
                // Diff
                int diff = metaData.AmountFound - amountFound;
                
                // Enabled
                bool enabled = true;
                if (checkEnabled)
                {
                    enabled = enabledMask.GetBit(i);
                }
                
                // Write Meta Data
                // 1 branch in QuickSaveMetaData constructor
                UnsafeUtility.WriteArrayElementWithStride(outputData.GetUnsafePtr(), localIndexInContainer.LocalIndex, stride, new QuickSaveMetaData(diff, (ushort)amountFound, enabled));
            }
        }
    }

    [BurstCompile]
    internal unsafe struct RemoveEnableOrDisableExistingComponent
    {
        public static void Execute(NativeArray<byte> inputData, ComponentType typeToRemove, int typeSize, NativeArray<Entity> entityArray,
            NativeArray<LocalIndexInContainer> localIndices, EnabledMask enabledMask, EntityCommandBuffer.ParallelWriter ecb, int unfilteredChunkIndex)
        {
            for (int i = 0; i < entityArray.Length; i++)
            {
                var localIndexInContainer = localIndices[i];
                int stride = typeSize + QuickSaveMetaData.SizeOfStruct;
                SafetyChecks.CheckOutOfRangeAccess(localIndexInContainer, stride, inputData);

                var metaData = UnsafeUtility.ReadArrayElementWithStride<QuickSaveMetaData>(inputData.GetUnsafeReadOnlyPtr(), localIndexInContainer.LocalIndex, stride);
                if (metaData.AmountFound == 0)
                {
                    ecb.RemoveComponent(unfilteredChunkIndex, entityArray[i], typeToRemove);
                }
                else if (typeToRemove.IsEnableable)
                {
                    enabledMask[i] = metaData.Enabled;
                }
            }
        }
    }
    
    [BurstCompile]
    internal unsafe struct AddMissingComponent
    {
        public static void Execute(NativeArray<byte> inputData, ComponentType componentType, int typeSize, NativeArray<Entity> entityArray,
            NativeArray<LocalIndexInContainer> localIndices, EntityCommandBuffer.ParallelWriter ecb, int unfilteredChunkIndex)
        {
            int totalElementSize = typeSize + QuickSaveMetaData.SizeOfStruct;
            
            for (int i = 0; i < entityArray.Length; i++)
            {
                LocalIndexInContainer localIndexInContainer = localIndices[i];
                SafetyChecks.CheckOutOfRangeAccess(localIndexInContainer, totalElementSize, inputData);
                int inputMetaByteIndex = localIndexInContainer.LocalIndex * totalElementSize;
                int inputDataByteIndex = inputMetaByteIndex + QuickSaveMetaData.SizeOfStruct;
                
                var metaData = UnsafeUtility.ReadArrayElementWithStride<QuickSaveMetaData>(inputData.GetUnsafeReadOnlyPtr(), localIndexInContainer.LocalIndex, totalElementSize);

                if (metaData.AmountFound == 1)
                {
                    if (typeSize != 0)
                    {
                        ecb.AddComponent(unfilteredChunkIndex, entityArray[i], componentType, typeSize, inputData.GetSubArray(inputDataByteIndex, typeSize));
                    }
                    else
                    {
                        ecb.AddComponent(unfilteredChunkIndex, entityArray[i], componentType);
                    }

                    if (componentType.IsEnableable && !metaData.Enabled)
                    {
                        ecb.SetComponentEnabled(unfilteredChunkIndex, entityArray[i], componentType, false);
                    }
                }
            }
        }
    }
    
    internal static partial class SafetyChecks
    {
        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        internal static void CheckHasBufferComponent(in ArchetypeChunk chunk, ref DynamicComponentTypeHandle dynamicComponentTypeHandle)
        {
            if (!chunk.Has(ref dynamicComponentTypeHandle))
            {
                throw new Exception("QuickSave(JobSafety): Removing/Adding buffer data is not supported! So the jobs expect the buffer component to be there!");
            }
        }
        
        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        internal static void CheckBufferSize(int maxElements)
        {
            if (maxElements >= QuickSaveMetaData.MaxValueForAmount)
            {
                throw new Exception($"QuickSave(JobSafety): You're exceeding the maximum amount of buffer elements that we can persist ({QuickSaveMetaData.MaxValueForAmount})!");
            }
        }
        
        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        internal static void CheckBufferResizedCorrectly(int currentLength, int expectedLength)
        {
            if (currentLength != expectedLength)
            {
                throw new Exception("QuickSave(JobSafety): The dynamic buffer did not resize correctly!");
            }
        }
        
        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        internal static void CheckCanGrabEnabled(bool checkEnabled, int amountFound)
        {
            if (amountFound == 0 && checkEnabled)
            {
                throw new Exception("QuickSave(JobSafety): A job is trying to grab the enabled bit, but the component was not even found!");
            }
        }
        
        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        internal static void CheckOutOfRangeAccess(LocalIndexInContainer localIndexInContainer, int stride, NativeArray<byte> rawData)
        {
            if (localIndexInContainer.LocalIndex * stride >= rawData.Length)
            {
                throw new IndexOutOfRangeException("QuickSave(JobSafety): LocalIndexInContainer.LocalIndex seems to be out of range. Or the stride is wrong.");
            }
        }
        
        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        internal static void CheckEntityHasDataBuffer(Entity e, BufferLookup<QuickSaveDataContainer.Data> lookup)
        {
            if (!lookup.HasBuffer(e))
            {
                throw new IndexOutOfRangeException("QuickSave(JobSafety): PersistenceState.ArrayIndex seems to be out of range. Or the stride is wrong.");
            }
        }
    }
}