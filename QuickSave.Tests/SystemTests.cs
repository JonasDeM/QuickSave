// Author: Jonas De Maeseneer

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using QuickSave.Baking;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using Hash128 = UnityEngine.Hash128;
using Object = UnityEngine.Object;

namespace QuickSave.Tests
{
    [TestFixture]
    class SystemTests : EcsTestsFixture
    {
        [Test]
        public void TestComponentBaking([Values(0, 1, 2, 3, 60, 61, 400)] int total)
        {
            var settingsAsset = CreateTestSettings();
            
            List<(QuickSaveAuthoring, Entity)> toBake = CreateGameObjectsAndPrimaryEntitiesForBaking(total, true, false, false);

            foreach (var tuple in toBake)
            {
                BakeForTests(tuple.Item1, BakingEntityManager, tuple.Item2, Hash128.Compute("TestBaking"), settingsAsset);
            }
            var bakingSystem = BakingWorld.GetOrCreateSystemManaged<QuickSaveBakingSystem>();
            bakingSystem.Update();

            VerifyBakedEntities(toBake.Select((tuple) => tuple.Item1).ToList(), 1);
            BakingEntityManager.DestroyEntity(BakingEntityManager.UniversalQuery);
        }
        
        [Test]
        public void TestTagBaking([Values(0, 1, 2, 3, 60, 63, 400)] int total)
        {
            var settingsAsset = CreateTestSettings();
            
            List<(QuickSaveAuthoring, Entity)> toBake = CreateGameObjectsAndPrimaryEntitiesForBaking(total, false, true, false);
            
            foreach (var tuple in toBake)
            {
                BakeForTests(tuple.Item1, BakingEntityManager, tuple.Item2, Hash128.Compute("TestBaking"), settingsAsset);
            }
            var bakingSystem = BakingWorld.GetOrCreateSystemManaged<QuickSaveBakingSystem>();
            bakingSystem.Update();

            VerifyBakedEntities(toBake.Select((tuple) => tuple.Item1).ToList(), 1);
            BakingEntityManager.DestroyEntity(BakingEntityManager.UniversalQuery);
        }
        
        [Test]
        public void TestBufferBaking([Values(0, 1, 2, 3, 60, 65, 400)] int total)
        {
            var settingsAsset = CreateTestSettings();
            
            List<(QuickSaveAuthoring, Entity)> toBake = CreateGameObjectsAndPrimaryEntitiesForBaking(total, false, false, true);

            foreach (var tuple in toBake)
            {
                BakeForTests(tuple.Item1, BakingEntityManager, tuple.Item2, Hash128.Compute("TestBaking"), settingsAsset);
            }
            var bakingSystem = BakingWorld.GetOrCreateSystemManaged<QuickSaveBakingSystem>();
            bakingSystem.Update();

            VerifyBakedEntities(toBake.Select((tuple) => tuple.Item1).ToList(), 1);
            
            BakingEntityManager.DestroyEntity(BakingEntityManager.UniversalQuery);
        }

        [Test]
        public void TestCombinedBaking([Values(0, 1, 2, 3, 60, 100)] int total)
        {            
            var settingsAsset = CreateTestSettings();
            
            List<(QuickSaveAuthoring, Entity)> toBake = new List<(QuickSaveAuthoring, Entity)>();
            toBake.AddRange(CreateGameObjectsAndPrimaryEntitiesForBaking(total, false, true, true));
            toBake.AddRange(CreateGameObjectsAndPrimaryEntitiesForBaking(total, false, false, true));
            toBake.AddRange(CreateGameObjectsAndPrimaryEntitiesForBaking(total, true, false, true));
            toBake.AddRange(CreateGameObjectsAndPrimaryEntitiesForBaking(total, true, false, false));
            toBake.AddRange(CreateGameObjectsAndPrimaryEntitiesForBaking(total, false, true, false));
            toBake.AddRange(CreateGameObjectsAndPrimaryEntitiesForBaking(total, true, true, true));

            foreach (var tuple in toBake)
            {
                BakeForTests(tuple.Item1, BakingEntityManager, tuple.Item2, Hash128.Compute("TestBaking"), settingsAsset);
            }
            var bakingSystem = BakingWorld.GetOrCreateSystemManaged<QuickSaveBakingSystem>();
            bakingSystem.Update();

            VerifyBakedEntities(toBake.Select((tuple) => tuple.Item1).ToList(), total == 0 ? 1 : 6);

            BakingEntityManager.DestroyEntity(BakingEntityManager.UniversalQuery);
        }
        
