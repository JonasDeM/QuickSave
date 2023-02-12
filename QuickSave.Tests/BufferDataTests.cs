// Author: Jonas De Maeseneer

using System;
using NUnit.Framework;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using UnityEngine;

// ReSharper disable AccessToDisposedClosure

namespace QuickSave.Tests
{
    [TestFixture]
    class BufferDataTests : EcsTestsFixture
    {
        [Test]
        public unsafe void TestReadBufferData([Values(0, 1, 2, 3, 60, 400)] int total)
        {
            int maxElements = total + 4;
            
            CreateTestSettings(maxBufferElements: maxElements);
            CreateEntities(total);
            
            int entityAmount3 = total / 3;
            int entityAmount1 = entityAmount3 + (total%3 > 0 ? 1 : 0);
            int entityAmount2 = entityAmount3 + (total%3 > 1 ? 1 : 0);
            
            var array1InfoRef = CreateFakeSceneInfoRef<PersistentDynamicBufferData1>(Allocator.Temp, entityAmount1);
            Entity array1Container = QuickSaveAPI.CreateInitialSceneContainer(EntityManager, UnityEngine.Hash128.Compute(nameof(PersistentDynamicBufferData1)), ref array1InfoRef.InfoRef.Value, out _);
            var array2InfoRef = CreateFakeSceneInfoRef<PersistentDynamicBufferData2>(Allocator.Temp, entityAmount2);
            Entity array2Container = QuickSaveAPI.CreateInitialSceneContainer(EntityManager, UnityEngine.Hash128.Compute(nameof(PersistentDynamicBufferData2)), ref array2InfoRef.InfoRef.Value, out _);
            var array3InfoRef = CreateFakeSceneInfoRef<PersistentDynamicBufferData3>(Allocator.Temp, entityAmount3);
            Entity array3Container = QuickSaveAPI.CreateInitialSceneContainer(EntityManager, UnityEngine.Hash128.Compute(nameof(PersistentDynamicBufferData3)), ref array3InfoRef.InfoRef.Value, out _);
            
            var query1 = EntityManager.CreateEntityQuery(typeof(PersistentDynamicBufferData1), typeof(LocalIndexInContainer));
            var query2 = EntityManager.CreateEntityQuery(typeof(PersistentDynamicBufferData2), typeof(LocalIndexInContainer));
            var query3 = EntityManager.CreateEntityQuery(typeof(PersistentDynamicBufferData3), typeof(LocalIndexInContainer));

            const int deletedIndex = 5;            
            NativeArray<Entity> entities = EntityManager.CreateEntityQuery(typeof(LocalIndexInContainer)).ToEntityArray(Allocator.Temp);
            foreach (Entity entity in entities)
            {
                int index = EntityManager.GetComponentData<LocalIndexInContainer>(entity).LocalIndex;
                if (index == deletedIndex)
                {
                    EntityManager.DestroyEntity(entity);
                }
            }
            
            var bufferLookUpSystem = World.GetOrCreateSystemManaged<TestSystem>();

            // Grab these after creation because of structural changes inside CreateInitialSceneContainer
            var array1Data = EntityManager.GetBuffer<QuickSaveDataContainer.Data>(array1Container);
            var array2Data = EntityManager.GetBuffer<QuickSaveDataContainer.Data>(array2Container);
            var array3Data = EntityManager.GetBuffer<QuickSaveDataContainer.Data>(array3Container);
            
            // Action
            var job1 = new CopyBufferElementsToByteArray()
            {
                LocalIndexInContainerType = EntityManager.GetComponentTypeHandle<LocalIndexInContainer>(true),
                ByteArrayLookup = bufferLookUpSystem.GetBufferLookup<QuickSaveDataContainer.Data>(),
                ContainerEntity = array1Container,
                SubArrayOffset = 0,
                SubArrayByteSize = array1Data.Length,
                BufferTypeHandle = EntityManager.GetDynamicComponentTypeHandle(typeof(PersistentDynamicBufferData1)),
                MaxElements = maxElements
            }.Schedule(query1, default);
            
            var job2 = new CopyBufferElementsToByteArray()
            {
                LocalIndexInContainerType = EntityManager.GetComponentTypeHandle<LocalIndexInContainer>(true),
                ByteArrayLookup = bufferLookUpSystem.GetBufferLookup<QuickSaveDataContainer.Data>(),
                ContainerEntity = array2Container,
                SubArrayOffset = 0,
                SubArrayByteSize = array2Data.Length,
                BufferTypeHandle = EntityManager.GetDynamicComponentTypeHandle(typeof(PersistentDynamicBufferData2)),
                MaxElements = maxElements
            }.Schedule(query2, default);
            
            var job3 = new CopyBufferElementsToByteArray()
            {
                LocalIndexInContainerType = EntityManager.GetComponentTypeHandle<LocalIndexInContainer>(true),
                ByteArrayLookup = bufferLookUpSystem.GetBufferLookup<QuickSaveDataContainer.Data>(),
                ContainerEntity = array3Container,
                SubArrayOffset = 0,
                SubArrayByteSize = array3Data.Length,
                BufferTypeHandle = EntityManager.GetDynamicComponentTypeHandle(typeof(PersistentDynamicBufferData3)),
                MaxElements = maxElements
            }.Schedule(query3, default);
            
            JobHandle.CombineDependencies(job1, job2, job3).Complete();
            
            // Check Results
            entities = query1.ToEntityArray(Allocator.Temp);
            foreach (Entity entity in entities)
            {
                var originalData = EntityManager.GetBuffer<PersistentDynamicBufferData1>(entity);
                var localIndexInContainer = EntityManager.GetComponentData<LocalIndexInContainer>(entity);
                var byteIndex = localIndexInContainer.LocalIndex * (maxElements * UnsafeUtility.SizeOf<PersistentDynamicBufferData1>() + QuickSaveMetaData.SizeOfStruct);

                var metaDataPtr = (QuickSaveMetaData*)((byte*) array1Data.GetUnsafeReadOnlyPtr() + byteIndex);
                var dataPtr = (PersistentDynamicBufferData1*)(metaDataPtr + 1);
                
                Assert.AreEqual(originalData.Length, metaDataPtr->AmountFound, "CopyBufferElementsToByteArray returned the wrong amount persisted.");
                Assert.True(metaDataPtr->HasChanged, "CopyBufferElementsToByteArray did not record a change when it should have.");

                for (var i = 0; i < originalData.Length; i++)
                {
                    if (i < maxElements)
                    {
                        var originalElement = originalData[i];
                        Assert.AreEqual(originalElement, *(dataPtr + i), "Data output by CopyBufferElementsToByteArray does not match data on entity.");
                    }
                }
            }
            
            entities = query2.ToEntityArray(Allocator.Temp);
            foreach (Entity entity in entities)
            {
                var originalData = EntityManager.GetBuffer<PersistentDynamicBufferData2>(entity);
                var localIndexInContainer = EntityManager.GetComponentData<LocalIndexInContainer>(entity);
                var byteIndex = localIndexInContainer.LocalIndex * (maxElements * UnsafeUtility.SizeOf<PersistentDynamicBufferData2>() + QuickSaveMetaData.SizeOfStruct);

                var metaDataPtr = (QuickSaveMetaData*)((byte*) array2Data.GetUnsafeReadOnlyPtr() + byteIndex);
                var dataPtr = (PersistentDynamicBufferData2*)(metaDataPtr + 1);
                
                Assert.AreEqual(originalData.Length, metaDataPtr->AmountFound, "CopyBufferElementsToByteArray returned the wrong amount persisted.");
                // this query only has empty buffers
                Assert.False(metaDataPtr->HasChanged, "CopyBufferElementsToByteArray recorded a change while it should not have.");

                for (var i = 0; i < originalData.Length; i++)
                {
                    if (i < maxElements)
                    {
                        var originalElement = originalData[i];
                        Assert.AreEqual(originalElement, *(dataPtr + i), "Data output by CopyBufferElementsToByteArray does not match data on entity.");
                    }
                }
            }
            
            entities = query3.ToEntityArray(Allocator.Temp);
            foreach (Entity entity in entities)
            {
                var originalData = EntityManager.GetBuffer<PersistentDynamicBufferData3>(entity);
                var localIndexInContainer = EntityManager.GetComponentData<LocalIndexInContainer>(entity);
                var byteIndex = localIndexInContainer.LocalIndex * (maxElements * UnsafeUtility.SizeOf<PersistentDynamicBufferData3>() + QuickSaveMetaData.SizeOfStruct);

                var metaDataPtr = (QuickSaveMetaData*)((byte*) array3Data.GetUnsafeReadOnlyPtr() + byteIndex);
                var dataPtr = (PersistentDynamicBufferData3*)(metaDataPtr + 1);
                
                Assert.AreEqual(Mathf.Clamp(originalData.Length, 0, maxElements), metaDataPtr->AmountFound, "CopyBufferElementsToByteArray returned the wrong amount persisted.");
                Assert.True(metaDataPtr->HasChanged, "CopyBufferElementsToByteArray did not record a change when it should have.");

                for (var i = 0; i < originalData.Length; i++)
                {
                    if (i < maxElements)
                    {
                        var originalElement = originalData[i];
                        Assert.AreEqual(originalElement, *(dataPtr + i), "Data output by CopyBufferElementsToByteArray does not match data on entity.");
                    }
                }
            }

            int stride1 = (maxElements * UnsafeUtility.SizeOf<PersistentDynamicBufferData1>() + QuickSaveMetaData.SizeOfStruct);
            if (array1Data.Length >= stride1 * deletedIndex)
            {
                bool hasChanged = UnsafeUtility.ReadArrayElementWithStride<QuickSaveMetaData>(array1Data.GetUnsafeReadOnlyPtr(), deletedIndex, stride1).HasChanged;
                Assert.False(hasChanged, "CopyBufferElementsToByteArray recorded a change while it should not have.");
            }
            
            int stride2 = (maxElements * UnsafeUtility.SizeOf<PersistentDynamicBufferData2>() + QuickSaveMetaData.SizeOfStruct);
            if (array2Data.Length >= stride2 * deletedIndex)
            {
                bool hasChanged = UnsafeUtility.ReadArrayElementWithStride<QuickSaveMetaData>(array2Data.GetUnsafeReadOnlyPtr(), deletedIndex, stride2).HasChanged;
                Assert.False(hasChanged, "CopyBufferElementsToByteArray recorded a change while it should not have.");
            }
            
            int stride3 = (maxElements * UnsafeUtility.SizeOf<PersistentDynamicBufferData3>() + QuickSaveMetaData.SizeOfStruct);
            if (array3Data.Length >= stride3 * deletedIndex)
            {
                bool hasChanged = UnsafeUtility.ReadArrayElementWithStride<QuickSaveMetaData>(array3Data.GetUnsafeReadOnlyPtr(), deletedIndex, stride3).HasChanged;
                Assert.False(hasChanged, "CopyBufferElementsToByteArray recorded a change while it should not have.");
            }

            // Cleanup
            EntityManager.DestroyEntity(EntityManager.CreateEntityQuery(typeof(LocalIndexInContainer)));
        }
        
