// Author: Jonas De Maeseneer

using System;
using NUnit.Framework;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;

// ReSharper disable AccessToDisposedClosure

namespace QuickSave.Tests
{
    [TestFixture]
    class ComponentDataTests : EcsTestsFixture
    {
        [Test]
        public unsafe void TestReadComponentData([Values(0, 1, 2, 3, 60, 400)] int total)
        {
            // Preparation
            CreateTestSettings();
            CreateEntities(total);

            int entityAmount3 = total / 3;
            int entityAmount1 = entityAmount3 + (total%3 > 0 ? 1 : 0);
            int entityAmount2 = entityAmount3 + (total%3 > 1 ? 1 : 0);

            var array1IntInfoRef = CreateFakeSceneInfoRef<EcsPersistingTestData>(Allocator.Temp, entityAmount1);
            Entity array1IntContainer = QuickSaveAPI.CreateInitialSceneContainer(EntityManager, UnityEngine.Hash128.Compute(nameof(EcsPersistingTestData)), ref array1IntInfoRef.InfoRef.Value, out _);
            var array2FloatInfoRef = CreateFakeSceneInfoRef<EcsPersistingFloatTestData2>(Allocator.Temp, entityAmount2);
            Entity array2FloatContainer = QuickSaveAPI.CreateInitialSceneContainer(EntityManager, UnityEngine.Hash128.Compute(nameof(EcsPersistingFloatTestData2)), ref array2FloatInfoRef.InfoRef.Value, out _);
            var array5IntInfoRef = CreateFakeSceneInfoRef<EcsPersistingTestData5>(Allocator.Temp, entityAmount3);
            Entity array5IntContainer = QuickSaveAPI.CreateInitialSceneContainer(EntityManager, UnityEngine.Hash128.Compute(nameof(EcsPersistingTestData5)), ref array5IntInfoRef.InfoRef.Value, out _);

            var query1 = EntityManager.CreateEntityQuery(typeof(EcsPersistingTestData), typeof(LocalIndexInContainer));
            var query2 = EntityManager.CreateEntityQuery(typeof(EcsPersistingFloatTestData2), typeof(LocalIndexInContainer));
            var query3 = EntityManager.CreateEntityQuery(typeof(EcsPersistingTestData5), typeof(LocalIndexInContainer));

            NativeArray<Entity> entities = query3.ToEntityArray(Allocator.Temp);
            foreach (Entity entity in entities)
            {
                if (EntityManager.GetComponentData<LocalIndexInContainer>(entity).LocalIndex < 3)
                {
                    EntityManager.DestroyEntity(entity);
                }
            }

            var bufferLookUpSystem = World.GetOrCreateSystemManaged<TestSystem>();

            // Grab these after creation because of structural changes inside CreateInitialSceneContainer
            var array1IntData = EntityManager.GetBuffer<QuickSaveDataContainer.Data>(array1IntContainer);
            var array2FloatData = EntityManager.GetBuffer<QuickSaveDataContainer.Data>(array2FloatContainer);
            var array5IntData = EntityManager.GetBuffer<QuickSaveDataContainer.Data>(array5IntContainer);

            // Action
            var job1 = new CopyComponentDataToByteArray()
            {
                ComponentTypeHandle = EntityManager.GetDynamicComponentTypeHandle(typeof(EcsPersistingTestData)),
                TypeSize = UnsafeUtility.SizeOf<EcsPersistingTestData>(),
                LocalIndexInContainerType = EntityManager.GetComponentTypeHandle<LocalIndexInContainer>(true),
                ByteArrayLookup = bufferLookUpSystem.GetBufferLookup<QuickSaveDataContainer.Data>(),
                ContainerEntity = array1IntContainer,
                SubArrayOffset = 0,
                SubArrayByteSize = array1IntData.Length
            }.Schedule(query1, default);
            
            var job2 = new CopyComponentDataToByteArray()
            {
                ComponentTypeHandle = EntityManager.GetDynamicComponentTypeHandle(typeof(EcsPersistingFloatTestData2)),
                TypeSize = UnsafeUtility.SizeOf<EcsPersistingFloatTestData2>() ,
                LocalIndexInContainerType = EntityManager.GetComponentTypeHandle<LocalIndexInContainer>(true),
                ByteArrayLookup = bufferLookUpSystem.GetBufferLookup<QuickSaveDataContainer.Data>(),
                ContainerEntity = array2FloatContainer,
                SubArrayOffset = 0,
                SubArrayByteSize = array2FloatData.Length
            }.Schedule(query2, default);
            
            var job3 = new CopyComponentDataToByteArray()
            {
                ComponentTypeHandle = EntityManager.GetDynamicComponentTypeHandle(typeof(EcsPersistingTestData5)),
                TypeSize = UnsafeUtility.SizeOf<EcsPersistingTestData5>(),
                LocalIndexInContainerType = EntityManager.GetComponentTypeHandle<LocalIndexInContainer>(true),
                ByteArrayLookup = bufferLookUpSystem.GetBufferLookup<QuickSaveDataContainer.Data>(),
                ContainerEntity = array5IntContainer,
                SubArrayOffset = 0,
                SubArrayByteSize = array5IntData.Length
            }.Schedule(query3, default);
            
            JobHandle.CombineDependencies(job1, job2, job3).Complete();
            
            // Check Results
            entities = query1.ToEntityArray(Allocator.Temp);
            foreach (Entity entity in entities)
            {
                var originalData = EntityManager.GetComponentData<EcsPersistingTestData>(entity);
                var localIndexInContainer = EntityManager.GetComponentData<LocalIndexInContainer>(entity);
                int byteIndex = localIndexInContainer.LocalIndex * (UnsafeUtility.SizeOf<EcsPersistingTestData>() + QuickSaveMetaData.SizeOfStruct) + QuickSaveMetaData.SizeOfStruct;
                var copiedData = *(EcsPersistingTestData*)((byte*)array1IntData.GetUnsafeReadOnlyPtr() + byteIndex);
                Assert.True(originalData.Equals(copiedData), "Data output by CopyComponentDataToByteArray does not match data on entity.");
            }
            
            entities = query2.ToEntityArray(Allocator.Temp);
            foreach (Entity entity in entities)
            {
                var originalData = EntityManager.GetComponentData<EcsPersistingFloatTestData2>(entity);
                var localIndexInContainer = EntityManager.GetComponentData<LocalIndexInContainer>(entity);
                int byteIndex = localIndexInContainer.LocalIndex * (UnsafeUtility.SizeOf<EcsPersistingFloatTestData2>() + QuickSaveMetaData.SizeOfStruct) + QuickSaveMetaData.SizeOfStruct;
                var copiedData = *(EcsPersistingFloatTestData2*)((byte*)array2FloatData.GetUnsafeReadOnlyPtr() + byteIndex);
                Assert.True(originalData.Equals(copiedData), "Data output by CopyComponentDataToByteArray does not match data on entity.");
            }

            entities = query3.ToEntityArray(Allocator.Temp);
            foreach (Entity entity in entities)
            {
                var originalData = EntityManager.GetComponentData<EcsPersistingTestData5>(entity);
                var localIndexInContainer = EntityManager.GetComponentData<LocalIndexInContainer>(entity);
                int byteIndex = localIndexInContainer.LocalIndex * (UnsafeUtility.SizeOf<EcsPersistingTestData5>() + QuickSaveMetaData.SizeOfStruct) + QuickSaveMetaData.SizeOfStruct;
                var copiedData = *(EcsPersistingTestData5*)((byte*)array5IntData.GetUnsafeReadOnlyPtr() + byteIndex);
                Assert.True(originalData.Equals(copiedData), "Data output by CopyComponentDataToByteArray does not match data on entity.");
            }
            
            for (int i = 0; i < entityAmount1; i++)
            {
                var stride = (UnsafeUtility.SizeOf<EcsPersistingTestData>() + QuickSaveMetaData.SizeOfStruct);
                var metaData = UnsafeUtility.ReadArrayElementWithStride<QuickSaveMetaData>(array1IntData.GetUnsafeReadOnlyPtr(), i, stride);
                Assert.True(metaData.AmountFound == 1, "Entity was not found even though it existed.");
                Assert.True(metaData.HasChanged, "Data changed but meta data didn't record the change.");
            }
            for (int i = 0; i < entityAmount2; i++)
            {
                var stride = (UnsafeUtility.SizeOf<EcsPersistingFloatTestData2>() + QuickSaveMetaData.SizeOfStruct);
                var metaData = UnsafeUtility.ReadArrayElementWithStride<QuickSaveMetaData>(array2FloatData.GetUnsafeReadOnlyPtr(), i, stride);
                Assert.True(metaData.AmountFound == 1, "Entity was not found even though it existed.");
                Assert.True(metaData.HasChanged, "Data changed but meta data didn't record the change.");
            }
            int amount5Found = 0;
            for (int i = 0; i < entityAmount3; i++)
            {
                var stride = (UnsafeUtility.SizeOf<EcsPersistingTestData5>() + QuickSaveMetaData.SizeOfStruct);
                var metaData = UnsafeUtility.ReadArrayElementWithStride<QuickSaveMetaData>(array5IntData.GetUnsafeReadOnlyPtr(), i, stride);
                Assert.True(metaData.AmountFound == 1 || metaData.AmountFound == 0, "Incorrect AmountFound on meta data.");
                amount5Found += metaData.AmountFound;
                if (i > 2)
                {
                    Assert.True(metaData.HasChanged, "Data changed but meta data didn't record the change.");
                }
                else
                {
                    Assert.False(metaData.HasChanged, "Data did not change but meta data recorded a change.");
                }
            }
            Assert.True(amount5Found < entityAmount3 || amount5Found == 0, "More values were persisted than entities existed.");

            // Cleanup
            EntityManager.DestroyEntity(EntityManager.CreateEntityQuery(typeof(LocalIndexInContainer)));
        }
        