        [Test]
        public void TestMissingTypeBaking([Values(0, 1, 2, 3, 60, 100)] int total)
        {            
            var settingsAsset = CreateTestSettings(removeFirst: true);
            
            List<(QuickSaveAuthoring, Entity)> toBake = new List<(QuickSaveAuthoring, Entity)>();
            toBake.AddRange(CreateGameObjectsAndPrimaryEntitiesForBaking(total, true, true, true));

            foreach (var tuple in toBake)
            {
                BakeForTests(tuple.Item1, BakingEntityManager, tuple.Item2, Hash128.Compute("TestBaking"), settingsAsset);
            }
            var bakingSystem = BakingWorld.GetOrCreateSystemManaged<QuickSaveBakingSystem>();
            bakingSystem.Update();

            VerifyBakedEntities(toBake.Select((tuple) => tuple.Item1).ToList(), 1);
            BakingEntityManager.DestroyEntity(BakingEntityManager.UniversalQuery);
        }
        
        private void VerifyBakedEntities(List<QuickSaveAuthoring> convertedObjects, int amountExpectedQuickSaveArchetypes)
        {
            Assert.True(convertedObjects.Count  == BakingEntityManager.CreateEntityQuery(typeof(LocalIndexInContainer)).CalculateEntityCount(), "Not all game objects were converted!");
            
            // Check components are added & arrayindex is unique per persistencyarchetype
            Dictionary<int, NativeHashSet<int>> uniqueIndicesPerArchetype = new Dictionary<int, NativeHashSet<int>>();

            NativeArray<Entity> entities = BakingEntityManager.CreateEntityQuery(typeof(LocalIndexInContainer)).ToEntityArray(Allocator.Temp);
            foreach (Entity e in entities)
            {
                LocalIndexInContainer localIndexInContainer = BakingEntityManager.GetComponentData<LocalIndexInContainer>(e);
                Assert.True(BakingEntityManager.HasComponent<SceneSection>(e), $"{BakingEntityManager.GetName(e)} did not have a {nameof(SceneSection)} component.");
                Assert.True(BakingEntityManager.GetSharedComponent<SceneSection>(e).SceneGUID != default, $"{BakingEntityManager.GetName(e)} did not have a valid value in the {nameof(SceneSection)} component.");
                Assert.True(BakingEntityManager.HasComponent<QuickSaveArchetypeIndexInContainer>(e), $"{BakingEntityManager.GetName(e)} did not have a {nameof(QuickSaveArchetypeIndexInContainer)} component.");
                var sharedComp = BakingEntityManager.GetSharedComponent<QuickSaveArchetypeIndexInContainer>(e);
                Assert.True(sharedComp.IndexInContainer != ushort.MaxValue, $"{BakingEntityManager.GetName(e)} did not have a valid value in the {nameof(QuickSaveArchetypeIndexInContainer)} component.");
                
                if (!uniqueIndicesPerArchetype.ContainsKey(sharedComp.IndexInContainer))
                {
                    uniqueIndicesPerArchetype.Add(sharedComp.IndexInContainer, new NativeHashSet<int>(4, Allocator.Temp));
                }
                
                int index = localIndexInContainer.LocalIndex;
                Assert.False(uniqueIndicesPerArchetype[sharedComp.IndexInContainer].Contains(index),  $"{BakingEntityManager.GetName(e)} had the same arrayindex as another entity '{index}'.");
                Assert.True(index >= 0,  $"{BakingEntityManager.GetName(e)} had an invalid arrayindex '{index}'.");
                uniqueIndicesPerArchetype[sharedComp.IndexInContainer].Add(index);
            }
            
            // Verify SceneInfo entity
            if (uniqueIndicesPerArchetype.Count > 0)
            {
                var quickSaveSceneInfoRef = BakingEntityManager.CreateEntityQuery(typeof(QuickSaveSceneInfoRef)).GetSingleton<QuickSaveSceneInfoRef>();
                Assert.True(quickSaveSceneInfoRef.InfoRef.IsCreated);
                Assert.NotZero(quickSaveSceneInfoRef.InfoRef.Value.DataLayoutHash);
                Assert.NotZero(quickSaveSceneInfoRef.InfoRef.Value.AllUniqueTypeHandles.Length);
                Assert.NotZero(quickSaveSceneInfoRef.InfoRef.Value.QuickSaveArchetypesInScene.Length);
            }
            
            // Check PersistableTypeCombinationHash values
            BakingEntityManager.GetAllUniqueSharedComponents(out NativeList<QuickSaveArchetypeIndexInContainer> allSharedCompValues, Allocator.Temp);
            Assert.AreEqual(amountExpectedQuickSaveArchetypes, allSharedCompValues.Length, $"Expected {amountExpectedQuickSaveArchetypes} different values!");
            
            // Clean
            foreach (NativeHashSet<int> value in uniqueIndicesPerArchetype.Values)
            {
                value.Dispose();
            }
        }

