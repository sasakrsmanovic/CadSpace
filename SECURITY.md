# Security and reliability

CadSpace is a development preview, not independently audited or engineering-certified software. Keep originals and separate backups. Save native projects before closing/refreshing; automatic recovery is not implemented.

ASCII/binary DXF input is untrusted. Transport validates known group types, finite values, encodings and truncation, with a 64 MiB application import limit. Native JSON has explicit size/depth limits and manual type dispatch. Serialized provenance is compared with reparsed original geometry/tables before raw-record reuse. No unrestricted runtime type-name deserialization or drawing-code execution is used.

Geometry, nesting, cycles, arrays, scene expansion, curves, hatches, indexes, mesh operations and GPU atlases have limits. These are not a comprehensive denial-of-service or adversarial-geometry audit. Complex unsupported data remains opaque or produces diagnostics.

The browser RGBA adapter depends on a pinned private Uno field and must be requalified during framework upgrades. It does not patch global browser graphics APIs. Physical drivers, context loss and resource lifetimes need broader qualification.

The application does not execute AutoLISP, VBA, embedded scripts or xref code, and provides no cloud drawing-upload/collaboration service. Python/ezdxf are test-only dependencies. Report vulnerabilities privately with a nonconfidential minimal fixture, commit and platform; never post credentials or customer engineering files in public issues.
