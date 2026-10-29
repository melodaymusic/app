# Security and privacy

Implemented in Meloday Music 0.61.0, on the 0.60.0 source. This is an implementation review and regression coverage, not an independent security audit.

## Plex connections

Settings > Plex uses the HTTP fallback choices documented by official Plex apps:

- Never (default): HTTPS endpoints only.
- On same network: HTTP only for connections Plex identifies as local, excluding relays.
- Always: HTTP permitted for local and remote endpoints.

All eligible HTTPS endpoints are tried before HTTP. A cached HTTP address cannot override that rule.
Offline fallback identifies literal private/loopback IPs as local; a saved hostname is not assumed local when
plex.tv is unavailable. Settings shows HTTPS or HTTP. Withdrawing permission stops a disallowed active stream
and reconnects, preserving the queue.

Authenticated Plex API calls use a dedicated transport with automatic redirects disabled. TLS certificate
validation remains enabled. Artwork and media retain their public-download/Windows playback transports;
this release does not independently audit the Windows media stack.

## Plex Companion

The archived official Plex Media Player and third-party Plex MPV Shim sources support tokenless local Companion
requests. Controller identifiers identify clients; they are not authentication secrets. Requiring every controller
to send Meloday's account token would be incompatible with that behaviour.

Meloday retains **trusted-network control**, rather than claiming account-authenticated control:

- Disabled by default for new installations; existing saved choices preserved.
- Private/link-local/loopback source addresses only.
- Browser Origin limited to hosted Plex Web or the connected server's origin. Unrelated origins and cross-site
  navigations omitting Origin are rejected before routing.
- Host must match the listener's local address/port, preventing requests through attacker-owned DNS names.
- Commands/timelines require a Plex client identifier; an explicit target must match this player.
- At most 32 connections, 180 requests per source per minute, 256 tracked sources, 16 callback subscribers,
  16 KB headers/bodies, and ten seconds to read a request.
- Callback subscriptions bind to their source IP, expire, validate ports, and cannot follow redirects.
- Play queues must address numeric /playQueues/{id} endpoints on the already connected server.

Enabling this feature still trusts reachable devices on the private network. A native client identifier can be
imitated. These checks do not provide cryptographically authenticated or encrypted control. Meloday adds no
firewall rules or router port forwarding.

## Storage and diagnostics

Credentials/API keys use Windows DPAPI CurrentUser encryption. This does not protect against malware running
as the user. Music caches, queue state, radio history, and pending scrobbles remain ordinary per-user files.

Logs redact labelled tokens, PINs, API keys/secrets, session keys, passwords, bearer/basic credentials and tokens
nested in URL-encoded queries. Structured track, artist, account, server, library and device fields are masked.
Addresses, paths, error messages and music identifiers may remain: review logs before sharing. Logs are not
automatically uploaded.

Logs expire after seven days, including while the app remains running, with a daily size cap. Settings > About
offers Clear Logs, which discards pending writes from before the clear operation too.

## External services

- Plex: account/resource discovery, library requests, streaming/downloads, configured playback reporting,
  playlist writes, and player discovery when Companion is enabled.
- Last.fm: public charts when enabled; song/artist/timestamp metadata when scrobbling is enabled.
  Requires the listener's own encrypted API key; no key is bundled. Chart access does not require a personal
  scrobbling session. Last.fm Home options are hidden when there is no key.
  The chart website runs in WebView2 and may load its own cookies and third-party resources.
- MusicBrainz: opt-in public artist catalogue requests, using the artist's MusicBrainz ID or an exact,
  unambiguous name lookup. No account or token. Requests identify Meloday and are limited to one per 1.1 seconds;
  results are cached for 24 hours. Cover Art Archive receives release-group IDs for artwork, without Plex credentials.
- ListenBrainz: the user explicitly connects their own token; listening reporting is a separate switch,
  initially off. Reports contain song, artist, album, duration and listening timestamp after the listening threshold
  is met. The token and bounded retry queue use per-user Windows DPAPI encryption. Token-bearing requests use
  a fixed HTTPS API origin, an Authorization header and a transport that does not follow redirects. Disconnecting
  or disabling reporting cancels outstanding work and discards pending listens. Connecting/refreshing reads the
  user's total listen count, even while listening reporting is off.
- LRCLIB: song title, artist, album and duration for enabled lyric fallback.
- fanart.tv: artist MusicBrainz ID and configured API credentials for logos.
- OpenAI: mix genre, mood, descriptor, era, day/time period and avoided words for optional AI titles.
  It does not receive the Plex token or a playlist track list.

The relevant settings explain these uses. Existing feature choices are preserved.

## References

- [Official Plex Android TV settings](https://support.plex.tv/articles/settings-android-tv/)
- [Official Plex Roku settings](https://support.plex.tv/articles/204275243-settings-plex-for-roku/)
- [Official Plex secure connections](https://support.plex.tv/articles/206225077-how-to-use-secure-server-connections/)
- [Official Plex Companion explanation](https://support.plex.tv/articles/201812803-plex-companion-remote-control-issues/)
- [Archived official Plex Media Player source](https://github.com/plexinc/plex-media-player/blob/master/src/remote/RemoteComponent.cpp)
- [Plex MPV Shim source](https://github.com/iwalton3/plex-mpv-shim/blob/master/plex_mpv_shim/client.py)

The archived client is historical protocol evidence, not a claim about all current closed-source Plex apps.
