// Author: Jonas De Maeseneer

using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;
using Hash128 = Unity.Entities.Hash128;

namespace DotsPersistency.Hybrid
{
    [ConverterVersion("Jonas", 2)]
    [UpdateInGroup(typeof(GameObjectDeclareReferencedObjectsGroup))]
    public class PersistencyConversionReferenceSystem : GameObjectConversionSystem
    {
        protected override void OnUpdate()
        {
            Entities.ForEach((PersistencyAuthoring persistencyAuthoring)
                =>
            {
                Entities.ForEach((PersistencyAuthoring other)
                    =>
                {
                    if (persistencyAuthoring != other)
                    {
                        DeclareDependency(persistencyAuthoring, other);
                    }
                });
            });
        }
    }

    [ConverterVersion("Jonas", 10)]
    [UpdateInGroup(typeof(GameObjectAfterConversionGroup))]
    public class PersistencyConversionSystem : GameObjectConversionSystem
    {
        protected override void OnUpdate()
        {
            var hashMap = new NativeHashMap<Hash128, PersistenceArchetype>(8, Allocator.Temp);
            
            CreatePersistenceArchetypes(hashMap);
            AddComponents(hashMap);

            hashMap.Dispose();
        }

        private void CreatePersistenceArchetypes(NativeHashMap<Hash128, PersistenceArchetype> hashMap)
        {
            int archetypeIndex = 0;

            var hashToStableTypeHashes = new Dictionary<Hash128, List<ulong>>(8);
            
            // Phase 1: Calculate PersistenceArchetypeIndices & Amount Entities per PersistenceArchetype
            Entities.ForEach((PersistencyAuthoring persistencyAuthoring)
                =>
            {
                var persistenceArchetypeHash = persistencyAuthoring.GetStablePersistenceArchetypeHash();
                PersistenceArchetype persistenceArchetype;
                if (!hashMap.TryGetValue(persistenceArchetypeHash, out persistenceArchetype))
                {
                    persistenceArchetype = new PersistenceArchetype()
                    {
                        Amount = 0,
                        ArchetypeIndex = archetypeIndex,
                        // todo remove
                        ComponentDataTypeHashList = persistencyAuthoring.GetComponentDataTypesToPersistHashes(),
                        BufferElementTypeHashList = persistencyAuthoring.GetBufferDataTypesToPersistHashes()
                    };
                    archetypeIndex++;
                    hashMap.Add(persistenceArchetypeHash, persistenceArchetype);
                    hashToStableTypeHashes.Add(persistenceArchetypeHash, persistencyAuthoring.FilteredTypesToPersistHashes);
                }

                persistenceArchetype.Amount += 1;
                hashMap[persistenceArchetypeHash] = persistenceArchetype;
            });
            
            // Phase 2: Calculate Byte Offset Per Type into the PersistenceArchetype SubArray
            var keyArray = hashMap.GetKeyArray(Allocator.Temp);
            var valueArray = hashMap.GetValueArray(Allocator.Temp);
            foreach (var keyToUpdate in keyArray)
            {
                var archetypeToUpdate = hashMap[keyToUpdate];

                archetypeToUpdate.PersistedTypeInfoArrayRef = BuildTypeInfoBlobAsset(hashToStableTypeHashes[keyToUpdate], archetypeToUpdate.Amount, out int sizePerEntity);
                archetypeToUpdate.SizePerEntity = sizePerEntity;
                
                hashMap[keyToUpdate] = archetypeToUpdate;
            }

            // Phase 3: Calculate Byte Offset into the SceneSection Array
            valueArray.Dispose();
            valueArray = hashMap.GetValueArray(Allocator.Temp);
            foreach (var keyToUpdate in keyArray)
            {
                var archetypeToUpdate = hashMap[keyToUpdate];

                foreach (var otherArchetype in valueArray)
                {
                    if (otherArchetype.ArchetypeIndex < archetypeToUpdate.ArchetypeIndex)
                    {
                        archetypeToUpdate.Offset += otherArchetype.Amount * otherArchetype.SizePerEntity;
                    }
                }

                hashMap[keyToUpdate] = archetypeToUpdate;
            }
        }
        
        
        private void AddComponents(NativeHashMap<Hash128, PersistenceArchetype> hashMap)
        {
            Entities.ForEach((PersistencyAuthoring persistencyAuthoring)
                =>
            {
                Entity e = GetPrimaryEntity(persistencyAuthoring);
                DstEntityManager.AddSharedComponentData(e, hashMap[persistencyAuthoring.GetStablePersistenceArchetypeHash()]);
                DstEntityManager.AddComponentData(e, new PersistenceState()
                {
                    ArrayIndex = persistencyAuthoring.CalculateArrayIndex()
                });
            });
        }
        
        BlobAssetReference<BlobArray<PersistedTypeInfo>> BuildTypeInfoBlobAsset(List<ulong> stableTypeHashes, int amountEntities, out int sizePerEntity)
        {
            BlobAssetReference<BlobArray<PersistedTypeInfo>> blobAssetReference;
            int currentOffset = 0;
            sizePerEntity = 0;

            using (BlobBuilder blobBuilder = new BlobBuilder(Allocator.Temp))
            {
                ref BlobArray<PersistedTypeInfo> blobArray = ref blobBuilder.ConstructRoot<BlobArray<PersistedTypeInfo>>();

                var blobBuilderArray = blobBuilder.Allocate(ref blobArray, stableTypeHashes.Count);

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
                    sizePerEntity += typeInfo.ElementSize * maxElements;
                    currentOffset += typeInfo.ElementSize * maxElements * amountEntities;
                }

                blobAssetReference = blobBuilder.CreateBlobAssetReference<BlobArray<PersistedTypeInfo>>(Allocator.Persistent);
            }
            
            return blobAssetReference;
        }
    }
}