        [Test]
        public unsafe void TestApplyStoredData([Values(0, 1, 2, 3, 60, 401)] int total)
        {
            // Preparation
            CreateTestSettings();
            CreateEntities(total);
            
            int entityAmount3 = total / 3;
            int entityAmount1 = entityAmount3 + (total%3 > 0 ? 1 : 0);
            int entityAmount2 = entityAmount3 + (total%3 > 1 ? 1 : 0);
            
            var array1IntInfoRef = CreateFakeSceneInfoRef<EcsPersistingTestData>(Allocator.Temp, entityAmount1);
            Entity array1IntContainer = QuickSaveAPI.CreateInitialSceneContainer(EntityManager, UnityEngine.Hash128.Compute(nameof(EcsPersistingTestData)), ref array1IntInfoRef.InfoRef.Value, out _);
            var array2FloatInfoRef = CreateFakeSceneInfoRef<EcsPersistingFloatTestData2>(Allocator.Temp, entityAmount2);
            Entity array2FloatContainer = QuickSaveAPI.CreateInitialSceneContainer(EntityManager, UnityEngine.Hash128.Compute(nameof(EcsPersistingFloatTestData2)), ref array2FloatInfoRef.InfoRef.Value, out _);
            var array5IntInfoRef = CreateFakeSceneInfoRef<EcsPersistingTestData5>(Allocator.Temp, entityAmount3);
            Entity array5IntContainer = QuickSaveAPI.CreateInitialSceneContainer(EntityManager, UnityEngine.Hash128.Compute(nameof(EcsPersistingTestData5)), ref array5IntInfoRef.InfoRef.Value, out _);

            var query1 = EntityManager.CreateEntityQuery(typeof(EcsPersistingTestData), typeof(LocalIndexInContainer));
            var query2 = EntityManager.CreateEntityQuery(typeof(EcsPersistingFloatTestData2), typeof(LocalIndexInContainer));
            var query3 = EntityManager.CreateEntityQuery(typeof(EcsPersistingTestData5), typeof(LocalIndexInContainer));

            NativeArray<Entity> entities = EntityManager.CreateEntityQuery(typeof(LocalIndexInContainer)).ToEntityArray(Allocator.Temp);
            foreach (Entity entity in entities)
            {
                int index = EntityManager.GetComponentData<LocalIndexInContainer>(entity).LocalIndex;
                if (index < 3)
                {
                    EntityManager.DestroyEntity(entity);
                }
            }
            
            // Grab these after creation because of structural changes happening after CreateInitialSceneContainer
            var array1IntData = EntityManager.GetBuffer<QuickSaveDataContainer.Data>(array1IntContainer);
            var array2FloatData = EntityManager.GetBuffer<QuickSaveDataContainer.Data>(array2FloatContainer);
            var array5IntData = EntityManager.GetBuffer<QuickSaveDataContainer.Data>(array5IntContainer);
            
            entities = EntityManager.CreateEntityQuery(typeof(LocalIndexInContainer)).ToEntityArray(Allocator.Temp);
            foreach (Entity entity in entities)
            {
                int index = EntityManager.GetComponentData<LocalIndexInContainer>(entity).LocalIndex;
                if (EntityManager.HasComponent<EcsPersistingTestData>(entity))
                {
                    QuickSaveMetaData* metaDataPtr = (QuickSaveMetaData*)((byte*) array1IntData.GetUnsafePtr() + index * (UnsafeUtility.SizeOf<EcsPersistingTestData>() + QuickSaveMetaData.SizeOfStruct));
                    *metaDataPtr = new QuickSaveMetaData(0, 1);
                    EcsPersistingTestData* dataPtr = (EcsPersistingTestData*)(metaDataPtr + 1);
                    *dataPtr = EntityManager.GetComponentData<EcsPersistingTestData>(entity).Modified();
                }
                else if (EntityManager.HasComponent<EcsPersistingFloatTestData2>(entity))
                {
                    QuickSaveMetaData* metaDataPtr = (QuickSaveMetaData*)((byte*) array2FloatData.GetUnsafePtr() + index * (UnsafeUtility.SizeOf<EcsPersistingFloatTestData2>() + QuickSaveMetaData.SizeOfStruct));
                    *metaDataPtr = new QuickSaveMetaData(0, 1);
                    EcsPersistingFloatTestData2* dataPtr = (EcsPersistingFloatTestData2*)(metaDataPtr + 1);
                    *dataPtr = EntityManager.GetComponentData<EcsPersistingFloatTestData2>(entity).Modified();
                }
                else if (EntityManager.HasComponent<EcsPersistingTestData5>(entity))
                {
                    QuickSaveMetaData* metaDataPtr = (QuickSaveMetaData*)((byte*) array5IntData.GetUnsafePtr() + index * (UnsafeUtility.SizeOf<EcsPersistingTestData5>() + QuickSaveMetaData.SizeOfStruct));
                    *metaDataPtr = new QuickSaveMetaData(0, 1);
                    EcsPersistingTestData5* dataPtr = (EcsPersistingTestData5*)(metaDataPtr + 1);
                    *dataPtr = EntityManager.GetComponentData<EcsPersistingTestData5>(entity).Modified();
                }
            }
            
            int amount1 = query1.CalculateEntityCount();
            int amount2 = query2.CalculateEntityCount();
            int amount3 = query3.CalculateEntityCount();
            
            entities = EntityManager.CreateEntityQuery(typeof(LocalIndexInContainer)).ToEntityArray(Allocator.Temp);
            foreach (Entity entity in entities)
            {
                int index = EntityManager.GetComponentData<LocalIndexInContainer>(entity).LocalIndex;
                if (index % 2 == 1)
                {
                    if (EntityManager.HasComponent<EcsPersistingTestData>(entity))
                    {
                        EntityManager.RemoveComponent<EcsPersistingTestData>(entity);
                    }
                    else if (EntityManager.HasComponent<EcsPersistingFloatTestData2>(entity))
                    {
                        EntityManager.RemoveComponent<EcsPersistingFloatTestData2>(entity);
                    }
                    else if (EntityManager.HasComponent<EcsPersistingTestData5>(entity))
                    {
                        EntityManager.RemoveComponent<EcsPersistingTestData5>(entity);
                    }
                }
            }
            
            var bufferLookUpSystem = World.GetOrCreateSystemManaged<TestSystem>();

            // Action
            using (EntityCommandBuffer cmds = new EntityCommandBuffer(Allocator.TempJob))
            {
                array1IntData = EntityManager.GetBuffer<QuickSaveDataContainer.Data>(array1IntContainer);
                new CopyByteArrayToComponentData()
                {
                    ComponentTypeHandle = EntityManager.GetDynamicComponentTypeHandle(typeof(EcsPersistingTestData)),
                    TypeSize = UnsafeUtility.SizeOf<EcsPersistingTestData>(),
                    LocalIndexInContainerType = EntityManager.GetComponentTypeHandle<LocalIndexInContainer>(true),
                    ByteArrayLookup = bufferLookUpSystem.GetBufferLookup<QuickSaveDataContainer.Data>(true),
                    ContainerEntity = array1IntContainer,
                    SubArrayOffset = 0,
                    SubArrayByteSize = array1IntData.Length,
                    ComponentType = typeof(EcsPersistingTestData),
                    EntityType = EntityManager.GetEntityTypeHandle(),
                    Ecb = cmds.AsParallelWriter()
                }.Run(EntityManager.CreateEntityQuery(typeof(EcsTestData), typeof(LocalIndexInContainer)));
                cmds.Playback(EntityManager);
            }

            using (EntityCommandBuffer cmds = new EntityCommandBuffer(Allocator.TempJob))
            {
                array2FloatData = EntityManager.GetBuffer<QuickSaveDataContainer.Data>(array2FloatContainer);
                new CopyByteArrayToComponentData()
                {
                    ComponentTypeHandle = EntityManager.GetDynamicComponentTypeHandle(typeof(EcsPersistingFloatTestData2)),
                    TypeSize = UnsafeUtility.SizeOf<EcsPersistingFloatTestData2>(),
                    LocalIndexInContainerType = EntityManager.GetComponentTypeHandle<LocalIndexInContainer>(true),
                    ByteArrayLookup = bufferLookUpSystem.GetBufferLookup<QuickSaveDataContainer.Data>(true),
                    ContainerEntity = array2FloatContainer,
                    SubArrayOffset = 0,
                    SubArrayByteSize = array2FloatData.Length,
                    ComponentType = typeof(EcsPersistingFloatTestData2),
                    EntityType = EntityManager.GetEntityTypeHandle(),
                    Ecb = cmds.AsParallelWriter()
                }.Run(EntityManager.CreateEntityQuery(typeof(EcsTestFloatData2), typeof(LocalIndexInContainer)));
                cmds.Playback(EntityManager);
            }

            using (EntityCommandBuffer cmds = new EntityCommandBuffer(Allocator.TempJob))
            {
                array5IntData = EntityManager.GetBuffer<QuickSaveDataContainer.Data>(array5IntContainer);
                new CopyByteArrayToComponentData()
                {
                    ComponentTypeHandle = EntityManager.GetDynamicComponentTypeHandle(typeof(EcsPersistingTestData5)),
                    TypeSize = UnsafeUtility.SizeOf<EcsPersistingTestData5>(),
                    LocalIndexInContainerType = EntityManager.GetComponentTypeHandle<LocalIndexInContainer>(true),
                    ByteArrayLookup = bufferLookUpSystem.GetBufferLookup<QuickSaveDataContainer.Data>(true),
                    ContainerEntity = array5IntContainer,
                    SubArrayOffset = 0,
                    SubArrayByteSize = array5IntData.Length,
                    ComponentType = typeof(EcsPersistingTestData5),
                    EntityType = EntityManager.GetEntityTypeHandle(),
                    Ecb = cmds.AsParallelWriter()
                }.Run(EntityManager.CreateEntityQuery(typeof(EcsTestData5), typeof(LocalIndexInContainer)));
                cmds.Playback(EntityManager);
            }

            // Check Results
            entities = query1.ToEntityArray(Allocator.Temp);
            foreach (Entity entity in entities)
            {
                var newData = EntityManager.GetComponentData<EcsPersistingTestData>(entity);
                var originalData = new EcsPersistingTestData() { data = EntityManager.GetComponentData<EcsTestData>(entity)};
                Assert.True(newData.Equals(originalData.Modified()), "CopyByteArrayToComponentData:: Restored data on entity is incorrect.");
            }
            
            entities = query2.ToEntityArray(Allocator.Temp);
            foreach (Entity entity in entities)
            {
                var newData = EntityManager.GetComponentData<EcsPersistingFloatTestData2>(entity);
                var originalData = new EcsPersistingFloatTestData2() { data = EntityManager.GetComponentData<EcsTestFloatData2>(entity)};
                Assert.True(newData.Equals(originalData.Modified()), "CopyByteArrayToComponentData:: Restored data on entity is incorrect.");
            }
            
            entities = query3.ToEntityArray(Allocator.Temp);
            foreach (Entity entity in entities)
            {
                var newData = EntityManager.GetComponentData<EcsPersistingTestData5>(entity);
                var originalData = new EcsPersistingTestData5() { data = EntityManager.GetComponentData<EcsTestData5>(entity)};
                Assert.True(newData.Equals(originalData.Modified()), "CopyByteArrayToComponentData:: Restored data on entity is incorrect.");
            }

            // Check Results
            Assert.AreEqual(amount1, query1.CalculateEntityCount(), "AddMissingComponents:: Not all missing components have not been restored");
            entities = query1.ToEntityArray(Allocator.Temp);
            foreach (Entity entity in entities)
            {
                var newData = EntityManager.GetComponentData<EcsPersistingTestData>(entity);
                var originalData = new EcsPersistingTestData() { data = EntityManager.GetComponentData<EcsTestData>(entity)};
                Assert.True(newData.Equals(originalData.Modified()), "AddMissingComponents:: Restored data on entity is incorrect.");
            }
            
            Assert.AreEqual(amount2, query2.CalculateEntityCount(), "AddMissingComponents:: Not all missing components have not been restored");
            entities = query2.ToEntityArray(Allocator.Temp);
            foreach (Entity entity in entities)
            {
                var newData = EntityManager.GetComponentData<EcsPersistingFloatTestData2>(entity);
                var originalData = new EcsPersistingFloatTestData2() { data = EntityManager.GetComponentData<EcsTestFloatData2>(entity)};
                Assert.True(newData.Equals(originalData.Modified()), "AddMissingComponents:: Restored data on entity is incorrect.");
            }
            
            Assert.AreEqual(amount3, query3.CalculateEntityCount(), "AddMissingComponents:: Not all missing components have not been restored");
            entities = query3.ToEntityArray(Allocator.Temp);
            foreach (Entity entity in entities)
            {
                var newData = EntityManager.GetComponentData<EcsPersistingTestData5>(entity);
                var originalData = new EcsPersistingTestData5() { data = EntityManager.GetComponentData<EcsTestData5>(entity)};
                Assert.True(newData.Equals(originalData.Modified()), "AddMissingComponents:: Restored data on entity is incorrect.");
            }

            // Cleanup
            EntityManager.DestroyEntity(EntityManager.CreateEntityQuery(typeof(LocalIndexInContainer)));
        }

