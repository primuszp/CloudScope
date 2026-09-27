# E57 interoperability fixture

`two-scans.e57` was generated independently with pye57/libE57Format, not with CloudScope. It contains two scans with numeric bitpack records, RGB, intensity and an invalid Cartesian record in each scan. Poses include rotation and translation. Four valid points remain; their combined world-space bounding-box center is (506.5, 1010.5, 1520). ExchangeChecks verifies that result and rejects a copy with a modified checksummed byte.

The file is synthetic test data and contains no survey or personal data. Primary codec reference: https://github.com/asmaloney/libE57Format.
