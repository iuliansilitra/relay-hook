# ADR 0002: At-least-once delivery

**Status:** Accepted

RelayHook promises at-least-once delivery. A receiver may accept a request before the sender records completion. Every logical job carries stable callback and idempotency identifiers so receivers can deduplicate.