        [Test]
        public void TestSceneLoad([Values(1,2,3,10)] int total)
        {
            var settingsAsset = CreateTestSettings();

            // Load SubScenes
            for (int i = 0; i < total; i++)
            {
                Unity.Entities.Hash128 sceneGUID = Hash128.Compute(i);
                LoadFakeSubScene(sceneGUID, i+10, settingsAsset);
            }
            
            var quickSaveSceneSystem = World.GetOrCreateSystem<QuickSaveSceneSystem>();
            quickSaveSceneSystem.Update(World.Unmanaged);

            var containerEntities = new EntityQueryBuilder(Allocator.Temp).WithAll<QuickSaveDataContainer>().Build(EntityManager).ToEntityArray(Allocator.Temp);
            Assert.AreEqual(total, containerEntities.Length, "Expected the amount of loaded scenes being equal to the amount of containers!");
            NativeHashMap<Hash128, Entity> containersByGUID = new NativeHashMap<Hash128, Entity>(10, Allocator.Temp);
            foreach (var containerEntity in containerEntities)
            {
                containersByGUID.Add(EntityManager.GetComponentData<QuickSaveDataContainer>(containerEntity).GUID, containerEntity);
            }
            
            for (int i = 0; i < total; i++)
            {
                int entitiesInScene = (i + 10) * 6;
                Unity.Entities.Hash128 sceneGUID = Hash128.Compute(i);
                
                // Since BeginFramePersistencySystem hasn't run, this data will be uninitialized.
                // We're only interested if the container exists & if it's the right size
                Assert.True(containersByGUID.ContainsKey(sceneGUID), $"Expected the subscene to have a {nameof(QuickSaveDataContainer)}.");

                var container = EntityManager.GetComponentData<QuickSaveDataContainer>(containersByGUID[sceneGUID]);
                Assert.True(container.InitialContainer == containersByGUID[sceneGUID], $"Expected the {nameof(QuickSaveSceneSystem)} to create a container that has itself set as the Initial Container (being the Initial Container).");
                Assert.AreEqual(entitiesInScene, container.EntityCapacity, 
                    $"LoadFakeSubScene created {entitiesInScene} entities, but the container reports {container.EntityCapacity}.");
                Assert.NotZero(container.DataLayoutHash);
                Assert.True(container.GUID != default);
                
                Assert.True(EntityManager.HasBuffer<QuickSaveDataContainer.Data>(containersByGUID[sceneGUID]), $"Expected the container to have a {nameof(QuickSaveDataContainer.Data)} dynamicbuffer.");
                Assert.True(EntityManager.HasBuffer<DataTransferRequest>(containersByGUID[sceneGUID]), $"Expected the container to have a {nameof(DataTransferRequest)} dynamicbuffer.");
                
                Assert.True(EntityManager.HasBuffer<QuickSaveArchetypeDataLayout>(containersByGUID[sceneGUID]), $"Expected the container to have a {nameof(QuickSaveArchetypeDataLayout)} dynamicbuffer.");
                var dataLayouts = EntityManager.GetBuffer<QuickSaveArchetypeDataLayout>(containersByGUID[sceneGUID]);
                Assert.AreEqual(6, dataLayouts.Length, 
                    $"LoadFakeSubScene creates entities with 6 different QuickSaveArchetypes so we expect 6 different data layouts in the container, but it reports {dataLayouts.Length}");

                int entitiesInContainer = 0;
                foreach (var dataLayout in dataLayouts)
                {
                    entitiesInContainer += dataLayout.Amount;
                }
                Assert.AreEqual(entitiesInScene, entitiesInContainer, 
                    $"LoadFakeSubScene created {entitiesInScene} entities, but the datalayouts reports {entitiesInContainer}.");
            }
            EntityManager.DestroyEntity(EntityManager.UniversalQuery);
        }
        
