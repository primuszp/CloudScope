# Large Point Cloud Rendering Plan

## Current Constraints

- OpenGL uploads one flat resident `PointData[]` buffer; Metal splits the resident set into one-million-point GPU buffers.
- Frame-time reduction is currently a prefix draw budget over a progressive render-order GPU buffer: OpenGL and Metal draw points from index `0` to `drawCount`, but the buffer prefix is now sampled instead of source-record ordered.
- The source array anchors labels, instances, LAS class export, and attribute arrays, so it is never reordered.
- Filters rebuild `ViewPoints`, rebuild `ViewToSource`, recolor on CPU, and reupload the whole visible point buffer. Color-source changes now keep the current view/map/render order and only recolor the visible point buffer before reupload.
- Metal and OpenGL cap resident/drawn points through shared, environment-tunable limits. The progressive render order stores at most five million indices instead of allocating an entry for every loaded point. This is still a coarse resident cap, not spatial GPU residency.
- An absent `ViewToSource` map means identity for an unfiltered cloud. The explicit map is allocated only for filtered views, avoiding another four bytes per loaded point in the common path.

## Phase 1: Make Existing Budget Correct

1. Done: add a render-order indirection to `PointCloudDataset`.
   Keep `SourcePoints` in LAS/source order, and create a separate progressive `int[] RenderOrder` or GPU index buffer for overview rendering.
2. Done: build a compact deterministic permutation prefix of source indices.
   A coprime modular stride visits source indices without repetition and distributes the overview across the input while preserving stable source indices. Only the GPU-resident prefix is stored, reducing render-order memory from four bytes per loaded point to at most about 20 MB.
3. Done: teach `IPointCloudRenderer.Upload` to accept a render descriptor.
   The current implementation uploads GPU point buffers in render order. The descriptor already carries attributes and source maps for the shader-color path.
4. Done: apply the same render-order path after filters.
   Filtered clouds should sample uniformly from the filtered source-index set, not from spatially or file-ordered prefixes.
5. Done: add diagnostics for loaded, visible, resident, and drawn counts per frame/backend.

## Phase 2: Stop Reuploading For Color Changes

0. Started: avoid rebuilding filter maps and render order on pure color-source changes.
   No-op color-source requests now skip recolor and upload. OpenGL attribute-backed views now switch RGB/Height/Class/Intensity/Return through a shader uniform without CPU recolor or GPU point-buffer reupload.
1. Started: split point storage into mostly immutable geometry plus mutable/lightweight attributes.
   OpenGL now uploads a separate per-point attribute buffer for Z, intensity, class, return, and original RGB.
2. Done: move color-source switching into shaders.
   OpenGL and Metal shade RGB, height, class, intensity, and return from uploaded attribute buffers.
3. Done: reduce CPU recolor work while filtering.
   Attribute-capable renderers switch color source without rebuilding the filtered view or reuploading point geometry.
4. Update annotation rendering incrementally.
   Selections should update sparse annotation buffers or dirty ranges instead of rebuilding a full highlight point buffer.
5. Keep the old CPU-colored path only as a fallback for backends that do not support the attribute shader path.

## Phase 3: Spatial LOD And Culling

0. Started: route OpenGL and Metal resident point preparation through `PointRenderUploadBuilder`.
   Render-order resolution and chunk boundaries now have one shared implementation. Spatial ordering can be added here without creating separate backend algorithms.
0a. Done: pool OpenGL upload scratch buffers.
   Ordered point and attribute uploads use `ArrayPool<T>` instead of allocating up to roughly 260 MB of short-lived managed arrays for a five-million-point resident set. Metal continues to write directly into managed GPU buffers.
0b. Done: add a shared chunk draw planner.
   Both backends build identical one-million-point chunk bounds, apply the same conservative viewport-frustum test, and distribute the frame budget over visible chunks without per-frame allocations. Chunk bounds become more selective once spatial ordering replaces the current progressive upload order.
1. Build a chunked spatial hierarchy during/after load.
   A loose octree or fixed grid with Morton ordering is sufficient; each leaf owns a compact source-index span and a local bounding box.
2. Store multiple LOD payloads per chunk.
   Examples: coarse blue-noise sample, medium voxel representatives, full points. Use projected screen size and point budget to choose LOD.
3. Traverse visible chunks each frame.
   Frustum-cull nodes, estimate projected density, and enqueue draw commands until the frame budget is filled.
4. Add GPU residency management.
   Keep visible/near chunks in an LRU cache; stream chunk buffers asynchronously; render lower LOD while full chunks upload.
5. Use local chunk coordinates.
   Store chunk origin plus quantized 16-bit or 24-bit local positions where quality allows, reducing VRAM and upload bandwidth.

## Phase 4: Selection And Annotation At Scale

1. Reuse the spatial hierarchy for selection queries.
   Reject whole nodes by selection-volume bounds, and only test points inside intersecting leaves.
2. Started: make preview resolution budgeted.
   Preview now aborts stale jobs when the selection volume moves and runs only the latest pending query. Progressive/chunked preview still depends on spatial indexing.
3. Store annotations by source index in a sparse structure.
   Render per-instance colors from annotation buffers; export semantic class codes to LAS from the same source-index map.
4. Add an instance-aware color mode.
   Keep semantic class coloring available, and add an instance-color overlay or `COLORBY Instance` once instance attributes are available on the GPU path.

## Phase 5: Backend-Specific Optimizations

1. OpenGL:
   - Use persistent mapped buffers or orphaning/ring buffers for streaming chunks.
   - Prefer multi-draw or indirect draw commands for many visible chunks.
   - Done: add a resident point cap symmetric with Metal and report resident/drawn counts through frame diagnostics.
2. Metal:
   - Keep chunk buffers in managed/private storage where appropriate.
   - Batch visible chunk draws through indirect command buffers once the chunk path is stable.
   - Replace the single global resident cap with an LRU chunk budget.

## Acceptance Targets

- 50M loaded points can open without a full-view GPU upload stall after every color change.
- Zoomed-out views show an unbiased spatial overview instead of the first source-record prefix.
- Panning/zooming stays interactive by drawing bounded visible LOD chunks.
- Semantic label and instance ID annotations remain keyed to original source indices and survive filters, color changes, and exports.
