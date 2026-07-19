# Releasing XIV.fm

XIV.fm uses numeric semantic versions (`MAJOR.MINOR.PATCH`). Git tags use `vMAJOR.MINOR.PATCH`; the generated Dalamud assembly version has a fourth `.0` component.

## Development prerelease

The `Release XIV.fm` GitHub Actions workflow is manually dispatched from `main`. It:

1. Validates that the requested version is numeric, greater than the current project version, and not already tagged.
2. Updates the plugin project version.
3. Downloads the current Dalamud API 15 development distribution.
4. Runs release-tool tests, locked restore, formatting, core tests, and a Release plugin build.
5. Generates `repository/pluginmaster.json` from the packaged manifest.
6. Commits the version and manifest as `release: vMAJOR.MINOR.PATCH`.
7. Creates and pushes an annotated tag.
8. Publishes `latest.zip` as a versioned GitHub release asset.
9. Uploads the same ZIP as a workflow artifact.

Start it from GitHub Actions → **Release XIV.fm** → **Run workflow**, supplying a new version and user-facing notes. Keep **prerelease** enabled until stable-release criteria are complete.

The workflow has narrowly scoped `contents: write` permission because it must commit the generated manifest, push the release tag, and create the release. Other CI remains read-only.

## Public Dalamud repository

Dalamud reads:

```text
https://raw.githubusercontent.com/platis/XIV.fm/main/repository/pluginmaster.json
```

The manifest references an immutable versioned asset:

```text
https://github.com/platis/XIV.fm/releases/download/vMAJOR.MINOR.PATCH/latest.zip
```

Do not manually replace an existing release asset or reuse a version. Dalamud determines updates from `AssemblyVersion`, so every distributed behavior change needs a greater numeric version.

## Checklist

Before dispatching:

1. Start from a clean, current `main` branch.
2. Ensure normal CI is green.
3. Update `CHANGELOG.md` and user documentation in a reviewed commit.
4. Confirm no credentials, private data, generated binaries, or local configuration are staged.
5. Choose a greater numeric version and concise release notes.

After dispatching:

1. Confirm the release workflow and the CI triggered by its metadata commit both pass.
2. Fetch `pluginmaster.json` without GitHub authentication and validate its JSON.
3. Fetch the release ZIP URL without authentication.
4. Install/update through Dalamud using the public custom-repository URL.
5. Verify the plugin loads, `/xivfm status` works, cards track expected characters, and unloading is clean.
6. Record rollback or follow-up findings.

Never place Last.fm, GitHub, registry, or deployment credentials in the repository. The release workflow uses GitHub's short-lived repository token.