        [Test]
        public void TestPersistAndApply([Values(1,2,3,10)] int total, [Values(false, true)] bool groupedJobs)
        {
            var settingsAsset = CreateTestSettings(groupedJobs);
            Assert.True(groupedJobs == QuickSaveSettings.UseGroupedJobs());
            
            QuickSaveBeginFrameSystem quickSaveBeginFrameSystem = World.GetOrCreateSystemManaged<QuickSaveBeginFrameSystem>();
            var ecbSystem = quickSaveBeginFrameSystem.EcbSystem;

            // Load SubScenes
            for (int i = 0; i < total; i++)
            {
                Unity.Entities.Hash128 sceneGUID = Hash128.Compute(i);
                LoadFakeSubScene(sceneGUID, i+10, settingsAsset);
            }

            // The fake subscene entities don't have any actual test data on them yet, so add some here
            NativeArray<Entity> entities = EntityManager.CreateEntityQuery(typeof(LocalIndexInContainer)).ToEntityArray(Allocator.Temp);
            foreach (Entity e in entities)
            {                
                LocalIndexInContainer localIndexInContainer = EntityManager.GetComponentData<LocalIndexInContainer>(e);
                // tracking add/remove of bufferdata isn't supported so we add all of them
                DynamicBuffer<DynamicBufferData1> buffer1 = EntityManager.AddBuffer<DynamicBufferData1>(e);
                buffer1.Add(new DynamicBufferData1(){Value = localIndexInContainer.LocalIndex});
                DynamicBuffer<DynamicBufferData2> buffer2 = EntityManager.AddBuffer<DynamicBufferData2>(e);
                buffer2.Add(new DynamicBufferData2(){Value = localIndexInContainer.LocalIndex});
                DynamicBuffer<DynamicBufferData3> buffer3 = EntityManager.AddBuffer<DynamicBufferData3>(e);
                buffer3.Add(new DynamicBufferData3(){Value = (byte)localIndexInContainer.LocalIndex});
                EntityManager.SetComponentEnabled<DynamicBufferData3>(e, localIndexInContainer.LocalIndex % 2 == 0); // disable uneven
                
                // Add different components
                if (localIndexInContainer.LocalIndex % 2 == 0)
                {
                    EntityManager.AddComponentData(e, new EcsTestData(localIndexInContainer.LocalIndex));
                }
                else
                {
                    EntityManager.AddComponent<EmptyEcsTestData>(e);
                }
            };
            
            var quickSaveSceneSystem = World.GetOrCreateSystem<QuickSaveSceneSystem>();
            quickSaveSceneSystem.Update(World.Unmanaged);
            quickSaveBeginFrameSystem.Update();
            ecbSystem.Update();
            
            var containerEntities = new EntityQueryBuilder(Allocator.Temp).WithAll<QuickSaveDataContainer>().Build(EntityManager).ToEntityArray(Allocator.Temp);
            Assert.AreEqual(total, containerEntities.Length, "Expected the amount of loaded scenes being equal to the amount of containers!");
            NativeHashMap<Hash128, Entity> containersByGUID = new NativeHashMap<Hash128, Entity>(10, Allocator.Temp);
            foreach (var containerEntity in containerEntities)
            {
                containersByGUID.Add(EntityManager.GetComponentData<QuickSaveDataContainer>(containerEntity).GUID, containerEntity);
            }

            // Check if some data was written to the container
            for (int i = 0; i < total; i++)
            {
                Unity.Entities.Hash128 sceneGUID = Hash128.Compute(i);
                Assert.True(containersByGUID.ContainsKey(sceneGUID), $"Expected the subscene to have a {nameof(QuickSaveDataContainer)}.");

                var container = EntityManager.GetComponentData<QuickSaveDataContainer>(containersByGUID[sceneGUID]);
                Assert.True(container.InitialContainer == containersByGUID[sceneGUID], $"Expected the {nameof(QuickSaveSceneSystem)} to create a container that has itself set as the Initial Container (being the Initial Container).");
                Assert.True(EntityManager.HasBuffer<QuickSaveDataContainer.Data>(containersByGUID[sceneGUID]), $"Expected the container to have a {nameof(QuickSaveDataContainer.Data)} dynamicbuffer.");
                Assert.True(EntityManager.HasBuffer<DataTransferRequest>(containersByGUID[sceneGUID]), $"Expected the container to have a {nameof(DataTransferRequest)} dynamicbuffer.");
                var dataBuffer = EntityManager.GetBuffer<QuickSaveDataContainer.Data>(containersByGUID[sceneGUID]);
                
                bool hasOnlyZero = true;
                foreach (QuickSaveDataContainer.Data dataByte in dataBuffer)
                {
                    if (dataByte.Byte != 0)
                    {
                        hasOnlyZero = false;
                        break;
                    }
                }
                Assert.False(hasOnlyZero, "Every byte in the container data was 0 after initial persist.");
            }
            
            // Change entities
            entities = EntityManager.CreateEntityQuery(typeof(LocalIndexInContainer)).ToEntityArray(Allocator.Temp);
            foreach (Entity e in entities)
            {                
                LocalIndexInContainer localIndexInContainer = EntityManager.GetComponentData<LocalIndexInContainer>(e);
                DynamicBuffer<DynamicBufferData1> buffer1 = EntityManager.GetBuffer<DynamicBufferData1>(e);
                buffer1[0] = default;
                DynamicBuffer<DynamicBufferData2> buffer2 = EntityManager.GetBuffer<DynamicBufferData2>(e);
                buffer2[0] = default;
                DynamicBuffer<DynamicBufferData3> buffer3 = EntityManager.GetBuffer<DynamicBufferData3>(e);
                buffer3[0] = default;
                EntityManager.SetComponentEnabled<DynamicBufferData3>(e, localIndexInContainer.LocalIndex % 2 == 1); // disable even, enable uneven
                
                if (localIndexInContainer.LocalIndex % 2 == 0)
                {
                    EntityManager.RemoveComponent<EcsTestData>(e);
                    EntityManager.AddComponent<EmptyEcsTestData>(e);
                }
                else
                {
                    EntityManager.AddComponentData(e, new EcsTestData(localIndexInContainer.LocalIndex));
                    EntityManager.RemoveComponent<EmptyEcsTestData>(e);
                }
            }
            
            // Revert entities to initial state
            foreach (var containerEntity in containerEntities)
            {
                EntityManager.GetBuffer<DataTransferRequest>(containerEntity).Add(new DataTransferRequest
                {
                    ExecutingSystem = quickSaveBeginFrameSystem.SystemHandle,
                    RequestType = DataTransferRequest.Type.FromDataContainerToEntities
                });
            }
            quickSaveBeginFrameSystem.Update();
            ecbSystem.Update();
            
            // Test if the revert actually worked on entities that we're tracking the components
            entities = EntityManager.CreateEntityQuery(typeof(LocalIndexInContainer)).ToEntityArray(Allocator.Temp);
            foreach (Entity e in entities)
            {
                LocalIndexInContainer localIndexInContainer = EntityManager.GetComponentData<LocalIndexInContainer>(e);
                if (EntityManager.GetName(e).Contains("_B")) // this means it was tracking all buffer components
                {
                    DynamicBuffer<DynamicBufferData1> buffer1 = EntityManager.GetBuffer<DynamicBufferData1>(e);
                    Assert.True(buffer1[0].Value == localIndexInContainer.LocalIndex, "The entity was expected to be reset and have it's original buffer value (1)");
                    DynamicBuffer<DynamicBufferData2> buffer2 = EntityManager.GetBuffer<DynamicBufferData2>(e);
                    Assert.True(Math.Abs(buffer2[0].Value - localIndexInContainer.LocalIndex) < 0.001f, "The entity was expected to be reset and have it's original buffer value (2)");
                    DynamicBuffer<DynamicBufferData3> buffer3 = EntityManager.GetBuffer<DynamicBufferData3>(e);
                    Assert.True(buffer3[0].Value == (byte)localIndexInContainer.LocalIndex, "The entity was expected to be reset and have it's original buffer value (3)");

                    if (localIndexInContainer.LocalIndex % 2 == 0)
                        Assert.True(EntityManager.IsComponentEnabled<DynamicBufferData3>(e), "The entity was expected to be reset and have the component enabled. ");
                    else
                        Assert.False(EntityManager.IsComponentEnabled<DynamicBufferData3>(e), "The entity was expected to be reset and have the component disabled.");
                }
                else
                {
                    DynamicBuffer<DynamicBufferData1> buffer1 = EntityManager.GetBuffer<DynamicBufferData1>(e);
                    Assert.True(buffer1[0].Value == 0, "The entity was expected to NOT be reset and have a 0 buffer value. (1)");
                    DynamicBuffer<DynamicBufferData2> buffer2 = EntityManager.GetBuffer<DynamicBufferData2>(e);
                    Assert.True(buffer2[0].Value == 0, "The entity was expected to NOT be reset and have a 0 buffer value. (2)");
                    DynamicBuffer<DynamicBufferData3> buffer3 = EntityManager.GetBuffer<DynamicBufferData3>(e);
                    Assert.True(buffer3[0].Value == 0, "The entity was expected to NOT be reset and have a 0 buffer value. (3)");
                    
                    if (localIndexInContainer.LocalIndex % 2 == 1)
                        Assert.True(EntityManager.IsComponentEnabled<DynamicBufferData3>(e), "The entity was expected to NOT be reset and have the component enabled. ");
                    else
                        Assert.False(EntityManager.IsComponentEnabled<DynamicBufferData3>(e), "The entity was expected to NOT be reset and have the component disabled.");
                }
                
                if (localIndexInContainer.LocalIndex % 2 == 0)
                {
                    if (EntityManager.GetName(e).Contains("_C")) // this means it was tracking all standard components
                        Assert.True(EntityManager.HasComponent<EcsTestData>(e), "The entity was expected to be reset and have its original component back.");
                    else
                        Assert.False(EntityManager.HasComponent<EcsTestData>(e), "The entity was expected to NOT be reset and NOT have its original component back.");

                    if (EntityManager.GetName(e).Contains("_T")) // this means it was tracking all tag components
                        Assert.False(EntityManager.HasComponent<EmptyEcsTestData>(e), "The entity was expected to be reset and have the new TAG component removed.");
                    else
                        Assert.True(EntityManager.HasComponent<EmptyEcsTestData>(e), "The entity was expected to NOT be reset and NOT have the new TAG component removed. ");
                }
                else
                {
                    if (EntityManager.GetName(e).Contains("_C")) // this means it was tracking all standard components
                        Assert.False(EntityManager.HasComponent<EcsTestData>(e), "The entity was expected to be reset and have the new component removed.");
                    else
                        Assert.True(EntityManager.HasComponent<EcsTestData>(e), "The entity was expected to NOT be reset and NOT have the new component removed.");

                    if (EntityManager.GetName(e).Contains("_T")) // this means it was tracking all tag components
                        Assert.True(EntityManager.HasComponent<EmptyEcsTestData>(e), "The entity was expected to be reset and have its original TAG component back.");
                    else
                        Assert.False(EntityManager.HasComponent<EmptyEcsTestData>(e), "The entity was expected to NOT be reset and NOT have its original TAG component back.");
                }
            }
            EntityManager.DestroyEntity(EntityManager.UniversalQuery);
        }