        [Test]
        public void TestApplyAndReadEmptyData()
        {
            CreateTestSettings();
            
            var entity1 = EntityManager.CreateEntity(typeof(LocalIndexInContainer), typeof(EmptyEcsTestData));
            var entity2 = EntityManager.CreateEntity(typeof(LocalIndexInContainer));
            var entity3 = EntityManager.CreateEntity(typeof(LocalIndexInContainer), typeof(EmptyEcsTestData));
            
            EntityManager.SetComponentData(entity1, new LocalIndexInContainer {LocalIndex = 0});
            EntityManager.SetComponentData(entity2, new LocalIndexInContainer {LocalIndex = 1});
            EntityManager.SetComponentData(entity3, new LocalIndexInContainer {LocalIndex = 2});
            
            var query = EntityManager.CreateEntityQuery(typeof(LocalIndexInContainer));
            
            var infoRef = CreateFakeSceneInfoRef<EmptyEcsTestData>(Allocator.Temp, 3);
            Entity container = QuickSaveAPI.CreateInitialSceneContainer(EntityManager, UnityEngine.Hash128.Compute(nameof(EmptyEcsTestData)), ref infoRef.InfoRef.Value, out var data);
            var bufferLookUpSystem = World.GetOrCreateSystemManaged<TestSystem>();
            
            // Action
            var readJob = new CopyComponentDataToByteArray()
            {
                LocalIndexInContainerType = EntityManager.GetComponentTypeHandle<LocalIndexInContainer>(true),
                ByteArrayLookup = bufferLookUpSystem.GetBufferLookup<QuickSaveDataContainer.Data>(),
                ContainerEntity = container,
                SubArrayOffset = 0,
                SubArrayByteSize = data.Length,
                TypeSize = 0,
                ComponentTypeHandle = EntityManager.GetDynamicComponentTypeHandle(typeof(EmptyEcsTestData))
            }.Schedule(query, default);
            readJob.Complete();

            EntityManager.AddComponentData(entity2, new EmptyEcsTestData());
            EntityManager.RemoveComponent<EmptyEcsTestData>(entity3);
            
            using (EntityCommandBuffer cmds = new EntityCommandBuffer(Allocator.TempJob))
            {
                data = EntityManager.GetBuffer<QuickSaveDataContainer.Data>(container);
                var removeJob = new CopyByteArrayToComponentData()
                {
                    ComponentType = typeof(EmptyEcsTestData),
                    TypeSize = TypeManager.GetTypeInfo<EmptyEcsTestData>().ElementSize,
                    LocalIndexInContainerType = EntityManager.GetComponentTypeHandle<LocalIndexInContainer>(true),
                    ByteArrayLookup = bufferLookUpSystem.GetBufferLookup<QuickSaveDataContainer.Data>(),
                    ContainerEntity = container,
                    SubArrayOffset = 0,
                    SubArrayByteSize = data.Length,
                    EntityType = EntityManager.GetEntityTypeHandle(),
                    Ecb = cmds.AsParallelWriter(),
                    ComponentTypeHandle = EntityManager.GetDynamicComponentTypeHandle(typeof(EmptyEcsTestData))
                }.Schedule(query, default);
                removeJob.Complete();
                cmds.Playback(EntityManager);
            }
            
            // Check Results
            Assert.True(EntityManager.HasComponent<EmptyEcsTestData>(entity1), "The entity does not have the component EmptyEcsTestData BUT the entity did have the component at the time of persisting.");
            Assert.False(EntityManager.HasComponent<EmptyEcsTestData>(entity2), "The entity has the component EmptyEcsTestData BUT the entity did not have the component at the time of persisting.");
            Assert.True(EntityManager.HasComponent<EmptyEcsTestData>(entity3), "The entity does not have the component EmptyEcsTestData BUT the entity did have the component at the time of persisting.");
            
            // Cleanup
            EntityManager.DestroyEntity(EntityManager.CreateEntityQuery(typeof(LocalIndexInContainer)));
        }

