// Author: Jonas De Maeseneer

using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using Unity.Collections;
using Unity.Entities;
using Debug = UnityEngine.Debug;
using Hash128 = Unity.Entities.Hash128;
// ReSharper disable AccessToDisposedClosure

[assembly:InternalsVisibleTo("com.studioaurelius.quicksave.editor")]
[assembly:InternalsVisibleTo("com.studioaurelius.quicksave.tests")]

namespace QuickSave.Baking
{
    // Works in Tandem with PersistencyBaker
    [WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]
    internal partial class QuickSaveBakingSystem : SystemBase
    {
        private EntityQuery _allPersistenceEntities;
        private EntityQuery _validQuickSaveEntities;
        
        internal struct QuickSaveArchetypesInSceneCreationInfo
        {
            public int OffsetInQuickSaveTypeHandlesLookupList;
            public int AmountTypeHandles;
            public int AmountEntities;
        }

        protected override void OnCreate()
        {
            base.OnCreate();

            NativeList<ComponentType> types = new NativeList<ComponentType>(5, Allocator.Temp);
            types.Add(typeof(LocalIndexInContainer));
            types.Add(typeof(QuickSaveArchetypeIndexInContainer));
            types.Add(typeof(QuickSaveTypeHandlesBakingOnly));
            types.Add(typeof(QuickSaveDataLayoutHashBakingOnly));
            _allPersistenceEntities = new EntityQueryBuilder(Allocator.Temp).WithAny(ref types).WithOptions(EntityQueryOptions.IncludeDisabledEntities).Build(this);
            _validQuickSaveEntities = new EntityQueryBuilder(Allocator.Temp).WithAll(ref types).WithOptions(EntityQueryOptions.IncludeDisabledEntities).Build(this);
        }

        protected override void OnUpdate()
        {
            BlobAssetStore blobAssetStore = World.GetExistingSystemManaged<BakingSystem>().BlobAssetStore;
            Update(blobAssetStore);
        }

