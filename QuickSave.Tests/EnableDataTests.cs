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
    class EnableDataTests : EcsTestsFixture
    {
        [Test]
        public unsafe void TestPersistEnabled([Values(0, 1, 2, 3, 60, 400)] int total)
        {
            // Preparation
            CreateTestSettings();
            CreateEntities(total);

            int entityAmount3 = total / 3;
            int entityAmount1 = entityAmount3 + (total%3 > 0 ? 1 : 0);
            int entityAmount2 = entityAmount3 + (total%3 > 1 ? 1 : 0);
            const int maxBufferElements = 2;
            
            var compDataInfoRef = CreateFakeSceneInfoRef<TestComponent>(entityAmount1);
            Entity compDataContainer = QuickSaveAPI.CreateInitialSceneContainer(EntityManager, UnityEngine.Hash128.Compute(nameof(TestComponent)),
                ref compDataInfoRef.InfoRef.Value, out _, BlobAssetsToDisposeOnTearDown);
            var compDataNonEnableInfoRef = CreateFakeSceneInfoRef<TestComponent>(entityAmount1);
            Entity compDataNonEnableContainer = QuickSaveAPI.CreateInitialSceneContainer(EntityManager, UnityEngine.Hash128.Compute(nameof(EcsTestData)),
                ref compDataNonEnableInfoRef.InfoRef.Value, out _, BlobAssetsToDisposeOnTearDown);
            var tagDataInfoRef = CreateFakeSceneInfoRef<TestTagComponent>(entityAmount2);
            Entity tagDataContainer = QuickSaveAPI.CreateInitialSceneContainer(EntityManager, UnityEngine.Hash128.Compute(nameof(TestTagComponent)),
                ref tagDataInfoRef.InfoRef.Value, out _, BlobAssetsToDisposeOnTearDown);
            var tagDataNonEnableInfoRef = CreateFakeSceneInfoRef<EmptyEcsTestData>(entityAmount2);
            Entity tagDataNonEnableContainer = QuickSaveAPI.CreateInitialSceneContainer(EntityManager, UnityEngine.Hash128.Compute(nameof(EmptyEcsTestData)),
                ref tagDataNonEnableInfoRef.InfoRef.Value, out _, BlobAssetsToDisposeOnTearDown);
            var bufferDataInfoRef = CreateFakeSceneInfoRef<TestBufferComponent>(entityAmount2);
            Entity bufferDataContainer = QuickSaveAPI.CreateInitialSceneContainer(EntityManager, UnityEngine.Hash128.Compute(nameof(TestBufferComponent)),
                ref bufferDataInfoRef.InfoRef.Value, out _, BlobAssetsToDisposeOnTearDown);
            var bufferDataNonEnableInfoRef = CreateFakeSceneInfoRef<DynamicBufferData1>(entityAmount2);
            Entity bufferDataNonEnableContainer = QuickSaveAPI.CreateInitialSceneContainer(EntityManager, UnityEngine.Hash128.Compute(nameof(DynamicBufferData1)),
                ref bufferDataNonEnableInfoRef.InfoRef.Value, out _, BlobAssetsToDisposeOnTearDown);

            var query1 = new EntityQueryBuilder(Allocator.Temp).WithAll<TestComponent, LocalIndexInContainer>().Build(EntityManager);
            var query1NonEnable = new EntityQueryBuilder(Allocator.Temp).WithAll<EcsTestData, LocalIndexInContainer>().Build(EntityManager);
            var query2 = new EntityQueryBuilder(Allocator.Temp).WithAll<TestTagComponent, LocalIndexInContainer>().Build(EntityManager);
            var query2NonEnable = new EntityQueryBuilder(Allocator.Temp).WithAll<EmptyEcsTestData, LocalIndexInContainer>().Build(EntityManager);
            var query3 = new EntityQueryBuilder(Allocator.Temp).WithAll<TestBufferComponent, LocalIndexInContainer>().Build(EntityManager);
            var query3NonEnable = new EntityQueryBuilder(Allocator.Temp).WithAll<DynamicBufferData1, LocalIndexInContainer>().Build(EntityManager);
            
            var allEntities = new EntityQueryBuilder(Allocator.Temp).WithAll<LocalIndexInContainer>().Build(EntityManager).ToEntityArray(Allocator.Temp);
            foreach (var e in allEntities)
            {
                // Disable every component on entities with uneven arrayIndex
                int index = EntityManager.GetComponentData<LocalIndexInContainer>(e).LocalIndex;
                if ((index % 2 == 1) && EntityManager.HasComponent<TestComponent>(e))
                    EntityManager.SetComponentEnabled<TestComponent>(e, false);
                if ((index % 2 == 1) && EntityManager.HasComponent<TestTagComponent>(e))
                    EntityManager.SetComponentEnabled<TestTagComponent>(e, false);
                if ((index % 2 == 1) && EntityManager.HasComponent<TestBufferComponent>(e))
                    EntityManager.SetComponentEnabled<TestBufferComponent>(e, false);
            }
            
            var bufferLookUpSystem = World.GetOrCreateSystemManaged<TestSystem>();

            // Grab these after creation because of structural changes inside CreateInitialSceneContainer
            var compData = EntityManager.GetBuffer<QuickSaveDataContainer.Data>(compDataContainer);
            var compDataNonEnable = EntityManager.GetBuffer<QuickSaveDataContainer.Data>(compDataNonEnableContainer);
            var tagData = EntityManager.GetBuffer<QuickSaveDataContainer.Data>(tagDataContainer);
            var tagDataNonEnable = EntityManager.GetBuffer<QuickSaveDataContainer.Data>(tagDataNonEnableContainer);
            var bufferData = EntityManager.GetBuffer<QuickSaveDataContainer.Data>(bufferDataContainer);
            var bufferDataNonEnable = EntityManager.GetBuffer<QuickSaveDataContainer.Data>(bufferDataNonEnableContainer);

            // Action
            var job1 = new CopyComponentDataToByteArray()
            {
                ComponentTypeHandle = EntityManager.GetDynamicComponentTypeHandle(typeof(TestComponent)),
                TypeSize = UnsafeUtility.SizeOf<TestComponent>(),
                LocalIndexInContainerType = EntityManager.GetComponentTypeHandle<LocalIndexInContainer>(true),
                ByteArrayLookup = bufferLookUpSystem.GetBufferLookup<QuickSaveDataContainer.Data>(),
                ContainerEntity = compDataContainer,
                SubArrayOffset = 0,
                SubArrayByteSize = compData.Length
            }.Schedule(query1, default);
            
            var job2 = new CopyComponentDataToByteArray()
            {
                ComponentTypeHandle = EntityManager.GetDynamicComponentTypeHandle(typeof(TestTagComponent)),
                TypeSize = 0,
                LocalIndexInContainerType = EntityManager.GetComponentTypeHandle<LocalIndexInContainer>(true),
                ByteArrayLookup = bufferLookUpSystem.GetBufferLookup<QuickSaveDataContainer.Data>(),
                ContainerEntity = tagDataContainer,
                SubArrayOffset = 0,
                SubArrayByteSize = tagData.Length
            }.Schedule(query2, default);
            
            var job3 = new CopyBufferElementsToByteArray()
            {
                BufferTypeHandle = EntityManager.GetDynamicComponentTypeHandle(typeof(TestBufferComponent)),
                MaxElements = maxBufferElements,
                LocalIndexInContainerType = EntityManager.GetComponentTypeHandle<LocalIndexInContainer>(true),
                ByteArrayLookup = bufferLookUpSystem.GetBufferLookup<QuickSaveDataContainer.Data>(),
                ContainerEntity = bufferDataContainer,
                SubArrayOffset = 0,
                SubArrayByteSize = bufferData.Length
            }.Schedule(query3, default);
            
            JobHandle.CombineDependencies(job1, job2, job3).Complete();
            
            job1 = new CopyComponentDataToByteArray()
            {
                ComponentTypeHandle = EntityManager.GetDynamicComponentTypeHandle(typeof(EcsTestData)),
                TypeSize = UnsafeUtility.SizeOf<EcsTestData>(),
                LocalIndexInContainerType = EntityManager.GetComponentTypeHandle<LocalIndexInContainer>(true),
                ByteArrayLookup = bufferLookUpSystem.GetBufferLookup<QuickSaveDataContainer.Data>(),
                ContainerEntity = compDataNonEnableContainer,
                SubArrayOffset = 0,
                SubArrayByteSize = compDataNonEnable.Length
            }.Schedule(query1NonEnable, default);
            
            job2 = new CopyComponentDataToByteArray()
            {
                ComponentTypeHandle = EntityManager.GetDynamicComponentTypeHandle(typeof(EmptyEcsTestData)),
                TypeSize = 0,
                LocalIndexInContainerType = EntityManager.GetComponentTypeHandle<LocalIndexInContainer>(true),
                ByteArrayLookup = bufferLookUpSystem.GetBufferLookup<QuickSaveDataContainer.Data>(),
                ContainerEntity = tagDataNonEnableContainer,
                SubArrayOffset = 0,
                SubArrayByteSize = tagDataNonEnable.Length
            }.Schedule(query2NonEnable, default);
            
            job3 = new CopyBufferElementsToByteArray()
            {
                BufferTypeHandle = EntityManager.GetDynamicComponentTypeHandle(typeof(DynamicBufferData1)),
                MaxElements = maxBufferElements,
                LocalIndexInContainerType = EntityManager.GetComponentTypeHandle<LocalIndexInContainer>(true),
                ByteArrayLookup = bufferLookUpSystem.GetBufferLookup<QuickSaveDataContainer.Data>(),
                ContainerEntity = bufferDataNonEnableContainer,
                SubArrayOffset = 0,
                SubArrayByteSize = bufferDataNonEnable.Length
            }.Schedule(query3NonEnable, default);
            
            JobHandle.CombineDependencies(job1, job2, job3).Complete();
            
            // Check Results
            var entities = query1.ToEntityArray(Allocator.Temp);
            foreach (Entity entity in entities)
            {
                var localIndexInContainer = EntityManager.GetComponentData<LocalIndexInContainer>(entity);
                int byteIndexMetaData = localIndexInContainer.LocalIndex * (UnsafeUtility.SizeOf<TestComponent>() + QuickSaveMetaData.SizeOfStruct);
                var metaData = *(QuickSaveMetaData*)((byte*)compData.GetUnsafeReadOnlyPtr() + byteIndexMetaData);
                Assert.True(metaData.Enabled == (localIndexInContainer.LocalIndex % 2 == 0), "Component its enabled state did not match the meta data!");
            }
            entities = query2.ToEntityArray(Allocator.Temp);
            foreach (Entity entity in entities)
            {
                var localIndexInContainer = EntityManager.GetComponentData<LocalIndexInContainer>(entity);
                int byteIndexMetaData = localIndexInContainer.LocalIndex * QuickSaveMetaData.SizeOfStruct;
                var metaData = *(QuickSaveMetaData*)((byte*)tagData.GetUnsafeReadOnlyPtr() + byteIndexMetaData);
                Assert.True(metaData.Enabled == (localIndexInContainer.LocalIndex % 2 == 0), "Component its enabled state did not match the meta data!");
            }
            entities = query3.ToEntityArray(Allocator.Temp);
            foreach (Entity entity in entities)
            {
                var localIndexInContainer = EntityManager.GetComponentData<LocalIndexInContainer>(entity);
                int byteIndexMetaData = localIndexInContainer.LocalIndex * (UnsafeUtility.SizeOf<TestBufferComponent>() * maxBufferElements + QuickSaveMetaData.SizeOfStruct);
                var metaData = *(QuickSaveMetaData*)((byte*)bufferData.GetUnsafeReadOnlyPtr() + byteIndexMetaData);
                Assert.True(metaData.Enabled == (localIndexInContainer.LocalIndex % 2 == 0), "Component its enabled state did not match the meta data!");
            }
            entities = query1NonEnable.ToEntityArray(Allocator.Temp);
            foreach (Entity entity in entities)
            {
                var localIndexInContainer = EntityManager.GetComponentData<LocalIndexInContainer>(entity);
                int byteIndexMetaData = localIndexInContainer.LocalIndex * (UnsafeUtility.SizeOf<EcsTestData>() + QuickSaveMetaData.SizeOfStruct);
                var metaData = *(QuickSaveMetaData*)((byte*)compDataNonEnable.GetUnsafeReadOnlyPtr() + byteIndexMetaData);
                Assert.True(metaData.Enabled, "Non IEnableableComponent component its meta data said it was disabled, it should always be enabled!");
            }
            entities = query2NonEnable.ToEntityArray(Allocator.Temp);
            foreach (Entity entity in entities)
            {
                var localIndexInContainer = EntityManager.GetComponentData<LocalIndexInContainer>(entity);
                int byteIndexMetaData = localIndexInContainer.LocalIndex * QuickSaveMetaData.SizeOfStruct;
                var metaData = *(QuickSaveMetaData*)((byte*)tagDataNonEnable.GetUnsafeReadOnlyPtr() + byteIndexMetaData);
                Assert.True(metaData.Enabled, "Non IEnableableComponent component its meta data said it was disabled, it should always be enabled!");
            }
            entities = query3NonEnable.ToEntityArray(Allocator.Temp);
            foreach (Entity entity in entities)
            {
                var localIndexInContainer = EntityManager.GetComponentData<LocalIndexInContainer>(entity);
                int byteIndexMetaData = localIndexInContainer.LocalIndex * (UnsafeUtility.SizeOf<DynamicBufferData1>() * maxBufferElements + QuickSaveMetaData.SizeOfStruct);
                var metaData = *(QuickSaveMetaData*)((byte*)bufferDataNonEnable.GetUnsafeReadOnlyPtr() + byteIndexMetaData);
                Assert.True(metaData.Enabled, "Non IEnableableComponent component its meta data said it was disabled, it should always be enabled!");
            }

            // Cleanup
            EntityManager.DestroyEntity(EntityManager.UniversalQuery);
        }

        [Test]
        public unsafe void TestApplyEnabled([Values(0, 1, 6, 61, 400)] int total)
        {
            // case 0: applying enable to existing enabled component
            // case 1: applying disable to existing enabled component
            // case 2: applying enable to existing disabled component
            // case 3: applying disable to existing disabled component
            // case 4: applying enable to newly created component
            // case 5: applying disable to newly created component
            // case 6: applying to newly destroyed component
            // case 7: applying to missing component
            
            // Preparation
            CreateTestSettings();
            CreateEntities(total);

            int entityAmount3 = total / 3;
            int entityAmount1 = entityAmount3 + (total%3 > 0 ? 1 : 0);
            int entityAmount2 = entityAmount3 + (total%3 > 1 ? 1 : 0);
            const int maxBufferElements = 2;

            var compDataInfoRef = CreateFakeSceneInfoRef<TestComponent>(entityAmount1);
            Entity compDataContainer = QuickSaveAPI.CreateInitialSceneContainer(EntityManager, UnityEngine.Hash128.Compute(nameof(TestComponent)),
                ref compDataInfoRef.InfoRef.Value, out var compData, BlobAssetsToDisposeOnTearDown);
            var containerIdCompData = new SceneSection {SceneGUID = UnityEngine.Hash128.Compute(nameof(TestComponent))};
            for (int i = 0; i < entityAmount1; i++)
            {
                int amount = Apply_ShouldComponentBeMissingAfter(new LocalIndexInContainer {LocalIndex = i}) ? 0 : 1;
                bool enabled = Apply_ShouldComponentBeEnabledAfter(new LocalIndexInContainer {LocalIndex = i});
                var metaData = new QuickSaveMetaData(0, (ushort)amount, enabled);
                UnsafeUtility.WriteArrayElementWithStride(compData.GetUnsafePtr(), i, UnsafeUtility.SizeOf<TestComponent>() + QuickSaveMetaData.SizeOfStruct, metaData);
            }
            
            var tagDataInfoRef = CreateFakeSceneInfoRef<TestTagComponent>(entityAmount2);
            Entity tagDataContainer = QuickSaveAPI.CreateInitialSceneContainer(EntityManager, UnityEngine.Hash128.Compute(nameof(TestTagComponent)),
                ref tagDataInfoRef.InfoRef.Value, out var tagData, BlobAssetsToDisposeOnTearDown);
            var containerIdTagData = new SceneSection {SceneGUID = UnityEngine.Hash128.Compute(nameof(TestTagComponent))};
            for (int i = 0; i < entityAmount2; i++)
            {
                int amount = Apply_ShouldComponentBeMissingAfter(new LocalIndexInContainer {LocalIndex = i}) ? 0 : 1;
                bool enabled = Apply_ShouldComponentBeEnabledAfter(new LocalIndexInContainer {LocalIndex = i});
                var metaData = new QuickSaveMetaData(0, (ushort)amount, enabled);
                UnsafeUtility.WriteArrayElementWithStride(tagData.GetUnsafePtr(), i, 0 + QuickSaveMetaData.SizeOfStruct, metaData);
            }
            
            var bufferDataInfoRef = CreateFakeSceneInfoRef<TestBufferComponent>(entityAmount2);
            Entity bufferDataContainer = QuickSaveAPI.CreateInitialSceneContainer(EntityManager, UnityEngine.Hash128.Compute(nameof(TestBufferComponent)),
                ref bufferDataInfoRef.InfoRef.Value, out var bufferData, BlobAssetsToDisposeOnTearDown);
            var containerIdBufferData = new SceneSection {SceneGUID = UnityEngine.Hash128.Compute(nameof(TestBufferComponent))};
            for (int i = 0; i < entityAmount3; i++)
            {
                int amount = Apply_ShouldComponentBeMissingAfter(new LocalIndexInContainer {LocalIndex = i}) ? 0 : 1;
                bool enabled = Apply_ShouldComponentBeEnabledAfter(new LocalIndexInContainer {LocalIndex = i});
                var metaData = new QuickSaveMetaData(0, (ushort)amount, enabled);
                UnsafeUtility.WriteArrayElementWithStride(bufferData.GetUnsafePtr(), i, (UnsafeUtility.SizeOf<TestBufferComponent>() * maxBufferElements + QuickSaveMetaData.SizeOfStruct), metaData);
            }
            
            var allEntities = new EntityQueryBuilder(Allocator.Temp).WithAll<LocalIndexInContainer>().Build(EntityManager).ToEntityArray(Allocator.Temp);
            foreach (var e in allEntities)
            {
                LocalIndexInContainer localIndexInContainer = EntityManager.GetComponentData<LocalIndexInContainer>(e);

                bool enabled = Apply_ShouldComponentBeEnabledBefore(localIndexInContainer);
                if (EntityManager.HasComponent<TestComponent>(e))
                {
                    EntityManager.AddSharedComponent(e, containerIdCompData);
                    EntityManager.SetComponentEnabled<TestComponent>(e, enabled);
                }
                if (EntityManager.HasComponent<TestTagComponent>(e))
                {
                    EntityManager.AddSharedComponent(e, containerIdTagData);
                    EntityManager.SetComponentEnabled<TestTagComponent>(e, enabled);
                }
                if (EntityManager.HasComponent<TestBufferComponent>(e))
                {
                    EntityManager.AddSharedComponent(e, containerIdBufferData);
                    EntityManager.SetComponentEnabled<TestBufferComponent>(e, enabled);
                }
                
                if (Apply_ShouldComponentBeMissingBefore(localIndexInContainer))
                {
                    EntityManager.RemoveComponent<TestComponent>(e);
                    EntityManager.RemoveComponent<TestTagComponent>(e);
                }
            }
            
            var query = new EntityQueryBuilder(Allocator.Temp).WithAll<LocalIndexInContainer, SceneSection>().Build(EntityManager);
            EntityCommandBuffer cmds = new EntityCommandBuffer(Allocator.TempJob);
            EntityCommandBuffer.ParallelWriter cmdsParallel = cmds.AsParallelWriter();
            
            var bufferLookUpSystem = World.GetOrCreateSystemManaged<TestSystem>();
            
            // Update these because of structural changes
            compData = EntityManager.GetBuffer<QuickSaveDataContainer.Data>(compDataContainer);
            tagData = EntityManager.GetBuffer<QuickSaveDataContainer.Data>(tagDataContainer);
            bufferData = EntityManager.GetBuffer<QuickSaveDataContainer.Data>(bufferDataContainer);
            
            // Action
            query.SetSharedComponentFilter(containerIdCompData);
            var job1 = new CopyByteArrayToComponentData()
            {
                ComponentTypeHandle = EntityManager.GetDynamicComponentTypeHandle(typeof(TestComponent)),
                ComponentType = typeof(TestComponent),
                TypeSize = UnsafeUtility.SizeOf<TestComponent>(),
                LocalIndexInContainerType = EntityManager.GetComponentTypeHandle<LocalIndexInContainer>(true),
                Ecb = cmdsParallel,
                EntityType = EntityManager.GetEntityTypeHandle(),
                ByteArrayLookup = bufferLookUpSystem.GetBufferLookup<QuickSaveDataContainer.Data>(true),
                ContainerEntity = compDataContainer,
                SubArrayOffset = 0,
                SubArrayByteSize = compData.Length
            }.Schedule(query, default);
            
            query.SetSharedComponentFilter(containerIdTagData);
            var job2 = new CopyByteArrayToComponentData()
            {
                ComponentTypeHandle = EntityManager.GetDynamicComponentTypeHandle(typeof(TestTagComponent)),
                ComponentType = typeof(TestTagComponent),
                TypeSize = 0,
                LocalIndexInContainerType = EntityManager.GetComponentTypeHandle<LocalIndexInContainer>(true),
                Ecb = cmdsParallel,
                EntityType = EntityManager.GetEntityTypeHandle(),
                ByteArrayLookup = bufferLookUpSystem.GetBufferLookup<QuickSaveDataContainer.Data>(true),
                ContainerEntity = tagDataContainer,
                SubArrayOffset = 0,
                SubArrayByteSize = tagData.Length
            }.Schedule(query, job1);
            
            query.SetSharedComponentFilter(containerIdBufferData);
            var job3 = new CopyByteArrayToBufferElements()
            {
                BufferTypeHandle = EntityManager.GetDynamicComponentTypeHandle(typeof(TestBufferComponent)),
                MaxElements = maxBufferElements,
                LocalIndexInContainerType = EntityManager.GetComponentTypeHandle<LocalIndexInContainer>(true),
                ByteArrayLookup = bufferLookUpSystem.GetBufferLookup<QuickSaveDataContainer.Data>(true),
                ContainerEntity = bufferDataContainer,
                SubArrayOffset = 0,
                SubArrayByteSize = bufferData.Length
            }.Schedule(query, default);
            
            JobHandle.CombineDependencies(job1, job2, job3).Complete();
            cmds.Playback(EntityManager);
            
            // Check Results
            foreach (var e in allEntities)
            {
                LocalIndexInContainer localIndexInContainer = EntityManager.GetComponentData<LocalIndexInContainer>(e);
                
                if (!EntityManager.HasComponent<TestBufferComponent>(e))
                {
                    // check removed/added (adding/removing buffers is not supported)
                    bool compMissingExpectedValue = Apply_ShouldComponentBeMissingAfter(localIndexInContainer);
                    bool compsMissing = !EntityManager.HasComponent<TestComponent>(e) && !EntityManager.HasComponent<TestTagComponent>(e);
                    if (compsMissing)
                        Assert.True(compMissingExpectedValue == compsMissing, $"Expected components to still be on {e}(LocalIndex={localIndexInContainer.LocalIndex}) but it doesn't have one.");
                    else
                        Assert.True(compMissingExpectedValue == compsMissing, $"Expected components to be removed on {e}(LocalIndex={localIndexInContainer.LocalIndex}) but it still has 'em.");
                }

                // check enabled/disabled
                bool compEnabledExpectedValue = Apply_ShouldComponentBeEnabledAfter(localIndexInContainer);
                if (EntityManager.HasComponent<TestComponent>(e))
                    Assert.True(EntityManager.IsComponentEnabled<TestComponent>(e) == compEnabledExpectedValue, $"Expected {nameof(TestComponent)} to be enabled={compEnabledExpectedValue} on {e}(LocalIndex={localIndexInContainer.LocalIndex}), but it was enabled={EntityManager.IsComponentEnabled<TestComponent>(e)}");
                if (EntityManager.HasComponent<TestTagComponent>(e))
                    Assert.True(EntityManager.IsComponentEnabled<TestTagComponent>(e) == compEnabledExpectedValue, $"Expected {nameof(TestTagComponent)} to be enabled={compEnabledExpectedValue} on {e}(LocalIndex={localIndexInContainer.LocalIndex}), but it was enabled={EntityManager.IsComponentEnabled<TestTagComponent>(e)}");
                if (EntityManager.HasComponent<TestBufferComponent>(e))
                    Assert.True(EntityManager.IsComponentEnabled<TestBufferComponent>(e) == compEnabledExpectedValue, $"Expected {nameof(TestBufferComponent)} to be enabled={compEnabledExpectedValue} on {e}(LocalIndex={localIndexInContainer.LocalIndex}), but it was enabled={EntityManager.IsComponentEnabled<TestBufferComponent>(e)}");
            }
            
            // Cleanup
            cmds.Dispose();
            EntityManager.DestroyEntity(EntityManager.UniversalQuery);
        }
        
        private static bool Apply_ShouldComponentBeMissingBefore(LocalIndexInContainer localIndexInContainer)
        {
            int i = localIndexInContainer.LocalIndex % 8;
            switch (i)
            {
                case 4: // case: applying enable to newly created component
                case 5: // case: applying disable to newly created component
                case 7: // case: applying to missing component
                    return true;
                default:
                    return false;
            }
        }
        
        private static bool Apply_ShouldComponentBeMissingAfter(LocalIndexInContainer localIndexInContainer)
        {
            int i = localIndexInContainer.LocalIndex % 8;
            switch (i)
            {
                case 6: // case: applying to newly destroyed component
                case 7: // case: applying to missing component
                    return true;
                default:
                    return false;
            }
        }
        
        private static bool Apply_ShouldComponentBeEnabledBefore(LocalIndexInContainer localIndexInContainer)
        {
            int i = localIndexInContainer.LocalIndex % 8;
            switch (i)
            {
                case 0: // case: applying enable to existing enabled component
                case 1: // case: applying disable to existing enabled component
                case 6: // case: applying to newly destroyed component
                case 7: // case: applying to missing component
                    return true;
                default:
                    return false;
            }
        }

        private static bool Apply_ShouldComponentBeEnabledAfter(LocalIndexInContainer localIndexInContainer)
        {
            int i = localIndexInContainer.LocalIndex % 8;
            switch (i)
            {
                case 0: // case: applying enable to existing enabled component
                case 2: // case: applying enable to existing disabled component
                case 4: // case: applying enable to newly created component
                    return true;
                default:
                    return false;
            }
        }

        internal struct TestComponent : IComponentData, IEnableableComponent
        {
            public EcsTestData Data;
        }
        
        internal struct TestTagComponent : IComponentData, IEnableableComponent
        {
        }
        
        internal struct TestBufferComponent : IBufferElementData, IEnableableComponent
        {
            public DynamicBufferData1 Data;
        }
        
        private void CreateEntities(int count)
        {
            for (int i = 0; i != count; i++)
            {
                int zeroOneTwo = i % 3;
                int peristenceIndex = i / 3;
                Entity entity;
                switch (zeroOneTwo)
                {
                    case 0:
                        entity = EntityManager.CreateEntity(typeof(EcsTestData), typeof(TestComponent), typeof(LocalIndexInContainer));
                        EntityManager.SetComponentData(entity, new LocalIndexInContainer {LocalIndex = peristenceIndex});
                        break;
                    case 1: 
                        entity = EntityManager.CreateEntity(typeof(EmptyEcsTestData), typeof(TestTagComponent), typeof(LocalIndexInContainer));
                        EntityManager.SetComponentData(entity, new LocalIndexInContainer {LocalIndex = peristenceIndex});
                        break;
                    case 2: 
                        entity = EntityManager.CreateEntity(typeof(DynamicBufferData1), typeof(TestBufferComponent), typeof(LocalIndexInContainer));
                        EntityManager.SetComponentData(entity, new LocalIndexInContainer {LocalIndex = peristenceIndex});
                        break;
                    default: throw new Exception("zeroOneTwo was not 0, 1 or 2");
                }
            }
        }
    }
}