        [Test]
        public unsafe void TestApplyStoredBufferData([Values(0, 1, 2, 3, 60, 400)] int total)
        {
            int maxElements = total + 4;
            
            CreateTestSettings(maxBufferElements: maxElements);
            CreateEntities(total);

            ushort persistedElementAmount = (ushort)total;
            
            int entityAmount3 = total / 3;
            int entityAmount1 = entityAmount3 + (total%3 > 0 ? 1 : 0);
            int entityAmount2 = entityAmount3 + (total%3 > 1 ? 1 : 0);
            
            var array1InfoRef = CreateFakeSceneInfoRef<PersistentDynamicBufferData1>(Allocator.Temp, entityAmount1);
            Entity array1Container = QuickSaveAPI.CreateInitialSceneContainer(EntityManager, UnityEngine.Hash128.Compute(nameof(PersistentDynamicBufferData1)), ref array1InfoRef.InfoRef.Value, out _);
            var array2InfoRef = CreateFakeSceneInfoRef<PersistentDynamicBufferData2>(Allocator.Temp, entityAmount2);
            Entity array2Container = QuickSaveAPI.CreateInitialSceneContainer(EntityManager, UnityEngine.Hash128.Compute(nameof(PersistentDynamicBufferData2)), ref array2InfoRef.InfoRef.Value, out _);
            var array3InfoRef = CreateFakeSceneInfoRef<PersistentDynamicBufferData3>(Allocator.Temp, entityAmount3);
            Entity array3Container = QuickSaveAPI.CreateInitialSceneContainer(EntityManager, UnityEngine.Hash128.Compute(nameof(PersistentDynamicBufferData3)), ref array3InfoRef.InfoRef.Value, out _);
            
            // Grab these after creation because of structural changes inside CreateInitialSceneContainer
            var array1Data = EntityManager.GetBuffer<QuickSaveDataContainer.Data>(array1Container);
            var array2Data = EntityManager.GetBuffer<QuickSaveDataContainer.Data>(array2Container);
            var array3Data = EntityManager.GetBuffer<QuickSaveDataContainer.Data>(array3Container);
            
            for (int i = 0; i < entityAmount1; i++)
            {
                QuickSaveMetaData* mateDataPtr = (QuickSaveMetaData*)((byte*)array1Data.GetUnsafePtr() + i * (maxElements * UnsafeUtility.SizeOf<PersistentDynamicBufferData1>() + QuickSaveMetaData.SizeOfStruct));
                *mateDataPtr = new QuickSaveMetaData(0, persistedElementAmount);
                PersistentDynamicBufferData1* dataPtr = (PersistentDynamicBufferData1*)(mateDataPtr + 1);
                for (int j = 0; j < maxElements; j++)
                {
                    *(dataPtr + j) = new PersistentDynamicBufferData1 {Value = i};
                }
            }
            for (int i = 0; i < entityAmount2; i++)
            {
                QuickSaveMetaData* mateDataPtr = (QuickSaveMetaData*)((byte*)array2Data.GetUnsafePtr() + i * (maxElements * UnsafeUtility.SizeOf<PersistentDynamicBufferData2>() + QuickSaveMetaData.SizeOfStruct));
                *mateDataPtr = new QuickSaveMetaData(0, persistedElementAmount);
                PersistentDynamicBufferData2* dataPtr = (PersistentDynamicBufferData2*)(mateDataPtr + 1);
                for (int j = 0; j < maxElements; j++)
                {
                    *(dataPtr + j) = new PersistentDynamicBufferData2 {Value = i};
                }
            }
            for (int i = 0; i < entityAmount3; i++)
            {
                QuickSaveMetaData* mateDataPtr = (QuickSaveMetaData*)((byte*)array3Data.GetUnsafePtr() + i * (maxElements * UnsafeUtility.SizeOf<PersistentDynamicBufferData3>() + QuickSaveMetaData.SizeOfStruct));
                *mateDataPtr = new QuickSaveMetaData(0, persistedElementAmount);
                PersistentDynamicBufferData3* dataPtr = (PersistentDynamicBufferData3*)(mateDataPtr + 1);
                for (int j = 0; j < maxElements; j++)
                {
                    *(dataPtr + j) = new PersistentDynamicBufferData3 {Value = (byte)i};
                }
            }

            var query1 = EntityManager.CreateEntityQuery(typeof(PersistentDynamicBufferData1), typeof(LocalIndexInContainer));
            var query2 = EntityManager.CreateEntityQuery(typeof(PersistentDynamicBufferData2), typeof(LocalIndexInContainer));
            var query3 = EntityManager.CreateEntityQuery(typeof(PersistentDynamicBufferData3), typeof(LocalIndexInContainer));
            
            var bufferLookUpSystem = World.GetOrCreateSystemManaged<TestSystem>();
            
            // Action
            var job1 = new CopyByteArrayToBufferElements()
            {
                LocalIndexInContainerType = EntityManager.GetComponentTypeHandle<LocalIndexInContainer>(true),
                ByteArrayLookup = bufferLookUpSystem.GetBufferLookup<QuickSaveDataContainer.Data>(true),
                ContainerEntity = array1Container,
                SubArrayOffset = 0,
                SubArrayByteSize = array1Data.Length,
                BufferTypeHandle = EntityManager.GetDynamicComponentTypeHandle(typeof(PersistentDynamicBufferData1)),
                MaxElements = maxElements
            }.Schedule(query1, default);
            
            var job2 = new CopyByteArrayToBufferElements()
            {
                LocalIndexInContainerType = EntityManager.GetComponentTypeHandle<LocalIndexInContainer>(true),
                ByteArrayLookup = bufferLookUpSystem.GetBufferLookup<QuickSaveDataContainer.Data>(true),
                ContainerEntity = array2Container,
                SubArrayOffset = 0,
                SubArrayByteSize = array2Data.Length,
                BufferTypeHandle = EntityManager.GetDynamicComponentTypeHandle(typeof(PersistentDynamicBufferData2)),
                MaxElements = maxElements
            }.Schedule(query2, default);
            
            var job3 = new CopyByteArrayToBufferElements()
            {
                LocalIndexInContainerType = EntityManager.GetComponentTypeHandle<LocalIndexInContainer>(true),
                ByteArrayLookup = bufferLookUpSystem.GetBufferLookup<QuickSaveDataContainer.Data>(true),
                ContainerEntity = array3Container,
                SubArrayOffset = 0,
                SubArrayByteSize = array3Data.Length,
                BufferTypeHandle = EntityManager.GetDynamicComponentTypeHandle(typeof(PersistentDynamicBufferData3)),
                MaxElements = maxElements
            }.Schedule(query3, default);
            
            JobHandle.CombineDependencies(job1, job2, job3).Complete();
            
            // Check Results
            NativeArray<Entity> entities = query1.ToEntityArray(Allocator.Temp);
            foreach (Entity entity in entities)
            {
                var newData = EntityManager.GetBuffer<PersistentDynamicBufferData1>(entity);
                var localIndexInContainer = EntityManager.GetComponentData<LocalIndexInContainer>(entity);
                var byteIndex = localIndexInContainer.LocalIndex * (maxElements * UnsafeUtility.SizeOf<PersistentDynamicBufferData1>() + QuickSaveMetaData.SizeOfStruct);

                var metaDataPtr = (QuickSaveMetaData*)((byte*) array1Data.GetUnsafeReadOnlyPtr() + byteIndex);
                var dataPtr = (PersistentDynamicBufferData1*)(metaDataPtr + 1);

                Assert.AreEqual(metaDataPtr->AmountFound, newData.Length, "CopyByteArrayToBufferElements made a buffer that was the wrong size.");

                for (var i = 0; i < newData.Length; i++)
                {
                    Assert.AreEqual(newData[i], *(dataPtr+i), "Data on entity set by CopyBufferElementsToByteArray does not match data in InputArray.");
                }
            }
            
            entities = query2.ToEntityArray(Allocator.Temp);
            foreach (Entity entity in entities)
            {
                var newData = EntityManager.GetBuffer<PersistentDynamicBufferData2>(entity);
                var localIndexInContainer = EntityManager.GetComponentData<LocalIndexInContainer>(entity);
                var byteIndex = localIndexInContainer.LocalIndex * (maxElements * UnsafeUtility.SizeOf<PersistentDynamicBufferData2>() + QuickSaveMetaData.SizeOfStruct);

                var metaDataPtr = (QuickSaveMetaData*)((byte*) array2Data.GetUnsafeReadOnlyPtr() + byteIndex);
                var dataPtr = (PersistentDynamicBufferData2*)(metaDataPtr + 1);

                Assert.AreEqual(metaDataPtr->AmountFound, newData.Length, "CopyByteArrayToBufferElements made a buffer that was the wrong size.");

                for (var i = 0; i < newData.Length; i++)
                {
                    Assert.AreEqual(newData[i], *(dataPtr+i), "Data on entity set by CopyBufferElementsToByteArray does not match data in InputArray.");
                }
            }
            
            entities = query3.ToEntityArray(Allocator.Temp);
            foreach (Entity entity in entities)
            {
                var newData = EntityManager.GetBuffer<PersistentDynamicBufferData3>(entity);
                var localIndexInContainer = EntityManager.GetComponentData<LocalIndexInContainer>(entity);
                var byteIndex = localIndexInContainer.LocalIndex * (maxElements * UnsafeUtility.SizeOf<PersistentDynamicBufferData3>() + QuickSaveMetaData.SizeOfStruct);

                var metaDataPtr = (QuickSaveMetaData*)((byte*) array3Data.GetUnsafeReadOnlyPtr() + byteIndex);
                var dataPtr = (PersistentDynamicBufferData3*)(metaDataPtr + 1);
                
                Assert.AreEqual(metaDataPtr->AmountFound, newData.Length, "CopyByteArrayToBufferElements made a buffer that was the wrong size.");
                
                for (var i = 0; i < newData.Length; i++)
                {
                    Assert.AreEqual(newData[i], *(dataPtr+i), "Data on entity set by CopyBufferElementsToByteArray does not match data in InputArray.");
                }
            }

            // Cleanup
            EntityManager.DestroyEntity(EntityManager.CreateEntityQuery(typeof(LocalIndexInContainer)));
        }

