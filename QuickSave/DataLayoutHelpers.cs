// Author: Jonas De Maeseneer

using System;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;

namespace QuickSave
{
    public static class DataLayoutHelpers
    {
        [Pure]
        private static unsafe BlobAssetReference<BlobArray<QuickSaveArchetypeDataLayout.TypeInfo>> BuildTypeInfoBlobAsset(
            ref QuickSaveArchetypesInScene quickSaveArchetypesInScene, int amountEntities, out int sizePerEntity)
        {
            NativeArray<QuickSaveTypeHandle> blobArrayAsNative =
                NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<QuickSaveTypeHandle>(quickSaveArchetypesInScene.QuickSaveTypeHandles.GetUnsafePtr(),
                    quickSaveArchetypesInScene.QuickSaveTypeHandles.Length, Allocator.None);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            var safety = AtomicSafetyHandle.Create();
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref blobArrayAsNative, safety);
#endif

            var blobAssetReference = BuildTypeInfoBlobAsset(blobArrayAsNative, amountEntities, out sizePerEntity);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.Release(safety);
#endif

            return blobAssetReference;
        }

        private static BlobAssetReference<BlobArray<QuickSaveArchetypeDataLayout.TypeInfo>> BuildTypeInfoBlobAsset(
            NativeArray<QuickSaveTypeHandle> quickSaveTypeHandles, int amountEntities, out int sizePerEntity)
        {
            BlobAssetReference<BlobArray<QuickSaveArchetypeDataLayout.TypeInfo>> blobAssetReference;
            int currentOffset = 0;
            sizePerEntity = 0;

            using (BlobBuilder blobBuilder = new BlobBuilder(Allocator.Temp))
            {
                ref BlobArray<QuickSaveArchetypeDataLayout.TypeInfo> blobArray =
                    ref blobBuilder.ConstructRoot<BlobArray<QuickSaveArchetypeDataLayout.TypeInfo>>();

                BlobBuilderArray<QuickSaveArchetypeDataLayout.TypeInfo> blobBuilderArray = blobBuilder.Allocate(ref blobArray, quickSaveTypeHandles.Length);

                for (int i = 0; i < blobBuilderArray.Length; i++)
                {
                    QuickSaveTypeHandle quickSaveTypeHandle = quickSaveTypeHandles[i];
                    TypeManager.TypeInfo typeInfo = TypeManager.GetTypeInfo(QuickSaveSettings.GetTypeIndex(quickSaveTypeHandle));
                    int maxElements = QuickSaveSettings.GetMaxElements(quickSaveTypeHandle);
                    ValidateType(typeInfo);

                    blobBuilderArray[i] = new QuickSaveArchetypeDataLayout.TypeInfo()
                    {
                        QuickSaveTypeHandle = quickSaveTypeHandles[i],
                        ElementSize = typeInfo.ElementSize,
                        IsBuffer = typeInfo.Category == TypeManager.TypeCategory.BufferData,
                        MaxElements = maxElements,
                        Offset = currentOffset
                    };
                    int sizeForComponent = (typeInfo.ElementSize * maxElements) + QuickSaveMetaData.SizeOfStruct;
                    sizePerEntity += sizeForComponent;
                    currentOffset += sizeForComponent * amountEntities;
                }

                blobAssetReference = blobBuilder.CreateBlobAssetReference<BlobArray<QuickSaveArchetypeDataLayout.TypeInfo>>(Allocator.Persistent);
            }

            return blobAssetReference;
        }

        internal static void BuildDataLayouts(ref QuickSaveSceneInfo quickSaveSceneInfo, DynamicBuffer<QuickSaveArchetypeDataLayout> layoutsToFill)
        {
            int offset = 0;
            layoutsToFill.ResizeUninitialized(quickSaveSceneInfo.QuickSaveArchetypesInScene.Length);
            
            for (var i = 0; i < quickSaveSceneInfo.QuickSaveArchetypesInScene.Length; i++)
            {
                ref QuickSaveArchetypesInScene quickSaveArchetypesInScene = ref quickSaveSceneInfo.QuickSaveArchetypesInScene[i];

                var dataLayout = new QuickSaveArchetypeDataLayout()
                {
                    Amount = quickSaveArchetypesInScene.AmountEntities,
                    TypeInfoArrayRef = BuildTypeInfoBlobAsset(ref quickSaveArchetypesInScene, quickSaveArchetypesInScene.AmountEntities, out int sizePerEntity),
                    SizePerEntity = sizePerEntity,
                    Offset = offset
                };
                offset += quickSaveArchetypesInScene.AmountEntities * sizePerEntity;

                layoutsToFill[i] = dataLayout;
            }
        }
        
