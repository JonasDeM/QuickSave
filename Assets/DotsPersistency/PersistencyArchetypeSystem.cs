using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using UnityEngine;

namespace DotsPersistency
{
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    public class PersistenceArchetypeSystem : ComponentSystem
    {
        private EntityQuery _query;
        private List<SceneSection> _requests = new List<SceneSection>(8);

        protected override void OnCreate()
        {
            _query = GetEntityQuery(new EntityQueryDesc(){ 
                All = new [] {ComponentType.ReadOnly<TypeHashesToPersist>(), ComponentType.ReadOnly<SceneSection>()},
                Options = EntityQueryOptions.IncludeDisabled
            });
        }

        public void RequestInitSceneSection(SceneSection section)
        {
            Debug.Assert(!_requests.Contains(section), "Already requested this scene section");
            _requests.Add(section);
        }

        protected override void OnUpdate()
        {
            var uniqueSharedCompData = new List<TypeHashesToPersist>();
            EntityManager.GetAllUniqueSharedComponentData(uniqueSharedCompData);
            uniqueSharedCompData.Remove(default);

            foreach (var sceneSection in _requests)
            {
                InitSceneSection(sceneSection, uniqueSharedCompData);
            }
        }

        private void InitSceneSection(SceneSection sceneSection, List<TypeHashesToPersist> typeHashes)
        {
            int offset = 0;
            for (var i = 0; i < typeHashes.Count; i++)
            {
                var typeHashesToPersist = typeHashes[i];
                _query.SetSharedComponentFilter(typeHashesToPersist, sceneSection);
                int amount = _query.CalculateEntityCount();
                if (amount <= 0) 
                    continue;
                
                var persistenceArchetype = new PersistenceArchetype()
                {
                    Amount = _query.CalculateEntityCount(),
                    ArchetypeIndex = i,
                    PersistedTypeInfoArrayRef = BuildTypeInfoBlobAsset(typeHashesToPersist.TypeHashList, amount, out int sizePerEntity),
                    SizePerEntity = sizePerEntity,
                    Offset = offset
                };
                offset += amount * sizePerEntity;
    
                EntityManager.AddSharedComponentData(_query, persistenceArchetype);
                EntityManager.RemoveComponent<TypeHashesToPersist>(_query);
            }
        }

        internal static BlobAssetReference<BlobArray<PersistedTypeInfo>> BuildTypeInfoBlobAsset(FixedList128<ulong> stableTypeHashes, int amountEntities, out int sizePerEntity)
        {
            BlobAssetReference<BlobArray<PersistedTypeInfo>> blobAssetReference;
            int currentOffset = 0;
            sizePerEntity = 0;
            
            using (BlobBuilder blobBuilder = new BlobBuilder(Allocator.Temp))
            {
                ref BlobArray<PersistedTypeInfo> blobArray = ref blobBuilder.ConstructRoot<BlobArray<PersistedTypeInfo>>();

                var blobBuilderArray = blobBuilder.Allocate(ref blobArray, stableTypeHashes.Length);

                for (int i = 0; i < blobBuilderArray.Length; i++)
                {
                    var typeInfo = TypeManager.GetTypeInfo(TypeManager.GetTypeIndexFromStableTypeHash(stableTypeHashes[i]));

                    int maxElements = typeInfo.Category == TypeManager.TypeCategory.BufferData ? typeInfo.BufferCapacity : 1;
                    blobBuilderArray[i] = new PersistedTypeInfo()
                    {
                        StableHash = stableTypeHashes[i],
                        ElementSize = typeInfo.ElementSize,
                        IsBuffer = typeInfo.Category == TypeManager.TypeCategory.BufferData,
                        MaxElements = maxElements,
                        Offset = currentOffset
                    };
                    sizePerEntity += typeInfo.ElementSize * maxElements + sizeof(ushort); // PersistenceMetaData is one ushort
                    currentOffset += sizePerEntity * amountEntities;
                }

                blobAssetReference = blobBuilder.CreateBlobAssetReference<BlobArray<PersistedTypeInfo>>(Allocator.Persistent);
            }
            
            return blobAssetReference;
        }
    }
}