# Contributing to TPFan-Win

## Development Workflow

### Branch Strategy
- main - Stable, production-ready code
- develop - Integration branch for features
- feature/* - New features
- bugfix/* - Bug fixes
- release/* - Release preparation

### Commit Convention
Follow conventional commits:
```
feat: add system tray integration
fix: resolve WMI query timeout
docs: update architecture documentation
test: add unit tests for fan curve interpolation
chore: update NuGet packages
```

### Pull Request Process
1. Create feature branch from develop
2. Make changes and commit
3. Push branch to GitHub
4. Create PR to develop
5. Wait for CI checks to pass (build + PR validation)
6. Request review (CODEOWNERS will be notified automatically)
7. Merge after approval

## CI/CD

### Workflows
- build.yml - Builds and publishes the single-binary `TPFan.GUI.exe` on every push/PR
- codeql.yml - Security analysis (weekly + on push)
- pr-check.yml - Validates PR title format
- stale.yml - Auto-closes inactive issues/PRs

All builds run on GitHub Actions so you don't need to build locally on T480.

See [Release Process](docs/RELEASE_PROCESS.md) for release workflow details.

## Code Standards

### C# Style
- Use file-scoped namespaces
- Use record types for immutable data
- Prefer async/await over .Result
- Use explicit types for clarity
- Add XML documentation for public APIs

### Testing
- Write unit tests for business logic
- Test on actual T480 hardware when possible
- Document hardware-specific behaviors

## Hardware Testing

Since this project targets ThinkPad T480:
- Test WMI queries on actual hardware
- Verify fan curve detection accuracy
- Check ACPI control stability
- Monitor for thermal issues

## Dependency Updates

Dependabot automatically creates PRs for:
- NuGet packages (weekly on Sunday)
- GitHub Actions (weekly on Sunday)

Review and merge when CI passes.

## Questions?

Open an issue for discussion before major changes.
