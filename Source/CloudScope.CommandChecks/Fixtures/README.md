# E57 interoperability fixture

`two-scans.e57` was generated independently with pye57/libE57Format, not with CloudScope. It contains two scans with numeric bitpack records, RGB, intensity and an invalid Cartesian record in each scan. Poses include rotation and translation. Four valid points remain; their combined world-space bounding-box center is (506.5, 1010.5, 1520). ExchangeChecks verifies that result and rejects a copy with a modified checksummed byte.

The file is synthetic test data and contains no survey or personal data. Primary codec reference: https://github.com/asmaloney/libE57Format.

## Surface reconstruction parity

`surface-reference.json` records outputs produced by running the unmodified upstream `src/engine/pointcloud/SurfaceReconstruction.ts` at commit `b2b6cf235763d7efa895b68bd1dc775ae494d672` from https://github.com/OpenAEC-Foundation/open-pointcloud-studio. The inputs are synthetic curved samples, with/without a remote point, and automatic/explicit maximum edge lengths. Tests compare triangle index order and winding exactly, and normals within 1e-5.

Regenerate with Node 24+: `node Source/CloudScope.CommandChecks/Fixtures/tools/generate-surface-reference.mjs /path/to/open-pointcloud-studio/src/engine/pointcloud/SurfaceReconstruction.ts`. The upstream implementation itself is not redistributed here. Its license is LGPL-3.0-or-later.

Deliberate host adaptations: CloudScope's fallback normal is Z-up; zero-area faces are rejected; cancellation is checked within grid construction, neighbour searches and reconstruction loops. The remote point in these fixtures still finds enough neighbours within the upstream five-cell search radius.
