# Large Point Cloud Rendering Plan

## Current Constraints

- The renderer uploads one flat `PointData[]` buffer per visible view.
- Frame-time reduction is currently a prefix draw budget over a progressive render-order GPU buffer: OpenGL and Metal draw points from index `0` to `drawCount`, but the buffer prefix is now sampled instead of source-record ordered.
- `PointCloudLoader.PrepareProgressiveSubsample` exists, but the loaded source array now also anchors labels, instances, LAS class export, and attribute arrays. Reordering source points directly would break stable source-index semantics unless every dependent array and map is updated together.
- Filters rebuild `ViewPoints`, rebuild `ViewToSource`, recolor on CPU, and reupload the whole visible point buffer. Color-source changes now keep the current view/map/render order and only recolor the visible point buffer before reupload.
- Metal and OpenGL cap resident/drawn points by environment-tunable constants. This is still a coarse whole-buffer cap, not chunked GPU residency.

## Phase 1: Make Existing Budget Correct

1. Done: add a render-order indirection to `PointCloudDataset`.
   Keep `SourcePoints` in LAS/source order, and create a separate progressive `int[] RenderOrder` or GPU index buffer for overview rendering.
2. Done: build the first render-order prefix as a deterministic uniform sample of source indices.
   This replaces the biased "first N records" overview while preserving stable source indices for labels and LAS export.
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
2. Started: move color-source switching into shaders.
   OpenGL can shade RGB, height, class, intensity, and return from the uploaded attribute buffer. Metal still uses the CPU-colored fallback.
3. Update annotation rendering incrementally.
   Selections should update sparse annotation buffers or dirty ranges instead of rebuilding a full highlight point buffer.
4. Keep the old CPU-colored path only as a fallback for backends that do not support the attribute shader path.

## Phase 3: Spatial LOD And Culling

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
2. Make preview resolution budgeted.
   Preview should update progressively and abort stale jobs when the selection volume moves.
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
