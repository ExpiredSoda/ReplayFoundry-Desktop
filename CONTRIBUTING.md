# Contributing

Replay Foundry is in a private release-preparation phase. External contributions are not yet open, but the repository is being structured for a public contribution workflow.

Changes must keep the application local-first, preserve explicit upload consent, avoid committing media/models/runtime payloads or secrets, and add focused verification for behavior changes. A change is ready for review only when the solution builds with zero warnings, the relevant executable test harnesses pass, formatting is clean, and the repository payload/security guards remain green.

Use a focused branch and keep commits reviewable. Do not combine generated artifacts, broad mechanical rewrites, and behavior changes in one commit. Security reports follow [SECURITY.md](SECURITY.md), not the normal issue workflow.
