# Roadmap

Realtime Chat is a messaging backend with a web client: direct and group conversations, live delivery over
SignalR, presence, typing indicators and read receipts. It runs as several instances behind a load balancer and
keeps every message in order, even across reconnects.

## Decisions

- **Ordering by a number per conversation.** Every message takes the next `seq` of its conversation in the same
  transaction that stores it (`UPDATE … RETURNING`). Clients render by `seq`, notice gaps and ask for everything
  after the last number they have, so a reconnect never loses or duplicates a message.
- **Idempotent sends.** The client gives every message its own id; sending it again (after a timeout or a
  reconnect) returns the stored message instead of a copy.
- **PostgreSQL for what must survive, Redis for what is live.** Messages, conversations and read markers are in
  PostgreSQL. Presence is in Redis with expiring heartbeats, and Redis is the SignalR backplane that lets people
  on different instances talk.
- **No sticky sessions.** Clients connect with WebSockets directly (no negotiate round trip), so any instance
  can take any connection.
- **Messages are private.** Message bodies never appear in logs; someone outside a conversation gets
  "not found" for it, never "forbidden".
- **Offline people get one email, not one per message.** When a message stays unread for a while and its reader
  is offline, one email goes out through the [notification-service](https://github.com/Beratoguzhan620/notification-service).
- **Web client in React and TypeScript** (Vite), served by nginx, which also balances the API instances.

## Stages

| # | Stage | Contents |
|---|---|---|
| 1 | Skeleton | Accounts with JWT, direct and group conversations, members and owners, PostgreSQL, Compose, CI |
| 2 | Messaging | SignalR hub, per-conversation sequence numbers, idempotent sends, catching up after a reconnect, history with a cursor, edit and delete, rate limit |
| 3 | Presence and scale | Presence across tabs and devices, typing, read receipts and unread counts, Redis backplane with two API instances behind nginx, offline email through the notification-service |
| 4 | Web client | React + TypeScript: conversation list, messages, typing, read ticks, online dots, reconnect state, phone layout; a Playwright test with two browsers |
| 5 | Load and docs | Load test with thousands of connections and published numbers, README in English and Turkish, screenshots and GIF, portfolio card |

## Later

- Attachments through object storage with signed URLs.
- Push notifications for the mobile apps.
- End-to-end encryption for direct conversations.
