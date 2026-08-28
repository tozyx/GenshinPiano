# Update package signing

GenshinPiano release ZIP files are authenticated with an RSA-3072/SHA-256
detached signature. The application contains only the public key. The private
key must never be committed, copied into a release ZIP, or uploaded with a
release.

## Current key

The private key generated for this checkout is outside the repository:

`D:\java\GenshinPiano-update-signing-private.xml`

Back this file up in an encrypted, access-controlled location. Losing it means
already released clients cannot authenticate future update packages. Anyone
who obtains it can produce packages trusted by those clients.

The corresponding public key is committed at:

`src\GenshinPiano.App\Assets\Security\UpdateSigningPublicKey.xml`

## Configure a publishing terminal

Set the private-key path before running either release script:

```powershell
$env:GENSHINPIANO_UPDATE_SIGNING_KEY = 'D:\java\GenshinPiano-update-signing-private.xml'
.\build-release.bat 3.0.1-preview.2
```

To persist it for future terminals:

```powershell
[Environment]::SetEnvironmentVariable(
    'GENSHINPIANO_UPDATE_SIGNING_KEY',
    'D:\java\GenshinPiano-update-signing-private.xml',
    'User')
```

Restart VS Code after setting a persistent environment variable.

## Release assets

Upload all three files produced for each ZIP:

- `GenshinPiano-<version>-win-x64.zip`
- `GenshinPiano-<version>-win-x64.zip.sha256`
- `GenshinPiano-<version>-win-x64.zip.sig`

The framework-dependent build uses the same three-file convention with the
`-framework` suffix.

The `.sha256` file detects accidental corruption. The `.sig` file proves that
the ZIP was signed by the project key. Its signed payload binds the signature
protocol version, ZIP file name, and SHA-256 value, preventing a previously
signed ZIP from being renamed and presented as another version. A release
missing either sidecar file is rejected by signed GenshinPiano clients.

## Generate a replacement key

Only rotate the key as part of a planned key-transition release. Generating a
new key and simply replacing the embedded public key breaks updates for every
client that still trusts the old key.

```powershell
.\tools\New-UpdateSigningKey.ps1 `
    -PrivateKeyPath 'D:\secure\GenshinPiano-update-signing-private.xml'
```
