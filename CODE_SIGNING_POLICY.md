# Code signing policy

Free code signing provided by [SignPath.io](https://signpath.io/), certificate by [SignPath Foundation](https://signpath.org/), subject to project acceptance.

## Source repository

https://github.com/dirreno-GH/Velvet

## Roles

- Committer and reviewer: [dirreno-GH](https://github.com/dirreno-GH)
- Signing approver: [dirreno-GH](https://github.com/dirreno-GH)

Every release intended for trusted signing must be built from this repository
by the automated build workflow. Signed binaries must not be modified after
signing.

## Privacy policy

See [PRIVACY.md](PRIVACY.md).

This program will not transfer information to other networked systems unless
specifically requested by the user or the person installing or operating it.

## Build integrity

- Source is public.
- Windows builds run in GitHub Actions.
- SHA-256 checksums are generated automatically.
- GitHub build-provenance attestations are generated for release executables.
- Release metadata uses the Velvet product identity consistently.

## SignPath status

The repository is prepared for an application to SignPath Foundation. Trusted
SignPath signing will be enabled only after SignPath accepts the project and
provides the required project/signing configuration.
