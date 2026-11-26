using System;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Rendering;

namespace AnimeCS.Play
{
    internal struct SurfaceMark : IComponentData
    {
        public Matrix4x4 Matrix;
    }

    /// <summary>Fixed archetype chunks double as the GPU instance input. Hits only write the next slot.</summary>
    internal sealed class SurfaceMarkBatch : IDisposable
    {
        private readonly World _world;
        private NativeArray<ArchetypeChunk> _chunks;
        private ComponentTypeHandle<SurfaceMark> _type;
        private readonly int _capacity;
        private readonly Mesh _mesh;
        private readonly Material _material;
        private readonly RenderParams _render;
        private int _chunk, _index, _serial;
        internal int Count { get; private set; }

        internal SurfaceMarkBatch(Material source, int capacity)
        {
            _world = new World("AnimeCS surface marks");
            _capacity = capacity;
            var entities = _world.EntityManager;
            entities.CreateEntity(entities.CreateArchetype(typeof(SurfaceMark)), capacity);
            using var query = entities.CreateEntityQuery(ComponentType.ReadWrite<SurfaceMark>());
            _chunks = query.ToArchetypeChunkArray(Allocator.Persistent);
            _type = entities.GetComponentTypeHandle<SurfaceMark>(false);
            _mesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
            _material = new Material(source) { enableInstancing = true };
            _render = new RenderParams(_material) { shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false };
        }

        internal void Add(Vector3 position, Vector3 normal, float size)
        {
            var marks = _chunks[_chunk].GetNativeArray(ref _type);
            var rotation = Quaternion.LookRotation(-normal) * Quaternion.Euler(0, 0, (_serial+1) * 137.5f);
            marks[_index++] = new SurfaceMark
            {
                Matrix = Matrix4x4.TRS(position + normal * .009f, rotation, Vector3.one * size)
            };
            if (_index == marks.Length) { _index=0; _chunk=(_chunk+1)%_chunks.Length; }
            _serial=(_serial+1)%_capacity;
            Count = Math.Min(_capacity, Count + 1);
        }

        internal void Draw()
        {
            if (Count == 0 || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return;
            int remaining=Count;
            for (int i=0; remaining>0; i++)
            {
                var instances=_chunks[i].GetNativeArray(ref _type).Reinterpret<Matrix4x4>();
                int count=Math.Min(remaining,instances.Length);
                if (SystemInfo.supportsInstancing)
                    Graphics.RenderMeshInstanced(_render,_mesh,0,instances,count);
                else
                    for (int j=0; j<count; j++) Graphics.RenderMesh(_render,_mesh,0,instances[j]);
                remaining-=count;
            }
        }

        internal void Reset() { _chunk=_index=_serial=Count=0; }

        public void Dispose()
        {
            if (_chunks.IsCreated) _chunks.Dispose();
            if (_world.IsCreated) _world.Dispose();
            UnityEngine.Object.Destroy(_material);
        }
    }
}