        internal struct EcsPersistingTestData : IComponentData, IEquatable<EcsTestData>, IEquatable<EcsPersistingTestData>
        {
            public EcsTestData data;
            public bool Equals(EcsTestData other)
            {
                return data.Equals(other);
            }

            public bool Equals(EcsPersistingTestData other)
            {
                return data.Equals(other.data);
            }
            
            public EcsPersistingTestData Modified()
            {
                return new EcsPersistingTestData()
                {
                    data = new EcsTestData(data.Value * data.Value * data.Value)
                };
            }
        }
        
        internal struct EcsPersistingFloatTestData2 : IComponentData, IEquatable<EcsTestFloatData2>, IEquatable<EcsPersistingFloatTestData2>
        {
            public EcsTestFloatData2 data;

            public bool Equals(EcsTestFloatData2 other)
            {
                return data.Equals(other);
            }

            public bool Equals(EcsPersistingFloatTestData2 other)
            {
                return data.Equals(other.data);
            }
            
            public EcsPersistingFloatTestData2 Modified()
            {
                return new EcsPersistingFloatTestData2()
                {
                    data = new EcsTestFloatData2() { Value0 = data.Value0 + 1.0f, Value1 = data.Value1 + 1.0f}
                };
            }
        }
        
        internal struct EcsPersistingTestData5 : IComponentData, IEquatable<EcsTestData5>, IEquatable<EcsPersistingTestData5>
        {
            public EcsTestData5 data;