        private void LoadFakeSubScene(Unity.Entities.Hash128 sceneGUID, int amountPerArchetype, QuickSaveSettingsAsset settingsAsset)
        {
            List<(QuickSaveAuthoring, Entity)> toBake = new List<(QuickSaveAuthoring, Entity)>();
            toBake.AddRange(CreateGameObjectsAndPrimaryEntitiesForBaking(amountPerArchetype, false, true, true));
            toBake.AddRange(CreateGameObjectsAndPrimaryEntitiesForBaking(amountPerArchetype, false, false, true));
            toBake.AddRange(CreateGameObjectsAndPrimaryEntitiesForBaking(amountPerArchetype, true, false, true));
            toBake.AddRange(CreateGameObjectsAndPrimaryEntitiesForBaking(amountPerArchetype, true, false, false));
            toBake.AddRange(CreateGameObjectsAndPrimaryEntitiesForBaking(amountPerArchetype, false, true, false));
            toBake.AddRange(CreateGameObjectsAndPrimaryEntitiesForBaking(amountPerArchetype, true, true, true));

            foreach (var tuple in toBake)
            {
                BakeForTests(tuple.Item1, BakingEntityManager, tuple.Item2, sceneGUID, settingsAsset);
            }
            var bakingSystem = BakingWorld.GetOrCreateSystemManaged<QuickSaveBakingSystem>();
            bakingSystem.Update();

            VerifyBakedEntities(toBake.Select((tuple) => tuple.Item1).ToList(), 6);
            
            foreach ((QuickSaveAuthoring, Entity) pair in toBake)
            {
                BakingEntityManager.AddSharedComponentManaged(pair.Item2, new SceneSection() {SceneGUID = sceneGUID, Section = 0});
                Object.DestroyImmediate(pair.Item1);
            }
            
            EntityManager.MoveEntitiesFrom(BakingEntityManager);

            // Fake the scene section entity, so the PersistentSceneSystem does it's work
            Entity sceneSectionEntity =  EntityManager.CreateEntity();
            EntityManager.AddComponentData(sceneSectionEntity, new SceneSectionData()
            {
                SceneGUID = sceneGUID,
                SubSectionIndex = 0
            });
            EntityManager.AddComponentData(sceneSectionEntity, new RequestSceneLoaded());
        }
        