        private Entity CreateEntity<T, U>(int index) 
            where T : struct, IBufferElementData
            where U : struct, IBufferElementData
        {
            var entity = EntityManager.CreateEntity(typeof(T), typeof(U), typeof(LocalIndexInContainer));
            EntityManager.SetComponentData(entity, new LocalIndexInContainer() {LocalIndex = index});
            return entity;
        }

        void CreateEntities(int count)
        {
            for (int i = 0; i != count; i++)
            {
                int zeroOneTwo = i % 3;
                int peristenceIndex = i / 3;
                switch (zeroOneTwo)
                {
                    case 0:
                        var e1 = CreateEntity<DynamicBufferData1, PersistentDynamicBufferData1>(peristenceIndex);
                        var buffer1 = EntityManager.GetBuffer<DynamicBufferData1>(e1);
                        buffer1.Add(new DynamicBufferData1() {Value = peristenceIndex * 1});
                        buffer1.Add(new DynamicBufferData1() {Value = peristenceIndex * 2});
                        buffer1.Add(new DynamicBufferData1() {Value = peristenceIndex * 3});
                        buffer1.Add(new DynamicBufferData1() {Value = peristenceIndex * 4});
                        var buffer1P = EntityManager.GetBuffer<PersistentDynamicBufferData1>(e1);
                        buffer1P.Add(new PersistentDynamicBufferData1() {Value = peristenceIndex * 1});
                        buffer1P.Add(new PersistentDynamicBufferData1() {Value = peristenceIndex * 2});
                        buffer1P.Add(new PersistentDynamicBufferData1() {Value = peristenceIndex * 3});
                        buffer1P.Add(new PersistentDynamicBufferData1() {Value = peristenceIndex * 4});
                        break;
                    case 1: 
                        CreateEntity<DynamicBufferData2, PersistentDynamicBufferData2>(peristenceIndex);
                        break;
                    case 2: 
                        var e3 = CreateEntity<DynamicBufferData3, PersistentDynamicBufferData3>(peristenceIndex);
                        var buffer3 = EntityManager.GetBuffer<DynamicBufferData3>(e3);
                        buffer3.Add(new DynamicBufferData3() {Value = 1});
                        buffer3.Add(new DynamicBufferData3() {Value = 2});
                        buffer3.Add(new DynamicBufferData3() {Value = 3});
                        buffer3.Add(new DynamicBufferData3() {Value = 4});
                        buffer3.Add(new DynamicBufferData3() {Value = 5});
                        var buffer3P = EntityManager.GetBuffer<PersistentDynamicBufferData3>(e3);
                        buffer3P.Add(new PersistentDynamicBufferData3() {Value = 1});
                        buffer3P.Add(new PersistentDynamicBufferData3() {Value = 2});
                        buffer3P.Add(new PersistentDynamicBufferData3() {Value = 3});
                        buffer3P.Add(new PersistentDynamicBufferData3() {Value = 4});
                        buffer3P.Add(new PersistentDynamicBufferData3() {Value = 5});
                        break;
                    default: throw new Exception("zeroOneTwo was not 0, 1 or 2");
                }
            }
        }

        [InternalBufferCapacity(2)]
        internal struct PersistentDynamicBufferData1 : IBufferElementData
        {
            public int Value;
            
            public override string ToString()
            {
                return Value.ToString();
            }
        }
        internal struct PersistentDynamicBufferData2 : IBufferElementData
        {
            public float Value;
            
            public override string ToString()
            {
                return Value.ToString();
            }
        }
        internal struct PersistentDynamicBufferData3 : IBufferElementData
        {
            public byte Value;
            
            public override string ToString()
            {
                return Value.ToString();
            }
        }
    }
}