        internal void Update(BlobAssetStore blobAssetStore)
        {
            QuickSaveSettings.Initialize(); // This needs to be here because we're in baking & it's not guaranteed to be initialized

            // Check for invalid entities
            if (_allPersistenceEntities.CalculateEntityCountWithoutFiltering() != _validQuickSaveEntities.CalculateEntityCount())
            {
                Debug.LogError($"{nameof(QuickSaveBakingSystem)} encountered entities that were invalid for this system! The baking result is likely incomplete or incorrect.");
            }
            
            // Init Containers
            NativeReference<ulong> containerDataLayoutHashRef = new NativeReference<ulong>(Allocator.TempJob) {Value = 17};
            
            NativeList<QuickSaveArchetypesInSceneCreationInfo> blobCreationInfoList = new NativeList<QuickSaveArchetypesInSceneCreationInfo>(16, Allocator.TempJob);
            NativeList<QuickSaveTypeHandle> quickSaveTypeHandlesLookup = new NativeList<QuickSaveTypeHandle>(128, Allocator.TempJob);
            NativeList<int> indexCounters = new NativeList<int>(Allocator.TempJob);
            
            NativeHashSet<QuickSaveTypeHandle> allUniqueTypeHandles = new NativeHashSet<QuickSaveTypeHandle>(16, Allocator.TempJob);
            NativeHashMap<ulong, ushort> hashToArchetypeIndexInContainer = new NativeHashMap<ulong, ushort>(16, Allocator.TempJob);
            EntityCommandBuffer ecb = new EntityCommandBuffer(Allocator.TempJob);

            // Iterate all persistable entities to get overview on indices & amounts
            Entities.WithEntityQueryOptions(EntityQueryOptions.IncludeDisabledEntities).ForEach(
                (Entity e, ref LocalIndexInContainer indexInContainer, 
                    in QuickSaveDataLayoutHashBakingOnly dataLayoutHash, in DynamicBuffer<QuickSaveTypeHandlesBakingOnly> typeHandlesBuffer
                    ) =>
                {
                    if (!hashToArchetypeIndexInContainer.ContainsKey(dataLayoutHash.Value))
                    {
                        Debug.Assert(blobCreationInfoList.Length < ushort.MaxValue);
                        indexCounters.Add(0);
                        hashToArchetypeIndexInContainer.Add(dataLayoutHash.Value, (ushort)blobCreationInfoList.Length);
                        blobCreationInfoList.Add(new QuickSaveArchetypesInSceneCreationInfo()
                        {
                            AmountEntities = 0,
                            AmountTypeHandles = typeHandlesBuffer.Length,
                            OffsetInQuickSaveTypeHandlesLookupList = quickSaveTypeHandlesLookup.Length
                        });

                        for (var i = 0; i < typeHandlesBuffer.Length; i++)
                        {
                            var handle = typeHandlesBuffer[i].QuickSaveTypeHandle;
                            allUniqueTypeHandles.Add(handle);
                            quickSaveTypeHandlesLookup.Add(handle);
                        }

                        // Add the datalayouthash of the new QuickSaveArchetype to the datalayouthash of the container
                        containerDataLayoutHashRef.Value = TypeHash.CombineFNV1A64(containerDataLayoutHashRef.Value, dataLayoutHash.Value);
                    }
                    
                    // Set index
                    ushort archetypeIndexInContainer = hashToArchetypeIndexInContainer[dataLayoutHash.Value];
                    ecb.SetSharedComponent(e, new QuickSaveArchetypeIndexInContainer { IndexInContainer = archetypeIndexInContainer });
                    int index = indexCounters[archetypeIndexInContainer];
                    indexInContainer.LocalIndex = index;
                    indexCounters[archetypeIndexInContainer] = index + 1;

                    // update amount for ScenePersistencyInfoEntity
                    var tempToUpdate = blobCreationInfoList[archetypeIndexInContainer];
                    tempToUpdate.AmountEntities += 1;
                    blobCreationInfoList[archetypeIndexInContainer] = tempToUpdate;
                }).WithBurst().Run();

            // Check if all types are supported
            bool allSupported = true;
            foreach (var quickSaveTypeHandle in allUniqueTypeHandles)
            {
                if (QuickSaveSettings.IsSupported(TypeManager.GetTypeInfo(QuickSaveSettings.GetTypeIndex(quickSaveTypeHandle)), out string notSupportedReason)) 
                    continue;
                Debug.LogError($"{nameof(QuickSaveBakingSystem)}:: BAKE FAILED FOR {TryGetScenePath(GetSceneGuid())} (NonSupported Type found): {notSupportedReason}");
                allSupported = false;
            }
            
            // Only do this work for scenes with persistent entities & when all types are valid
            if (blobCreationInfoList.Length > 0 && allSupported)
            {
                // Make the shared component changes
                ecb.Playback(EntityManager);
                
                // Add amount entities per QuickSaveArchetype to hash
                foreach (var creationInfo in blobCreationInfoList)
                {
                    containerDataLayoutHashRef.Value = TypeHash.CombineFNV1A64(containerDataLayoutHashRef.Value, (ulong) creationInfo.AmountEntities);
                }

                // Create a single entity (per scene) that holds blob data which contains all the necessary info to be able to persist the whole subscene to a single container
                CreateOrUpdateScenePersistencyInfoEntity(allUniqueTypeHandles, blobCreationInfoList, quickSaveTypeHandlesLookup,
                    containerDataLayoutHashRef.Value, GetSceneGuid(), blobAssetStore);
            }
            
            // Clean up
            containerDataLayoutHashRef.Dispose();
            blobCreationInfoList.Dispose();
            quickSaveTypeHandlesLookup.Dispose();
            indexCounters.Dispose();
            allUniqueTypeHandles.Dispose();
            hashToArchetypeIndexInContainer.Dispose();
            ecb.Dispose();
        }

        protected override void OnDestroy()
        {
            QuickSaveSettings.CleanUp(); // This needs to be here because we're in baking & this system should be the last thing that uses it during baking
            base.OnDestroy();
        }