        private List<(QuickSaveAuthoring, Entity)> CreateGameObjectsAndPrimaryEntitiesForBaking(int total, bool comp, bool tag, bool buffer)
        {
            List<(QuickSaveAuthoring, Entity)> all = new List<(QuickSaveAuthoring, Entity)>(total);
            for (int i = 0; i < total; i++)
            {
                GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = $"TestAuthoring_{i.ToString()}";
                QuickSaveAuthoring quickSaveAuthoring = go.AddComponent<QuickSaveAuthoring>();
                if (comp)
                {
                    quickSaveAuthoring.FullTypeNamesToPersist.Add(typeof(EcsTestData).FullName);
                    quickSaveAuthoring.FullTypeNamesToPersist.Add(typeof(EcsTestFloatData2).FullName);
                    quickSaveAuthoring.FullTypeNamesToPersist.Add(typeof(EcsTestData5).FullName);
                    go.name += "_C";
                }
                if (tag)
                {
                    quickSaveAuthoring.FullTypeNamesToPersist.Add(typeof(EmptyEcsTestData).FullName);
                    go.name += "_T";
                }
                if (buffer)
                {
                    quickSaveAuthoring.FullTypeNamesToPersist.Add(typeof(DynamicBufferData1).FullName);
                    quickSaveAuthoring.FullTypeNamesToPersist.Add(typeof(DynamicBufferData2).FullName);
                    quickSaveAuthoring.FullTypeNamesToPersist.Add(typeof(DynamicBufferData3).FullName);
                    go.name += "_B";
                }

                Entity e = BakingEntityManager.CreateEntity();
                BakingEntityManager.SetName(e, go.name);
                all.Add((quickSaveAuthoring, e));
            }

            return all;
        }
        
