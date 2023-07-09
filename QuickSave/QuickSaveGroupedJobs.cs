// Author: Jonas De Maeseneer

using System;
using System.Diagnostics;
using QuickSave.Containers;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;

namespace QuickSave
{
    // This job uses nested native containers, the unity job safety system doesn't support that, so we only run these jobs in a build.
    [BurstCompile]
    internal struct GroupedApplyJob : IJobChunk
    {        
        [ReadOnly]
        public BufferLookup<QuickSaveDataContainer.Data> ByteArrayLookup;
        [ReadOnly]
        public BufferLookup<QuickSaveArchetypeDataLayout> DataLayoutLookup;
        public Entity ContainerEntity;
        
        [ReadOnly] public EntityTypeHandle EntityTypeHandle;
        [ReadOnly] public ComponentTypeHandle<LocalIndexInContainer> LocalIndexInContainerTypeHandle;
        [ReadOnly] public SharedComponentTypeHandle<QuickSaveArchetypeIndexInContainer> QuickSaveArchetypeIndexInContainerHandle;
        
        [DeallocateOnJobCompletion]
        [ReadOnly] public ComponentTypeHandleArray DynamicComponentTypeHandles;
        
        public EntityCommandBuffer.ParallelWriter Ecb;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            NativeArray<LocalIndexInContainer> persistenceStateArray = chunk.GetNativeArray(ref LocalIndexInContainerTypeHandle);
            NativeArray<Entity> entityArray = chunk.GetNativeArray(EntityTypeHandle);
            
            ushort archetypeIndexInContainer = chunk.GetSharedComponent(QuickSaveArchetypeIndexInContainerHandle).IndexInContainer;
            
            SafetyChecks.CheckEntityHasDataLayouts(ContainerEntity, DataLayoutLookup);
            QuickSaveArchetypeDataLayout dataLayout = DataLayoutLookup[ContainerEntity][archetypeIndexInContainer];
            ref BlobArray<QuickSaveArchetypeDataLayout.TypeInfo> typeInfoArray = ref dataLayout.TypeInfoArrayRef.Value;
            SafetyChecks.CheckEntityHasDataBuffer(ContainerEntity, ByteArrayLookup);
            var dataForArchetype = ByteArrayLookup[ContainerEntity].Reinterpret<byte>().AsNativeArray().GetSubArray(dataLayout.Offset, dataLayout.Amount * dataLayout.SizePerEntity);

