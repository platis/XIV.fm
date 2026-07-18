# Releasing XIV.fm

Use semantic versioning (`MAJOR.MINOR.PATCH`) unless the package ecosystem imposes another format. Git tags and GitHub releases use `vMAJOR.MINOR.PATCH`.

## Checklist

1. Start from a clean, current `main` branch.
2. Run the documented formatter, linter, tests, and production build.
3. Update the changelog and user-facing documentation.
4. Update the package version and lockfiles where applicable.
5. Commit the release as `release: vMAJOR.MINOR.PATCH`.
6. Create an annotated tag: `git tag -a vMAJOR.MINOR.PATCH -m "vMAJOR.MINOR.PATCH"`.
7. Push the commit and tag, then wait for CI to pass.
8. Create the GitHub release from the tag with reviewed release notes.
9. Publish ecosystem packages or deployment artifacts only after the tagged build passes.
10. Verify installation or deployment and record any rollback instructions.

Example GitHub command after CI succeeds:

```bash
gh release create vMAJOR.MINOR.PATCH --verify-tag --generate-notes
```

Never place registry tokens or release credentials in the repository. Use GitHub environments or ecosystem-specific trusted publishing where available.
