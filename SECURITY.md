# Security policy

CadSpace is early-stage CAD software. It has not undergone an independent security review. Do not use it as the only copy of engineering documents or as a certification tool.

DXF input is treated as untrusted data. The reader rejects binary files, malformed group pairs, nonfinite numeric data, missing EOF markers, duplicate sections, and cyclic block graphs. Imports are capped at 64 MiB by the application, and the codec limits input to 64 Mi characters. Generated arrays, mesh resolutions, block nesting, and hatch lines are bounded. These limits are not a substitute for a complete denial-of-service audit.

Opaque DXF records are data, never executable code. CadSpace does not execute AutoLISP, VBA, scripts, external references, or embedded commands from drawings. The app has no cloud drawing upload or collaboration service.

Report a suspected vulnerability privately to the repository owner before disclosing a reproducible exploit. Include the affected commit, platform, minimal sample, and observed behavior. Do not put confidential drawings, credentials, or production customer data in public issues.
