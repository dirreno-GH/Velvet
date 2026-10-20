# Security Policy

## Supported version

The current supported release is Velvet Vault 2.1.

## Build verification

Official binaries are built by GitHub Actions from this public repository.
Each build publishes a SHA-256 checksum and a GitHub build-provenance
attestation.

Verify the downloaded executable against `SHA256SUMS.txt` before running it.

## Password vault

The password vault is encrypted locally. Users should use a strong master
password and keep backups of important data.

## Reporting a security issue

Do not post passwords, private vault files, keys, or other secrets in a public
GitHub issue. Report only the minimum technical detail necessary to reproduce
a problem.

## Code signing

The project is being prepared for trusted Authenticode signing. Until a
trusted signing certificate is available, Windows SmartScreen may warn about
the executable even when its SHA-256 checksum and GitHub provenance are valid.
