// Author: Jonas De Maeseneer

using Unity.Entities;

namespace QuickSave
{
    // IMPORTANT: QuickSave only supports saving & applying state for scene section 0 of a subscene!
    
    // Optional component to put on SceneSection entities
    // The state in the specified container will be applied to all relevant entities in the scene section right after it is loaded
    public struct AutoApplyOnLoad : IComponentData
    {
        public Entity ContainerEntityToApply;
    }
    
    // Needs to be used together with RequestSceneUnloaded!
    // Optional component to put on SceneSection entities
    // The state of all relevant entities in the scene section will be saved to the specified container right before the section is unloaded
    public struct AutoPersistOnUnload : IComponentData
    {
        public Entity ContainerEntityToPersist;
    }
    
    // Should be used together with AutoPersistOnUnload
    // Adding this component to a scene section will result in it being unloaded, the standard way of doing this is to remove the RequestSceneLoaded component.
    // But using this component enables the QuickSaveEndFrameSystem to save the state of the scene section right before it is unloaded.
    public struct RequestSceneUnloaded : IComponentData
    {
        
    }
    
    // This indicates a scene section that was loaded at some point & is tracked by QuickSave
    // If the scene section entity is destroyed, the QuickSaveSceneSystem will automatically destroy the initial container it created.
    // All other containers are owned by the user, but since the QuickSaveSceneSystem auto-creates the initial containers it will also handle their destruction.
    public struct QuickSaveSceneSection : ICleanupComponentData
    {
        public Entity InitialStateContainerEntity;
    }
    
    // Each scene section with persisting entities will have a singleton entity with this component.
    // It contains info to quickly construct QuickSaveArchetypeDataLayout at runtime
    public struct QuickSaveSceneInfoRef : IComponentData
    {
        public BlobAssetReference<QuickSaveSceneInfo> InfoRef;
    }
    
    public struct QuickSaveSceneInfo
    {
        public BlobArray<QuickSaveTypeHandle> AllUniqueTypeHandles;
        internal BlobArray<QuickSaveArchetypesInScene> QuickSaveArchetypesInScene;
        public Hash128 SceneGUID;
        public ulong DataLayoutHash;
    }
    
    internal struct QuickSaveArchetypesInScene
    {
        public BlobArray<QuickSaveTypeHandle> QuickSaveTypeHandles;
        
        // This is the amount of entities with this QuickSaveArchetype in the scene
        public int AmountEntities;
    }
}