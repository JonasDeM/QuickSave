// Author: Jonas De Maeseneer

using System.Collections.Generic;
using NUnit.Framework;
using QuickSave.Baking;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.CodeGeneratedJobForEach;
using UnityEngine;
using UnityEngine.SceneManagement;
using Hash128 = Unity.Entities.Hash128;
using Object = UnityEngine.Object;

namespace QuickSave.Tests
{
    public abstract class EcsTestsFixture
    {
        protected World PreviousWorld;
        protected World World;
        protected EntityManager EntityManager;
        
        protected World BakingWorld;
        protected EntityManager BakingEntityManager;

        protected NativeList<BlobAssetReference<BlobArray<QuickSaveArchetypeDataLayout.TypeInfo>>> BlobAssetsToDisposeOnTearDown;
        protected BlobAssetStore TestBlobAssetStore;

        [SetUp]
        public virtual void Setup()
        {
            PreviousWorld = World.DefaultGameObjectInjectionWorld;
            World = World.DefaultGameObjectInjectionWorld = new World("Test World");
            EntityManager = World.EntityManager;
            BakingWorld = new World("Test Baking World");
            BakingEntityManager = BakingWorld.EntityManager;
            QuickSaveSettings.Initialize();
            BlobAssetsToDisposeOnTearDown = new NativeList<BlobAssetReference<BlobArray<QuickSaveArchetypeDataLayout.TypeInfo>>>(16, Allocator.Persistent);
            TestBlobAssetStore = new BlobAssetStore(64);
        }

        [TearDown]
        public virtual void TearDown()
        {
            if (EntityManager != default && World.IsCreated)
            {
                // Clean up systems before calling CheckInternalConsistency because we might have filters etc
                // holding on SharedComponentData making checks fail
                while (World.Systems.Count > 0)
                {
                    World.DestroySystemManaged(World.Systems[0]);
                }
                EntityManager.Debug.CheckInternalConsistency();
                World.Dispose();
                World = null;
                World.DefaultGameObjectInjectionWorld = PreviousWorld;
                PreviousWorld = null;
                EntityManager = default;
            }

            if (BakingEntityManager != default && BakingWorld.IsCreated)
            {
                while (BakingWorld.Systems.Count > 0)
                {
                    BakingWorld.DestroySystemManaged(BakingWorld.Systems[0]);
                }
                BakingEntityManager.Debug.CheckInternalConsistency();
                BakingWorld.Dispose();
                BakingWorld = null;
                BakingEntityManager = default;
            }

            foreach (var rootGameObject in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                Object.DestroyImmediate(rootGameObject);
            }
            QuickSaveSettings.CleanUp();
            
            for (int i = 0; i < BlobAssetsToDisposeOnTearDown.Length; i++)
            {
                BlobAssetsToDisposeOnTearDown[i].Dispose();
            }
            BlobAssetsToDisposeOnTearDown.Dispose();
            TestBlobAssetStore.Dispose();
        }
        
