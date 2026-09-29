# ADR 0003: Lease-based job claiming

**Status:** Accepted

Use atomic SQL claims plus renewable leases. Permanent locks cannot recover from process death. A lease permits another instance to reclaim work after expiry. Completion requires matching lease ownership to prevent stale writes.
