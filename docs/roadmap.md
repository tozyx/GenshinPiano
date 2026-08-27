# GenshinPiano v3 Roadmap

## Completed in the current iteration

- [x] Add per-Windows-session single-instance coordination.
  - Use a named mutex to prevent concurrent writers to settings, recovery data, and update caches.
  - Forward supported score paths from a second launch to the existing instance through a named pipe.
  - Restore and activate the existing main window when a forwarded open request arrives.
  - Detect the normal/elevated privilege boundary and show a clear instruction when forwarding is blocked.

- [x] Improve update-source racing when GitHub and GitCode publish different latest versions.
  - Keep the low-latency first-valid-response behavior.
  - After the first valid manifest arrives, allow the other source a short grace period (initial target: 500–1000 ms).
  - If both sources respond during that window, select the higher semantic version.
  - If the other source fails or exceeds the grace period, immediately use the first valid result.
  - Add diagnostics for source latency, selected mirror, selected version, and fallback reason.