        protected static QuickSaveSettingsAsset CreateTestSettings(bool groupedJobs = false, bool removeFirst = false, int maxBufferElements = -1)
        {
            QuickSaveSettingsAsset settingsAsset = ScriptableObject.CreateInstance<QuickSaveSettingsAsset>();
            settingsAsset.AddQuickSaveTypeInEditor(typeof(EcsTestData).FullName);
            settingsAsset.AddQuickSaveTypeInEditor(typeof(EcsTestFloatData2).FullName);
            settingsAsset.AddQuickSaveTypeInEditor(typeof(EcsTestData5).FullName);
            settingsAsset.AddQuickSaveTypeInEditor(typeof(DynamicBufferData1).FullName, maxBufferElements);
            settingsAsset.AddQuickSaveTypeInEditor(typeof(DynamicBufferData2).FullName, maxBufferElements);
            settingsAsset.AddQuickSaveTypeInEditor(typeof(DynamicBufferData3).FullName, maxBufferElements);
            settingsAsset.AddQuickSaveTypeInEditor(typeof(EmptyEcsTestData).FullName);
            settingsAsset.AddQuickSaveTypeInEditor(typeof(ComponentDataTests.EcsPersistingTestData).FullName);
            settingsAsset.AddQuickSaveTypeInEditor(typeof(ComponentDataTests.EcsPersistingFloatTestData2).FullName);
            settingsAsset.AddQuickSaveTypeInEditor(typeof(ComponentDataTests.EcsPersistingTestData5).FullName);
            settingsAsset.AddQuickSaveTypeInEditor(typeof(BufferDataTests.PersistentDynamicBufferData1).FullName, maxBufferElements);
            settingsAsset.AddQuickSaveTypeInEditor(typeof(BufferDataTests.PersistentDynamicBufferData2).FullName, maxBufferElements);
            settingsAsset.AddQuickSaveTypeInEditor(typeof(BufferDataTests.PersistentDynamicBufferData3).FullName, maxBufferElements);
            settingsAsset.AddQuickSaveTypeInEditor(typeof(EnableDataTests.TestComponent).FullName);
            settingsAsset.AddQuickSaveTypeInEditor(typeof(EnableDataTests.TestTagComponent).FullName);
            settingsAsset.AddQuickSaveTypeInEditor(typeof(EnableDataTests.TestBufferComponent).FullName, maxBufferElements);

            settingsAsset.ForceUseGroupedJobsInEditor = groupedJobs;
            settingsAsset.ForceUseNonGroupedJobsInBuild = !groupedJobs;
            if (removeFirst)
            {
                settingsAsset.AllQuickSaveTypeInfos.RemoveAt(0);
            }

            settingsAsset.QuickSaveArchetypeCollection = ScriptableObject.CreateInstance<QuickSaveArchetypeCollection>();
            
            // Reset it so the new types are initialized
            QuickSaveSettings.CleanUp();
            QuickSaveSettings.Initialize(settingsAsset);

            return settingsAsset;
        }
        
        // Creates a 'fake' SceneInfoRef for single QuickSaveArchetype with a single Type T to track
        internal QuickSaveSceneInfoRef CreateFakeSceneInfoRef<T>(int amountEntities) where T : unmanaged
        {
            var typeHandle = QuickSaveSettings.GetTypeHandleFromTypeIndex(ComponentType.ReadWrite<T>().TypeIndex);
            var typeHandleList = new NativeList<QuickSaveTypeHandle>(1, Allocator.Temp) {typeHandle};
            var creationInfoList = new NativeList<QuickSaveBakingSystem.QuickSaveArchetypesInSceneCreationInfo>(Allocator.Temp)
            {
                new QuickSaveBakingSystem.QuickSaveArchetypesInSceneCreationInfo
                {
                    AmountEntities = amountEntities,
                    AmountTypeHandles = 1,
                    OffsetInQuickSaveTypeHandlesLookupList = 0
                }
            };
            Hash128 sceneGUID = UnityEngine.Hash128.Compute(typeof(T).FullName);
            return QuickSaveBakingSystem.CreateQuickSaveSceneInfoRef(new List<QuickSaveTypeHandle> {typeHandle}, creationInfoList, typeHandleList,
                sceneGUID, 0, TestBlobAssetStore);
        }
        
        [DisableAutoCreation]
        internal partial class TestSystem : SystemBase
        {
            protected override void OnUpdate() { }
        }
    }
    
    
    public struct EcsTestData : IComponentData
    {
        public int Value;
        
        public EcsTestData(int value)
        {
            this.Value = value;
        }
    }
    public struct EcsTestFloatData2 : IComponentData
    {
        public float Value0;
        public float Value1;
        
        public EcsTestFloatData2(float value)
        {
            this.Value0 = value;
            this.Value1 = value;
        }
    }
    public struct EcsTestData5 : IComponentData
    {
        public EcsTestData5(int value)
        {
            Value0 = value;
            Value1 = value;
            Value2 = value;
            Value3 = value;
            Value4 = value;
        }
        
        public int Value0;
        public int Value1;
        public int Value2;
        public int Value3;
        public int Value4;
    }
    
    
    [InternalBufferCapacity(2)]
    public struct DynamicBufferData1 : IBufferElementData
    {
        public int Value;
            
        public override string ToString()
        {
            return Value.ToString();
        }
    }
    
    public struct DynamicBufferData2 : IBufferElementData
    {
#pragma warning disable 649
        public float Value;
#pragma warning restore 649
            
        public override string ToString()
        {
            return Value.ToString();
        }
    }
    
    public struct DynamicBufferData3 : IBufferElementData, IEnableableComponent
    {
        public byte Value;
            
        public override string ToString()
        {
            return Value.ToString();
        }
    }
    
    public struct EmptyEcsTestData : IComponentData
    {
            
    }
}