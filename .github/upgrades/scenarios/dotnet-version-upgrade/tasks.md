# .NET Version Upgrade Progress

## Overview

Upgrading the UA Samples WinForms projects and their shared libraries from .NET Framework 4.8 to `net10.0-windows`, using a Bottom-Up (dependency-first) strategy: foundation libraries first, then the sample and workshop applications, with per-tier validation.
**Progress**: 0/7 tasks complete <progress value="0" max="100"></progress> 0%
**Progress**: 0/7 tasks complete <progress value="0" max="100"></progress> 0%

## Tasks
- 🔄 01-prerequisites: Verify toolchain and target framework readiness ([Content](tasks/01-prerequisites/task.md))
- 🔲 01-prerequisites: Verify toolchain and target framework readiness
- 🔲 02-sdk-style-conversion: Convert legacy csproj files to SDK-style (on net48)
- 🔲 03-foundation-libraries: Retarget shared libraries to net10.0-windows
- 🔲 04-sample-applications: Retarget Samples WinForms apps to net10.0-windows
- 🔲 05-workshop-applications: Retarget Workshop WinForms apps to net10.0-windows
- 🔲 06-incompatible-package-resolution: Resolve deferred incompatible package in ConsoleAggregationServer
- 🔲 07-final-validation: Full-solution validation and deferred recommendations