        private void CreateOrUpdateScenePersistencyInfoEntity(NativeHashSet<QuickSaveTypeHandle> allUniqueTypeHandles, 
            NativeList<QuickSaveArchetypesInSceneCreationInfo> blobCreationInfoList, NativeList<QuickSaveTypeHandle> typeHandlesLookup, ulong dataLayoutHash,
            Hash128 sceneGUID, BlobAssetStore blobAssetStore)
        {
            // GetOrCreate Setting Entity & set the right data
            // Create the entity that contains all the info the PersistentSceneSystem needs for initializing a loaded scene
            NativeArray<Entity> scenePersistencyInfoEntityArray = EntityManager.CreateEntityQuery(typeof(QuickSaveSceneInfoRef)).ToEntityArray(Allocator.Temp);
            Entity sceneInfoEntity;
            if (scenePersistencyInfoEntityArray.Length == 0)
            {
                sceneInfoEntity = EntityManager.CreateEntity();
                EntityManager.SetName(sceneInfoEntity, nameof(QuickSaveSceneInfoRef));
            }
            else
            {
                sceneInfoEntity = scenePersistencyInfoEntityArray[0];
                if (scenePersistencyInfoEntityArray.Length > 1)
                {
                    Debug.LogError($"{nameof(QuickSaveBakingSystem)} found more than 1 {nameof(QuickSaveSceneInfoRef)} entities, this indicates an invalid bake!");
                }
            }
            List<QuickSaveTypeHandle> allUniqueTypeHandlesSorted = new List<QuickSaveTypeHandle>(allUniqueTypeHandles.Count);
            var hashSetEnumerator = allUniqueTypeHandles.GetEnumerator();
            while (hashSetEnumerator.MoveNext())
            {
                allUniqueTypeHandlesSorted.Add(hashSetEnumerator.Current);
            }
            allUniqueTypeHandlesSorted.Sort((left, right) => left.CompareTo(right));

            QuickSaveSceneInfoRef quickSaveSceneInfoRef = CreateQuickSaveSceneInfoRef(allUniqueTypeHandlesSorted, blobCreationInfoList,
                typeHandlesLookup, sceneGUID, dataLayoutHash, blobAssetStore);
            EntityManager.AddComponentData(sceneInfoEntity, quickSaveSceneInfoRef);

            Debug.Assert(sceneGUID != default);
            EntityManager.AddSharedComponent(sceneInfoEntity, new SceneSection {SceneGUID = sceneGUID, Section = 0});

            if (QuickSaveSettings.VerboseBakingLog)
            {
                VerboseLog(quickSaveSceneInfoRef, TryGetScenePath(sceneGUID));
            }
        }

        internal static QuickSaveSceneInfoRef CreateQuickSaveSceneInfoRef(List<QuickSaveTypeHandle> allUniqueTypeHandlesSorted, 
            NativeList<QuickSaveArchetypesInSceneCreationInfo> blobCreationInfoList, NativeList<QuickSaveTypeHandle> typeHandlesLookup, Hash128 sceneGUID, ulong dataLayoutHash,
            BlobAssetStore blobAssetStore)
        {
            QuickSaveSceneInfoRef quickSaveSceneInfoRef = new QuickSaveSceneInfoRef();
            using (BlobBuilder blobBuilder = new BlobBuilder(Allocator.Temp))
            {
                ref QuickSaveSceneInfo quickSaveSceneInfo = ref blobBuilder.ConstructRoot<QuickSaveSceneInfo>();

                BlobBuilderArray<QuickSaveTypeHandle> blobBuilderArray1 = blobBuilder.Allocate(ref quickSaveSceneInfo.AllUniqueTypeHandles, allUniqueTypeHandlesSorted.Count);
                for (int i = 0; i < blobBuilderArray1.Length; i++)
                {
                    blobBuilderArray1[i] = allUniqueTypeHandlesSorted[i];
                }
                
                BlobBuilderArray<QuickSaveArchetypesInScene> blobBuilderArray2 = blobBuilder.Allocate(ref quickSaveSceneInfo.QuickSaveArchetypesInScene, blobCreationInfoList.Length);
                for (int i = 0; i < blobCreationInfoList.Length; i++)
                {
                    QuickSaveArchetypesInSceneCreationInfo info = blobCreationInfoList[i];
                    ref QuickSaveArchetypesInScene quickSaveArchetypesInScene = ref blobBuilderArray2[i];
                    quickSaveArchetypesInScene.AmountEntities = info.AmountEntities;
                    BlobBuilderArray<QuickSaveTypeHandle> blobBuilderArray3 = blobBuilder.Allocate(ref quickSaveArchetypesInScene.QuickSaveTypeHandles, info.AmountTypeHandles);

                    for (int j = 0; j < info.AmountTypeHandles; j++)
                    {
                        blobBuilderArray3[j] = typeHandlesLookup[info.OffsetInQuickSaveTypeHandlesLookupList + j];
                    }
                }

                quickSaveSceneInfo.SceneGUID = sceneGUID;
                quickSaveSceneInfo.DataLayoutHash = dataLayoutHash;

                BlobAssetReference<QuickSaveSceneInfo> blobAssetReference = blobBuilder.CreateBlobAssetReference<QuickSaveSceneInfo>(Allocator.Persistent);
                blobAssetStore.TryAdd(ref blobAssetReference);
                quickSaveSceneInfoRef.InfoRef = blobAssetReference;
            }
            return quickSaveSceneInfoRef;
        }
        