        private static unsafe bool CheckResize(NativeArray<QuickSaveArchetypeDataLayout> dataLayouts, NativeArray<int> newAmounts, out int oldSize, out int newSize)
        {
            CheckNewAmountsArray(dataLayouts, newAmounts);

            oldSize = 0;
            newSize = 0;
            bool difference = false;
            
            for (var i = 0; i < dataLayouts.Length; i++)
            {
                var dataLayout = dataLayouts[i];
                int newAmount = newAmounts[i];
                
                oldSize += dataLayout.Amount * dataLayout.SizePerEntity;
                newSize += newAmount * dataLayout.SizePerEntity;
                
                if (newAmount != dataLayout.Amount)
                    difference = true;
            }

            return difference;
        }
        
        internal static unsafe void ResizeDataContainer(NativeList<byte> data, NativeArray<QuickSaveArchetypeDataLayout> dataLayouts, NativeArray<int> newAmounts)
        {
            if (!CheckResize(dataLayouts, newAmounts, out int oldSize, out int newSize))
                return;

            if (newSize > oldSize)
                data.Resize(newSize, NativeArrayOptions.ClearMemory);
            
            var startOfDataPtr = (byte*) data.GetUnsafePtr(); // important to do this after resize!
            
            {
                int newByteOffset = 0;
                for (int i = 0; i < dataLayouts.Length; i++)
                {
                    var dataLayout = dataLayouts[i];
                    int newAmount = newAmounts[i];
                    
                    int oldByteOffset = dataLayout.Offset;

                    // ONLY SHRINK IN FIRST LOOP
                    if (newAmount < dataLayout.Amount)
                    {
                        int oldByteAmount = dataLayout.Amount * dataLayout.SizePerEntity;
                        var subArray = data.AsArray().GetSubArray(oldByteOffset, oldByteAmount);
                        ShrinkFixUp(subArray, newAmount, dataLayout);
                    }
                    else
                    {
                        newAmount = dataLayout.Amount;
                    }
                    
                    AssertMovingToLeft(oldByteOffset, newByteOffset);

                    // MOVE SUB ARRAY TO LEFT
                    int subArraySize = newAmount * dataLayout.SizePerEntity;
                    UnsafeUtility.MemMove(startOfDataPtr + newByteOffset, startOfDataPtr + oldByteOffset, subArraySize);

                    // Update data layout
                    dataLayout.Amount = newAmount;
                    dataLayout.Offset = newByteOffset;
                    dataLayouts[i] = dataLayout;

                    // newByteOffset for next layout
                    newByteOffset += subArraySize;
                }
            }
            
            {
                int newByteOffset = newSize;
                for (int i = dataLayouts.Length - 1; i >= 0; i--)
                {
                    var dataLayout = dataLayouts[i];
                    int newAmount = newAmounts[i];
                    
                    int oldByteOffset = dataLayout.Offset;
                    
                    // ONLY GROW IN SECOND LOOP
                    if (newAmount > dataLayout.Amount)
                    {
                        int newByteAmount = newAmount * dataLayout.SizePerEntity;
                        var subArray = data.AsArray().GetSubArray(oldByteOffset, newByteAmount);
                        GrowFixUp(subArray, newAmount, dataLayout);
                    }
                    else
                    {
                        // Assert, if it's not growing, the amount is already correct (since we shrunk already)
                        AssertNewAmountAlreadyCorrect(newAmount, dataLayout);
                    }
                    
                    int subArraySize = newAmount * dataLayout.SizePerEntity;
                    
                    // newByteOffset for this layout
                    newByteOffset -= subArraySize;
                    AssertMovingToRight(oldByteOffset, newByteOffset);
                    
                    // MOVE SUB ARRAY TO RIGHT
                    UnsafeUtility.MemMove(startOfDataPtr + newByteOffset, startOfDataPtr + oldByteOffset, subArraySize);
                    
                    // Update data layout
                    dataLayout.Amount = newAmount;
                    dataLayout.Offset = newByteOffset;
                    dataLayouts[i] = dataLayout;
                }
            }
            
            if (newSize < oldSize)
                data.Resize(newSize, NativeArrayOptions.ClearMemory);
        }
        
