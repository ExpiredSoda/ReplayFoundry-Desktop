# Security policy

## Reporting a vulnerability

Please do not open a public issue for a suspected vulnerability or include credentials, private media, transcripts, access tokens, personal information, or exploit details in a public discussion.

Use the private contact route on the [Replay Foundry support page](https://replayfoundry.com/support). If GitHub private vulnerability reporting is available for the repository, that is also an appropriate route. Do not disclose the report through a public issue or discussion.

Include the affected Replay Foundry version, Windows version, a concise reproduction, and the least-sensitive diagnostics that demonstrate the problem. Replace personal paths and account identifiers. Do not attach source videos unless an encrypted transfer has been explicitly arranged.

## Application diagnostics

Replay Foundry keeps crash and diagnostic reports local by default. A report is sent only after the user reviews and explicitly submits it. The reporting boundary strips credentials, environment secrets, private filesystem roots, raw media, transcripts, prompts, and unbounded provider output. Reports that cannot pass the sanitizer remain local.

## Supported versions

During public beta, only the latest published beta receives security fixes. Development and locally generated unsigned builds are unsupported outside their explicit test scope.
