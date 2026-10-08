# Local HID notification fixes

Upstream: https://github.com/HijelHub/HijelHID_BLEMouse
Original commit: 609d78dcebfd52be9aa8fc057fec5fa7abaf0f4d
License: Apache-2.0 (see LICENSE).

This vendored copy is modified:

- Track Input Report notification subscription through onSubscribe.
- Require an encrypted/paired connection and subscription before notifying.
- Notify only for input changes, plus one initial report after subscription.
- Target the active connection handle explicitly.
- Retain a failed report and retry with 100-1000 ms backoff.
- Drop pending reports across connection/subscription generations.
- Expose counters for stack-accepted reports and send failures.
- Do not send a report directly from the authentication callback.

Stack acceptance is not proof that iOS moved its pointer. Physical validation is still required.
