# Security policy

## Reporting a vulnerability

Please do not open a public issue for a suspected vulnerability or include credentials, private media, transcripts, access tokens, personal information, or exploit details in a public discussion.

While this repository is private, contact the repository owner through the associated GitHub account. Before the repository becomes public, GitHub private vulnerability reporting and the support address on `replayfoundry.com` will be enabled and this file will be updated with the exact route.

Include the affected Replay Foundry version, Windows version, a concise reproduction, and the least-sensitive diagnostics that demonstrate the problem. Replace personal paths and account identifiers. Do not attach source videos unless an encrypted transfer has been explicitly arranged.

## Application diagnostics

Replay Foundry keeps crash and diagnostic reports local by default. A report is sent only after the user reviews and explicitly submits it. The reporting boundary strips credentials, environment secrets, private filesystem roots, raw media, transcripts, prompts, and unbounded provider output. Reports that cannot pass the sanitizer remain local.

## Supported versions

Only the latest signed production release will receive security updates once public distribution begins. Development and unsigned builds are unsupported outside the private test program.