        // Todo I dislike this cody copying!
        // Should almost be a perfect copy of the PersistencyBaker::Bake method
        internal static void BakeForTests(QuickSaveAuthoring authoring, EntityManager entityManager, Entity e, Hash128 sceneGUID, QuickSaveSettingsAsset settingsAsset)
        {
            entityManager.AddSharedComponent(e, new SceneSection { SceneGUID = sceneGUID });
            // var settings = QuickSaveSettingsAsset.Get();
            // if (settings == null || settings.QuickSaveArchetypeCollection == null)
            //    return;
            //
            // DependsOn(settings);
            // DependsOn(settings.QuickSaveArchetypeCollection);
            
            // Get the types from preset or type list
            List<string> fullTypeNames = authoring.FullTypeNamesToPersist;
            if (authoring.QuickSaveArchetypeName != "")
            {
                var quickSaveArchetype = settingsAsset.QuickSaveArchetypeCollection.Definitions.Find(p => p.Name == authoring.QuickSaveArchetypeName);
                fullTypeNames = quickSaveArchetype != null ? quickSaveArchetype.FullTypeNames : new List<string>();
            }

            if (fullTypeNames.Count == 0)
                return;
            
            QuickSaveSettings.Initialize(); // This needs to be here because we're in baking & it's not guaranteed to be initialized
            
            // Add 2 uninitialized components that will get set by the baking system
            entityManager.AddComponent<LocalIndexInContainer>(e);
            entityManager.AddSharedComponent(e, new QuickSaveArchetypeIndexInContainer
            {
                IndexInContainer = ushort.MaxValue // uninitialized but will get set by the baking system
            });
            
            // Add the baking only components
            var bakingOnlyBuffer = entityManager.AddBuffer<QuickSaveTypeHandlesBakingOnly>(e);
            foreach (var handle in QuickSaveSettings.GetTypeHandles(fullTypeNames, Allocator.Temp))
            {
                bakingOnlyBuffer.Add(new QuickSaveTypeHandlesBakingOnly { QuickSaveTypeHandle = handle });
            }
            QuickSaveDataLayoutHashBakingOnly dataLayoutHash = new QuickSaveDataLayoutHashBakingOnly();
            foreach (QuickSaveTypeHandlesBakingOnly bufferElement in bakingOnlyBuffer)
            {
                var typeInfo = TypeManager.GetTypeInfo(QuickSaveSettings.GetTypeIndex(bufferElement.QuickSaveTypeHandle));
                dataLayoutHash.Value = TypeHash.CombineFNV1A64(dataLayoutHash.Value, typeInfo.StableTypeHash);
            }
            entityManager.AddComponentData(e, dataLayoutHash);
        }
    }
}