        private static unsafe void ShrinkFixUp(NativeArray<byte> data, int newAmount, QuickSaveArchetypeDataLayout dataLayout)
        {
            // Shrinking -> Move memory from high to low -> do lowest memory block first
            AssertNotResized(data, newAmount, dataLayout); // if shrinking ensure the array is not yet resized (dataLayout.Amount represents oldAmount)
            
            ref BlobArray<QuickSaveArchetypeDataLayout.TypeInfo> infoArray = ref dataLayout.TypeInfoArrayRef.Value;
            
            int newOffset = 0;
            var startOfDataPtr = (byte*) data.GetUnsafePtr();
            for (int i = 0; i < infoArray.Length; i++)
            {
                QuickSaveArchetypeDataLayout.TypeInfo info = infoArray[i];
                int oldOffset = info.Offset;
                
                // Update data layout
                info.Offset = newOffset;
                infoArray[i] = info;
                
                // Move Data
                UnsafeUtility.MemMove(startOfDataPtr + newOffset, startOfDataPtr + oldOffset, info.SizePerEntityForThisType * newAmount);
                
                // newOffset for next type
                newOffset += info.SizePerEntityForThisType * newAmount;
            }
        }

        private static unsafe void GrowFixUp(NativeArray<byte> data, int newAmount, QuickSaveArchetypeDataLayout dataLayout)
        { 
            // Growing -> Move memory from low to high -> do highest memory block first
            AssertResized(data, newAmount, dataLayout); // if growing ensure the array is already resized
            
            ref BlobArray<QuickSaveArchetypeDataLayout.TypeInfo> infoArray = ref dataLayout.TypeInfoArrayRef.Value;
            int oldAmount = dataLayout.Amount;
            
            int newOffset = data.Length;
            var startOfDataPtr = (byte*) data.GetUnsafePtr(); // Important that this call is after the resize
            for (int i = infoArray.Length - 1; i >= 0; i--)
            {
                QuickSaveArchetypeDataLayout.TypeInfo info = infoArray[i];
                int oldOffset = info.Offset;
                
                // newOffset for this type
                newOffset -= info.SizePerEntityForThisType * newAmount;
                
                // Update data layout
                info.Offset = newOffset;
                infoArray[i] = info;
                
                // Move Data
                UnsafeUtility.MemMove(startOfDataPtr + newOffset, startOfDataPtr + oldOffset, info.SizePerEntityForThisType * oldAmount);
            }
        }
        
        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private static void AssertResized(NativeArray<byte> data, int newAmount, QuickSaveArchetypeDataLayout dataLayout)
        {
            if (data.Length != newAmount * dataLayout.SizePerEntity)
            {
                throw new ArgumentException("Expected the (sub)array to already have the new grown size!");
            }
        }
        
        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private static void AssertNotResized(NativeArray<byte> data, int newAmount, QuickSaveArchetypeDataLayout dataLayout)
        {
            if (data.Length != dataLayout.Amount * dataLayout.SizePerEntity)
            {
                throw new ArgumentException("Expected the (sub)array to still have the old non-shrunk size!");
            }
        }
        
        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private static void AssertMovingToRight(int oldByteOffset, int newByteOffset)
        {
            if (oldByteOffset > newByteOffset)
            {
                throw new Exception("Expected to only move data to the right (low to high) here!");
            }
        }
        
        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private static void AssertMovingToLeft(int oldByteOffset, int newByteOffset)
        {
            if (newByteOffset > oldByteOffset)
            {
                throw new Exception("Expected to only move data to the left (high to low) here!");
            }
        }
        
        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private static void CheckNewAmountsArray(NativeArray<QuickSaveArchetypeDataLayout> dataLayouts, NativeArray<int> newAmounts)
        {
            if (dataLayouts.Length != newAmounts.Length)
            {
                throw new Exception("Expected the 'NewAmounts' array to have the same length as the amount of QuickSaveArchetypes in this container!");
            }
        }
        
        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private static void AssertNewAmountAlreadyCorrect(int newAmount, QuickSaveArchetypeDataLayout layout)
        {
            if (newAmount != layout.Amount)
            {
                throw new Exception("Expected that if this QuickSaveArchetype layout did not grow, it already had the correct size since shrinking already happened.");
            }
        }

        [BurstDiscard] // Todo perhaps we need a validate type that is burst compatible
        [Conditional("DEBUG")]
        private static void ValidateType(TypeManager.TypeInfo typeInfo)
        {
            if (!QuickSaveSettings.IsSupported(typeInfo, out string notSupportedReason))
            {
                throw new NotSupportedException(notSupportedReason);
            }
        }
    }
}