            public bool Equals(EcsTestData5 other)
            {
                return data.Equals(other);
            }

            public bool Equals(EcsPersistingTestData5 other)
            {
                return data.Equals(other.data);
            }

            public EcsPersistingTestData5 Modified()
            {
                return new EcsPersistingTestData5()
                {
                    data = new EcsTestData5(data.Value0 * data.Value0)
                };
            }
        }
        
        private Entity CreateEntity<T, U>(int index, T value, U value2) 
            where T : unmanaged, IComponentData
            where U : unmanaged, IComponentData
        {
            var entity = EntityManager.CreateEntity(typeof(T), typeof(U), typeof(LocalIndexInContainer));
            EntityManager.SetComponentData<T>(entity, value);
            EntityManager.SetComponentData<U>(entity, value2);
            EntityManager.SetComponentData(entity, new LocalIndexInContainer() {LocalIndex = index});
            return entity;
        }

        private void CreateEntities(int count)
        {
            for (int i = 0; i != count; i++)
            {
                int zeroOneTwo = i % 3;
                int peristenceIndex = i / 3;
                switch (zeroOneTwo)
                {
                    case 0:
                        var ecsData = new EcsTestData(i);
                        CreateEntity(peristenceIndex, ecsData, new EcsPersistingTestData() { data = ecsData});
                        break;
                    case 1: 
                        var ecsData2 = new EcsTestFloatData2() {Value0 = i * 1.1111f, Value1 = i * 7.7777f};
                        CreateEntity(peristenceIndex, ecsData2, new EcsPersistingFloatTestData2() { data = ecsData2});
                        break;
                    case 2: 
                        var ecsData5 = new EcsTestData5(i * i);
                        CreateEntity(peristenceIndex, ecsData5, new EcsPersistingTestData5() { data = ecsData5});
                        break;
                    default: throw new Exception("zeroOneTwo was not 0, 1 or 2");
                }
            }
        }
    }
}
