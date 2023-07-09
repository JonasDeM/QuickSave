// Author: Jonas De Maeseneer

using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;

namespace QuickSave.Baking
{
    [BakingType]
    internal struct QuickSaveTypeHandlesBakingOnly : IBufferElementData
    {
        public QuickSaveTypeHandle QuickSaveTypeHandle;
    }
    
    [BakingType]
    internal struct QuickSaveDataLayoutHashBakingOnly : IComponentData
    {
        public ulong Value;
    }
    
    // Works in Tandem with PersistencyBakingSystem
    internal class QuickSaveBaker : Baker<QuickSaveAuthoring>
    {
        public override void Bake(QuickSaveAuthoring authoring)
        {
            var settings = QuickSaveSettingsAsset.Get();
            if (settings == null || settings.QuickSaveArchetypeCollection == null)
                return;
            
            DependsOn(settings);
            DependsOn(settings.QuickSaveArchetypeCollection);
            
            // Get the types from preset or type list
            List<string> fullTypeNames = authoring.FullTypeNamesToPersist;
            if (authoring.QuickSaveArchetypeName != "")
            {
                var quickSaveArchetype = settings.QuickSaveArchetypeCollection.Definitions.Find(p => p.Name == authoring.QuickSaveArchetypeName);
                fullTypeNames = quickSaveArchetype != null ? quickSaveArchetype.FullTypeNames : new List<string>();
            }

            if (fullTypeNames.Count == 0)
                return;
            
            QuickSaveSettings.Initialize(); // This needs to be here because we're in baking & it's not guaranteed to be initialized

            var entity = GetEntity(TransformUsageFlags.None);
            // Add 2 uninitialized components that will get set by the baking system
            AddComponent(entity, new LocalIndexInContainer()
            {
                LocalIndex = -1
            });
            AddSharedComponent(entity, new QuickSaveArchetypeIndexInContainer
            {
                IndexInContainer = ushort.MaxValue
            });
            
            // Add the baking only components
            var bakingOnlyBuffer = AddBuffer<QuickSaveTypeHandlesBakingOnly>(entity);
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
            AddComponent(entity, dataLayoutHash);
        }
    }
    
}