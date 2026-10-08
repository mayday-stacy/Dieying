# Publishing Dieying source and builds

Dieying is an independent fork of Composa, maintained in [mayday-stacy/Dieying](https://github.com/mayday-stacy/Dieying). Publishing its source repository, sharing an unsigned development ZIP and shipping a signed release are separate operations. The source can be published with the current documented feature boundaries; a Photoshop-complete editor or Windows installer is not a prerequisite.

The supported packaging entry point is `scripts/windows.ps1`. Upstream Linux/Windows installers and public Release automation remain disabled because they carry Composa's product identity. Never publish a Dieying binary with Composa's installer AppId, file associations or release links.

## Before the first source push

1. Review the staged diff, including untracked source files. Keep the inherited MIT notices, third-party notices, model licence information and provenance headers. Preserve the upstream Git history and tags so attribution and MinVer remain traceable.
2. Exclude `artifacts/`, `dist/`, `bin/`, `obj/`, downloaded MODNet weights, local settings, credentials, private images and personal test documents. The two small committed model files are part of the source; the fetch script validates the third model by pinned size and SHA-256.
3. Run the validation commands below from the intended commit's source. Read the actual test counts and failures; an exit code alone is not proof that tests ran. Record the environment and any manual checks separately from generated test logs.
4. Confirm the destination is `mayday-stacy/Dieying` and the intended visibility is public. Authenticate as the repository owner or an authorized collaborator. Keep `dvdstelt/Composa` as an upstream reference rather than a push destination; do not infer the owner from a different connected account.
5. Commit the reviewed files and push the development branch to the independent repository. Inspect the resulting source and CI results on GitHub. A failed or skipped workflow is not a passing check. Do not create a release or upload local test reports merely to make the repository look complete.

Do not commit a hardcoded version or invent a release tag for the initial source push. A repository upload does not change the version policy below.

## Validation and portable packaging

From the repository root in PowerShell with the SDK selected by `global.json`:

```powershell
.\scripts\windows.ps1 -Action Models
.\scripts\windows.ps1 -Action Test
.\scripts\tests\windows-test-reports.ps1
.\scripts\windows.ps1 -Action Publish -Runtime win-x64 -Zip
```

`Test` runs the core and desktop suites separately. Each run receives a unique `artifacts/windows-tests/` directory containing console logs and TRX reports; both projects must execute tests and pass. Update tests provide fake release/network environments, and all script builds use the local update channel.

`Publish` creates a new portable folder in `dist/`, a ZIP and its `.zip.sha256` sidecar. It validates the three model files, includes .NET, image libraries and licence notices, and removes debug symbols. No file is uploaded, no installer is built and previous output directories are left alone. `-Runtime win-arm64` builds for Windows on Arm but does not substitute for testing on that hardware.

Inspect the package before sharing it:

- Extract into a fresh folder and launch `dieying.exe` with the whole folder present.
- Confirm the Dieying window name, About attribution, local update message and independent settings/recovery paths. Keep the `LICENSE`, `THIRD-PARTY-NOTICES.txt` and `ImageMagick-NOTICE.txt` files with the executable.
- Open a representative image, add Chinese text with a real Windows IME, compose layers and masks, save/reopen `.cmps`, and export PNG/JPEG/WebP. Check a cancel and an undo/redo path, not only successful input.
- Compare `Get-FileHash -Algorithm SHA256 <zip-path>` with the sidecar. A checksum verifies file integrity, not publisher trust. Describe the package as unsigned while that remains true.

The Packages workflow provides a Windows portable build from the checked-out source; its artifacts are development output. Consult the workflow's current triggers and results rather than assuming every push produces a package. Linux CI validates source compatibility, not an independent Dieying Linux distribution.

## Versioning

MinVer derives assembly versions from the nearest Git tag. Untagged commits normally build as a prerelease of the next patch. `AppInfo.Version` reads that version for the application; do not hand-edit assembly, project or packaging version numbers. CI checkouts must use `fetch-depth: 0` so MinVer can see the history and tags.

The repository inherited Composa's `v1.4.0` history. Those tags describe upstream versions and are not historical Dieying releases. Choose and document the first Dieying release version when an actual release is prepared, rather than retagging upstream commits or claiming a development package is a stable release.

## Preparing a future binary release

Public Release automation is intentionally disabled. Before enabling it:

1. Configure Dieying's own repository and release asset naming, including the application's allowlist and checksum verification. Keep `UpdateChannel=local` until that path has tests against the intended source.
2. Choose the supported binary formats. A portable-only release is valid; if adding an installer, create an independent stable AppId and file associations and test upgrade/uninstall behavior alongside Composa. Do not reuse the inherited installer identity.
3. Establish the Windows signing and verification process for the executable and native libraries if shipping trusted signed builds. Do not describe a self-signed build or a ZIP hash as trusted signing.
4. Move the appropriate Dieying entries from `CHANGELOG.md` into the chosen release section. Keep older Composa entries labelled as upstream history.
5. Prepare a **draft** release for the reviewed commit and intended tag, build and attach the expected files and checksum manifest, and test the downloaded files. Publish only when the assets and notes agree. If a draft build needs a version override before its tag exists, use MinVer's supported build override rather than editing versions in source.

Do not push a tag from a terminal just to trigger the inherited release workflow. No current workflow should publish an empty release or send Dieying users to upstream binaries. If release preparation fails, keep the draft unpublished and fix the failure before continuing.
