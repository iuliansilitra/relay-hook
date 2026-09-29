# Packaging and release

## Package architecture

RelayHook publishes four assembly packages:

```text
RelayHook.AspNetCore
├── RelayHook.Core
├── RelayHook.Authentication ── RelayHook.Core
└── RelayHook.SqlServer ─────── RelayHook.Core
```

`RelayHook.AspNetCore` is the recommended installation for normal ASP.NET Core and SQL Server applications. Lower-level packages remain independently versioned together so future hosts or storage providers do not require an ASP.NET Core dependency. No meta package is needed because `RelayHook.AspNetCore` already provides one-package installation without duplicating assemblies.

## Framework

Packages target `net10.0`. .NET 10 is the active LTS release through November 2028. Multi-targeting is intentionally omitted: .NET 8 is near end of support, and extra targets would multiply compatibility and test work without a current support commitment.

## Versioning

`Directory.Build.props` provides one repository-wide development version, `0.1.0-alpha.1`. Supply an intentional SemVer at pack time:

```bash
dotnet pack RelayHook.slnx -c Release -p:Version=0.2.0-beta.1
```

All four packages receive the same version. The SDK derives assembly and file versions from that value and appends source revision information to `InformationalVersion` when Git metadata is available. Release workflow derives package version from a validated `vMAJOR.MINOR.PATCH[-prerelease]` tag.

## Package metadata and symbols

Common metadata, deterministic compiler output, repository URL, portable PDB settings, XML documentation, README packing, and symbol-package generation are centralized in `Directory.Build.props`. Release packages go to `artifacts/packages/`.

.NET SDK Source Link embeds repository/source mapping and source revision data in portable PDBs. NuGet packages contain assemblies, XML documentation, README, and generated metadata. Symbol packages contain matching portable PDBs and source files needed for debugging.

## API compatibility

Package validation is not enabled before the first stable baseline exists. After publishing a stable release, enable `EnablePackageValidation` and set `PackageValidationBaselineVersion` to the previous stable package version in release builds. This avoids creating a false baseline from an unpublished pre-1.0 package.

## Dependency locking and updates

Library lock files are not committed. Central package management provides one direct-version source, while consumers remain free to resolve compatible transitive updates. CI runs restore plus vulnerability review. Major dependency upgrades require focused compatibility testing; an available newer version is not sufficient reason alone.

## Signing

No author-signing certificate is configured. NuGet.org repository signing provides integrity for packages published there. Add author signing only when distribution policy supplies a protected certificate and timestamping process; never store signing keys in the repository.

## CI and publishing

`.github/workflows/build.yml` restores, audits, builds, tests, packs, and uploads package artifacts without publishing. `.github/workflows/publish.yml` runs only for version tags, validates tag format, rebuilds/tests, and pushes with secret `NUGET_API_KEY`. Publishing uses `--skip-duplicate` and is never triggered by pull requests.

Before first release, confirm NuGet package ID ownership and repository access, create a scoped NuGet.org API key, and choose the initial public SemVer.