        private Hash128 GetSceneGuid()
        {
            EntityManager.GetAllUniqueSharedComponents(out NativeList<SceneSection> allValues, Allocator.Temp);
            Hash128 sceneGUID = default;
            foreach (var sceneSection in allValues)
            {
                if (sceneSection.SceneGUID != default)
                    sceneGUID = sceneSection.SceneGUID;
            }
            Debug.Assert(sceneGUID != default, $"Expected to find another SceneSection value than default!");
            return sceneGUID;
        }
        
        private string TryGetScenePath(Hash128 sceneGUID)
        {
            string scenePath = "";
#if UNITY_EDITOR
            try
            {
                scenePath = UnityEditor.AssetDatabase.GUIDToAssetPath(sceneGUID);
            }
            catch { /*ignored*/ }
#endif
            if (string.IsNullOrEmpty(scenePath))
            {
                scenePath = "Scene Path Not Found";
            }
            return scenePath;
        }
        
        [Conditional("UNITY_EDITOR")]
        private static void VerboseLog(QuickSaveSceneInfoRef quickSaveSceneInfoRef, string sceneName)
        {
            ref QuickSaveSceneInfo quickSaveSceneInfo = ref quickSaveSceneInfoRef.InfoRef.Value;
            StringBuilder stringBuilder = new StringBuilder($"PersistenceBaking Logs ({sceneName})\n", 128);
            stringBuilder.AppendLine("------DataLayoutHash------");
            stringBuilder.AppendLine(quickSaveSceneInfoRef.InfoRef.Value.DataLayoutHash.ToString());
            for (int i = 0; i < quickSaveSceneInfo.QuickSaveArchetypesInScene.Length; i++)
            {
                stringBuilder.AppendLine($"---Archetype{i.ToString()}---");
                stringBuilder.AppendLine("Amount Entities: " + quickSaveSceneInfo.QuickSaveArchetypesInScene[i].AmountEntities.ToString());
                for (int j = 0; j < quickSaveSceneInfo.QuickSaveArchetypesInScene[i].QuickSaveTypeHandles.Length; j++)
                {
                    stringBuilder.AppendLine(ComponentType.FromTypeIndex(QuickSaveSettings.GetTypeIndex(quickSaveSceneInfo.QuickSaveArchetypesInScene[i].QuickSaveTypeHandles[j])).ToString());
                }
            }
            stringBuilder.AppendLine("------AllUniqueTypes------");
            for (int i = 0; i < quickSaveSceneInfo.AllUniqueTypeHandles.Length; i++)
            {
                stringBuilder.AppendLine(ComponentType.FromTypeIndex(QuickSaveSettings.GetTypeIndex(quickSaveSceneInfo.AllUniqueTypeHandles[i])).ToString());
            }
            Debug.Log(stringBuilder.ToString());
        }
    }
}
