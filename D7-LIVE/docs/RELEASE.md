# Release and Update Policy

D7 LIVE uses semantic versions. Tagged versions trigger Windows CI.

Production updater requirements:

1. HTTPS manifest.
2. Semantic version comparison.
3. SHA-256 package verification.
4. Ed25519 signature verification using a public key embedded in the application.
5. No update installation while live.
6. Profiles and scenes live outside the installation directory.
7. Failed startup validation must not delete user data.

Never commit the update signing private key to GitHub. Keep signing credentials in the release environment or GitHub Actions secrets.
