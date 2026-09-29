# Security

- HTTPS is required by default. HTTP requires explicit opt-in.
- Operators can restrict callback hosts with `AllowedHosts` or replace `ICallbackUrlPolicy`.
- Literal loopback, link-local, and private IP destinations are blocked unless the host is explicitly allowed.
- Automatic redirects are disabled. Authorization data cannot be forwarded to another host.
- Reserved headers cannot be supplied as custom endpoint headers. Control characters are rejected in custom headers, event names, correlation IDs, idempotency keys, API keys, and bearer tokens before request construction.
- Payload and persisted response sizes are bounded.
- Resolved secrets and authorization header values are not logged or persisted. Non-secret settings and secret references are snapshotted.
- Error text is sanitized and truncated before persistence.

Hostname-to-private-address protection requires environment-specific DNS controls or a custom URL policy. URL validation alone cannot eliminate DNS rebinding.

Correlation IDs and event names are included in structured operational logs. Treat them as non-secret identifiers. Payloads, response bodies, authorization values, resolved secrets, and exception messages are not logged.
