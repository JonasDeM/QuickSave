![quicksavebanner](https://user-images.githubusercontent.com/23634827/218306750-7084d9a7-b36d-42c8-872b-dc86b690cfb5.png)
*For any commercial work you can buy the asset on the Unity Asset Store. (coming soon)  
This repository comes with a restrictive [non-commercial license](LICENSE.MD).*

## In Short
This Unity DOTS-related package enables you to asynchronously load and save state on entities without the need for any feature-specific code.  
The highly optimized implementation makes it a solid foundation for ambitious features since it can easily run on every frame.

## Getting Started
* [Package Manual](PackageManual.md)
* [Demo Project](https://github.com/JonasDeM/QuickSaveDemo)  

## Potential Use Cases
Depending on what state you choose to save, anything is possible.  
You can choose to save the whole state of your application (given it is in ECS), maybe you choose to only save a handful of gameplay entities for a certain game mechanic, or you can do anything inbetween!  
Here are some ideas:
* Replay System
* Level Reset without reloading scenes
* World Streaming & World State Saving
* Time-rewind Gameplay
* User Generated Content
* QA Testing & Bug Reproduction
* Networking

## Project Pillars

#### Easy to Use
* Stores the state from any set amount of entities into one simple array
* No need for any state-specific saving or loading code
* ECS API with all the benefits of Unity's safety systems

#### Performant
* All code is Burst compiled & most of the work is done in async jobs
* Single schedule call per container

#### Scalable
* Partitions savedata by subscenes (or by any other GUID)
* Stores user-defined state in binary format
* (Planned for 2.0) LZ4 + Delta Compression

#### Extendable
* Make & Manage your own QuickSaveSystems
* Make & Manage your own Containers

## Current Limitations
* No support for quicksaving SharedComponentData, ChunkComponentData or any components with references/pointers.  
The Package Manual helps with working with this limitation.  
Support for this is technically possible, but would come with additional complexity & loss of performance.  
* No support for quicksaving destruction/creation of entities. Use the 'Disabled' component & quicksave that.  
The QuickSave Demo Project gives an example on how to implement a constant-size pool for dynamically spawnable prefabs.  
Supporting variable-sized containers is being looked at, but not guaranteed to see the light of day.

## Goals for 2.0 release:
* Compression in the default serialization implementation.
* Versioning & Upgradability of serialized state.
* Improved support for dynamically spawned entities.
* Inspector for containers.