            for (int typeInfoIndex = 0; typeInfoIndex < typeInfoArray.Length; typeInfoIndex++)
            {
                // type info
                QuickSaveArchetypeDataLayout.TypeInfo typeInfo = typeInfoArray[typeInfoIndex];
                ComponentType runtimeType = ComponentType.ReadWrite(QuickSaveSettings.GetTypeIndex(typeInfo.QuickSaveTypeHandle)); 
                int stride = typeInfo.ElementSize * typeInfo.MaxElements + QuickSaveMetaData.SizeOfStruct;
                int byteSize = dataLayout.Amount * stride;

                // Grab read-only containers
                var inputData = dataForArchetype.GetSubArray(typeInfo.Offset, byteSize);
                
                bool found = DynamicComponentTypeHandles.GetByTypeIndex(runtimeType.TypeIndex, out DynamicComponentTypeHandle typeHandle);
                SafetyChecks.CheckFoundDynamicTypeHandle(found);
                UntypedAccessExtensionMethods.DisableSafetyChecks(ref typeHandle);

                if (typeInfo.IsBuffer)
                {
                    SafetyChecks.CheckHasBufferComponent(chunk, ref typeHandle); // Removing/Adding buffer data is not supported
                    var untypedBufferAccessor = chunk.GetUntypedBufferAccessor(ref typeHandle);
                    EnabledMask enabledMask = runtimeType.IsEnableable ? chunk.GetEnabledMask(ref typeHandle) : default;
                    CopyByteArrayToBufferElements.Execute(inputData, typeInfo.MaxElements, untypedBufferAccessor, persistenceStateArray, enabledMask, runtimeType.IsEnableable);
                }
                else // if ComponentData
                {
                    if (chunk.Has(ref typeHandle))
                    {
                        if (typeInfo.ElementSize > 0)
                        {
                            var byteArray = chunk.GetComponentDataAsByteArray(ref typeHandle);
                            CopyByteArrayToComponentData.Execute(inputData, typeInfo.ElementSize, byteArray, persistenceStateArray);
                        }

                        EnabledMask enabledMask = runtimeType.IsEnableable ? chunk.GetEnabledMask(ref typeHandle) : default;
                        RemoveEnableOrDisableExistingComponent.Execute(inputData, runtimeType, typeInfo.ElementSize, entityArray, persistenceStateArray, enabledMask, Ecb, unfilteredChunkIndex);
                    }
                    else
                    {
                        AddMissingComponent.Execute(inputData, runtimeType,  typeInfo.ElementSize, entityArray, persistenceStateArray, Ecb, unfilteredChunkIndex);
                    }
                }
            }
        }
    }

    // This job uses nested native containers, the unity job safety system doesn't support that, so we only run these jobs in a build.
    [BurstCompile]
    internal struct GroupedPersistJob : IJobChunk
    {
        [WriteOnly, NativeDisableContainerSafetyRestriction]
        public BufferLookup<QuickSaveDataContainer.Data> ByteArrayLookup;
        [ReadOnly]
        public BufferLookup<QuickSaveArchetypeDataLayout> DataLayoutLookup;
        public Entity ContainerEntity;
        
        [ReadOnly] public ComponentTypeHandle<LocalIndexInContainer> LocalIndexInContainerTypeHandle;
        [ReadOnly] public SharedComponentTypeHandle<QuickSaveArchetypeIndexInContainer> QuickSaveArchetypeIndexInContainerHandle;
        
        [DeallocateOnJobCompletion]
        [ReadOnly] public ComponentTypeHandleArray DynamicComponentTypeHandles;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            NativeArray<LocalIndexInContainer> persistenceStateArray = chunk.GetNativeArray(ref LocalIndexInContainerTypeHandle);

            ushort archetypeIndexInContainer = chunk.GetSharedComponent(QuickSaveArchetypeIndexInContainerHandle).IndexInContainer;
            
            SafetyChecks.CheckEntityHasDataLayouts(ContainerEntity, DataLayoutLookup);
            QuickSaveArchetypeDataLayout dataLayout = DataLayoutLookup[ContainerEntity][archetypeIndexInContainer];
            ref BlobArray<QuickSaveArchetypeDataLayout.TypeInfo> typeInfoArray = ref dataLayout.TypeInfoArrayRef.Value;
            SafetyChecks.CheckEntityHasDataBuffer(ContainerEntity, ByteArrayLookup);
            var dataForArchetype = ByteArrayLookup[ContainerEntity].Reinterpret<byte>().AsNativeArray().GetSubArray(dataLayout.Offset, dataLayout.Amount * dataLayout.SizePerEntity);
            
            for (int typeInfoIndex = 0; typeInfoIndex < typeInfoArray.Length; typeInfoIndex++)
            {
                // type info
                QuickSaveArchetypeDataLayout.TypeInfo typeInfo = typeInfoArray[typeInfoIndex];
                ComponentType runtimeType = ComponentType.ReadOnly(QuickSaveSettings.GetTypeIndex(typeInfo.QuickSaveTypeHandle)); 
                int stride = typeInfo.ElementSize * typeInfo.MaxElements + QuickSaveMetaData.SizeOfStruct;
                int byteSize = dataLayout.Amount * stride;
                bool checkEnabled = runtimeType.IsEnableable;

                // Grab containers
                var outputData = dataForArchetype.GetSubArray(typeInfo.Offset, byteSize);
                
                bool found = DynamicComponentTypeHandles.GetByTypeIndex(runtimeType.TypeIndex, out DynamicComponentTypeHandle typeHandle);
                SafetyChecks.CheckFoundDynamicTypeHandle(found);
                UntypedAccessExtensionMethods.DisableSafetyChecks(ref typeHandle);

                if (typeInfo.IsBuffer)
                {
                    SafetyChecks.CheckHasBufferComponent(chunk, ref typeHandle); // Removing/Adding buffer data is not supported
                    EnabledMask enabledMask = checkEnabled ? chunk.GetEnabledMask(ref typeHandle) : default;
                    var untypedBufferAccessor = chunk.GetUntypedBufferAccessor(ref typeHandle);
                    CopyBufferElementsToByteArray.Execute(outputData, typeInfo.MaxElements, untypedBufferAccessor, persistenceStateArray, checkEnabled, enabledMask);
                }
                else
                {
                    if (chunk.Has(ref typeHandle))
                    {
                        EnabledMask enabledMask = checkEnabled ? chunk.GetEnabledMask(ref typeHandle) : default;
                        if (typeInfo.ElementSize > 0)
                        {
                            var byteArray = chunk.GetComponentDataAsByteArray(ref typeHandle);
                            CopyComponentDataToByteArray.Execute(outputData, typeInfo.ElementSize, byteArray, persistenceStateArray, checkEnabled, enabledMask);
                        }
                        else
                        {
                            UpdateMetaDataForComponent.Execute(outputData, stride, persistenceStateArray, 1, checkEnabled, enabledMask);
                        }
                    }
                    else
                    {
                        UpdateMetaDataForComponent.Execute(outputData, stride, persistenceStateArray, 0, false, default);
                    }
                }
            }
        }
    }

    internal static partial class SafetyChecks
    {
        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        internal static void CheckEntityHasDataLayouts(Entity e, BufferLookup<QuickSaveArchetypeDataLayout> lookup)
        {
            if (!lookup.HasBuffer(e))
            {
                throw new IndexOutOfRangeException("QuickSave(JobSafety): LocalIndexInContainer.LocalIndex seems to be out of range. Or the stride is wrong.");
            }
        }
        
        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        internal static void CheckFoundDynamicTypeHandle(bool found)
        {
            if (!found)
            {
                throw new Exception("QuickSave(JobSafety): Did not find the DynamicComponentTypeHandle we were looking for!");
            }
        }
    }
}