# QuickSave Package Manual
This is a comprehensive guide on how to use the package in your own project.  
You can use it together with the [Demo Project](https://github.com/JonasDeM/QuickSaveDemo) to learn how to use the package.

## Setup
To install the package you can just put it in your projects Packages folder.  
An alternative is to install it via the Unity Package Manager via the git URL. (It's possible to specify an exact commit in the URL.)

Add the QuickSaveAuthoring component to a gameobject in a SubScene and click the 'Create QuickSave Settings'.

## Getting Started
### Defining what **CAN** get saved
If the Initial Setup was followed there will now be 2 assets in the 'Assets/QuickSave/Resources/QuickSave' folder.  
On the [QuickSaveSettingsAsset](QuickSave/QuickSaveSettingsAsset.cs) you will define every ECS component that you might want to save & load.  
For BufferElementData you'll have to define the max amount of elements that can be saved.  
On the [QuickSaveArchetypeCollection](QuickSave/QuickSaveArchetypeCollection.cs) asset you can optionally define named combinations of types.  
These definitions are useful to reduce authoring work, add consistency and make later modifications easier.  


![image](https://github.com/JonasDeM/QuickSave/assets/23634827/3d5cec80-6525-4b72-9e63-cfac7e1db354)


QuickSave also tracks if the component is enabled or not (see IEnableableComponent).  
For IComponentData it tracks whether the component is on the entity or not (especially useful for TagComponents).  
For IBufferElementData there's no tracking for the component being added or not, BUT it tracks the amount of entries and their data. You'll have to empty you buffers instead of removing them!
The reason for this is performance related.  


_There are restrictions to what can get saved. ComponentData & BufferElementData are supported as long as they don't container pointers, blobassetreferences or references to other entities.
The restrictions are there because it doesn't make sense to persist any of these things in their raw form.
If for example your pointer field is part of the state of your entity, it's up to the user to quicksave some kind of serializable field like a GUID and quicksave that instead. 
The user can then make a system that automatically fixes up the pointer based on whatever the GUID is._

### Defining what **WILL** get saved
With QuickSave you can decide per Authoring Gameobject (and thus per Entity) what state gets saved specifically.  
This doesn't mean that there should be lots of authoring work though.  
The authoring component [QuickSaveAuthoring](QuickSave/QuickSaveAuthoring.cs) can get added to Prefabs & multi-object editing is fully supported.  


[QuickSaveAuthoring](QuickSave/QuickSaveAuthoring.cs) should get added to any authoring gameobject for which their resulting Entity you want to save & load their state.
The options in the inspector are what you defined in the previous step 'Defining what can get saved'. You can select one of the presets you made (Preset is the same as a QuickSaveArchetype).
Or you can make a custom combination of types directly on the component.


![image](https://github.com/JonasDeM/QuickSave/assets/23634827/97a5b53f-e292-4cae-afd6-9a7a74826406)


### Testing it out
To make this first test simple I recommend using a physics object & saving at least the LocalTransform + PhysicsVelocity.  


When you added a [QuickSaveAuthoring](QuickSave/QuickSaveAuthoring.cs) component with some definition of what to save, you can close the subscene.  
QuickSave has a useful utility component [QuickSaveSubScene](QuickSave/QuickSaveSubScene.cs), add this to your subscene gameobject.  

You can now enter playmode and use the [QuickSaveSubScene inspector](QuickSave.Editor/QuickSaveSubSceneInspector.cs) on the subscene to 'Reset To Initial State', you can also use it to load & unload your subscene.
Resetting a SubScene to its initial state is the only feature that works out of the box with 0 coding.


![resetGif](https://github.com/JonasDeM/QuickSave/assets/23634827/ddae8948-0eae-4af5-90e9-3c78bcbf8752)


### What's next

In the next parts we delve into more detail on how QuickSave works and how it can be used.  
The [Demo Project](https://github.com/JonasDeM/QuickSaveDemo) also shows these things and how to:
* Save/Load the state of a subscene at any time.
* Save the state to a file & load it from a file.
* Create a Replay System
  
There's even more than that you can do, but that's up to the creativity of the developer!  
Once you learn the API, there's little limits to it.  

## QuickSave Containers
When QuickSave saves the state of your entities it copies the data to a single array per SubScene.
It just looks at the SceneSection component on the entities to decide in which container the data ends up.


QuickSave creates only 1 container automatically, the 'Initial Container', this is where the initial state of the subscene gets stored.
This gets done by the [QuickSaveSceneSystem](QuickSave/QuickSaveSceneSystem.cs) & [QuickSaveBeginFrameSystem](QuickSave/QuickSaveBeginFrameSystem.cs).

A container is just an entity that stores all its data in a buffer component of type [QuickSaveDataContainer.Data](QuickSave/QuickSaveDataContainer.cs). 
It also has the [QuickSaveDataContainer](QuickSave/QuickSaveDataContainer.cs) component with some info about the container.


You can create new containers for a subscene simply by instantiating an existing valid container entity.
When a subscene is loaded the first time, QuickSave automatically creates 1 valid container with the intial state of the subscene.
So to create your first own container you'll need to duplicate/instantiate that entity. 
The [QuickSaveAPI](QuickSave/QuickSaveAPI.cs) class has some handy methods to do this, they are mainly meant to guide the user, but feel free to work with the container entities directly.
If you have the SceneSection, you can get the InitialContainer entity by checking its [QuickSaveSceneSection](QuickSave/QuickSaveSceneComponents.cs) component. 


To apply the data from a container to the entities or to do the reverse you use the [DataTransferRequest](QuickSave/QuickSaveDataContainer.cs) component on a container.

## QuickSave Systems
There are 2 default systems that will execute your [DataTransferRequest](QuickSave/QuickSaveDataContainer.cs): [QuickSaveBeginFrameSystem](QuickSave/QuickSaveBeginFrameSystem.cs) & [QuickSaveEndFrameSystem](QuickSave/QuickSaveEndFrameSystem.cs).  


For common usage these can be enough, but if you want your requests to be executed at another time in the frame you can create your own QuickSaveSystem by simply inheriting from [QuickSaveSystemBase](QuickSave/QuickSaveSystemBase.cs).

## Subscene Loading & Unloading
You can use the Unity component RequestSceneLoaded on SceneSection entities just as you would normally.
[QuickSaveSceneSystem](QuickSave/QuickSaveSceneSystem.cs) will auto-detect when scenes are loaded that use QuickSave.

QuickSave does provide 2 handy components to simplify auto-saving & auto-loading of state.
[AutoApplyOnLoad](QuickSave/QuickSaveSceneComponents.cs) & [AutoPersistOnUnload](QuickSave/QuickSaveSceneComponents.cs).  
If you decide to use [AutoPersistOnUnload](QuickSave/QuickSaveSceneComponents.cs) you will need to unload your subscene by adding the [AutoPersistOnUnload](QuickSave/QuickSaveSceneComponents.cs) component.
This is to give QuickSave time to save the state before the scene gets unloaded.

## Serialization
QuickSave comes with serialization support, so you can write your containers to disk.
The [RequestSerialization](QuickSave/DefaultQuickSaveSerialization.cs) & [RequestDeserialization](QuickSave/DefaultQuickSaveSerialization.cs) components can be added/enabled on a quicksave container for this purpose.


If you need custom serialization you can disable the [DefaultQuickSaveSerializationSystem](QuickSave/DefaultQuickSaveSerialization.cs) & create your own system based on it.


# API Reference
[QuickSaveAuthoring](QuickSave/QuickSaveAuthoring.cs): The authoring component to put on gameobjects in subscenes.  


[QuickSaveBaker](QuickSave.Baking/QuickSaveBaker.cs): Baker for QuickSaveAuthoring


[QuickSaveBakingSystem](QuickSave.Baking/QuickSaveBakingSystem.cs): Finishes the baking from QuickSaveBaker.  


------


[QuickSaveDataContainer](QuickSave/QuickSaveDataContainer.cs): ComponentData on container entities that describes the container.  


[QuickSaveDataContainer.Data](QuickSave/QuickSaveDataContainer.cs): BufferData on container entities that contains all the raw container data.  


[QuickSaveArchetypeDataLayout](QuickSave/QuickSaveDataContainer.cs): BufferData on container entities that described the data layout of the raw data.


[DataTransferRequest](QuickSave/QuickSaveDataContainer.cs): BufferData that can be added to a container to request a QuickSaveSystemBase to apply or save the data.


[QuickSaveAPI.IsInitialContainer](QuickSave/QuickSaveAPI.cs): Utility function to check whether the container is the auto-created initial container for a subscene.


[QuickSaveAPI.InstantiateContainer](QuickSave/QuickSaveAPI.cs): Utility function to duplicate a container.


------


[QuickSaveSystemBase](QuickSave/QuickSaveSystemBase.cs): Abstract class to inherit from when in need of a custom QuickSaveSystem.  


[QuickSaveSceneSystem](QuickSave/QuickSaveSceneSystem.cs): System that detects subscene loads, creates the initial container & does other crucial work on subscene load.  


[QuickSaveSceneSection](QuickSave/QuickSaveSceneComponents.cs) : ComponentData on SceneSections that holds a reference to the initial container entity.  


------


[QuickSaveArchetypeIndexInContainer](QuickSave/IndexInContainer.cs): ComponentData on user entities that holds a primary index into the containerdata array.  


[LocalIndexInContainer](QuickSave/IndexInContainer.cs): ComponentData on user entities that holds a secondary index into the containerdata array.  


[QuickSaveMetaData](QuickSave/IndexInContainer.cs): Small struct that lives in front of every piece of data in the containerdata array.  


------


[QuickSaveSettings](QuickSave/QuickSaveSettings.cs): Static runtime equivalent to the QuickSaveSettingsAsset.  


[QuickSaveSettingsAsset](QuickSave/QuickSaveSettingsAsset.cs): Asset which contains the user defined types & some options.  


[QuickSaveArchetypeCollection](QuickSave/QuickSaveArchetypeCollection.cs): Asset which contains the user defined type combination presets.  
