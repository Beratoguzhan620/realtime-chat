import { describe, expect, it } from "vitest";
import { contiguousTail, initialState, mergeMessages, readState, reducer, TYPING_VISIBLE_MS } from "./state";
import type { Conversation, Message } from "./types";

const me = "me";
const other = "other";

function message(seq: number, overrides: Partial<Message> = {}): Message {
  return {
    id: `m${seq}`,
    conversationId: "c1",
    seq,
    senderId: other,
    clientMessageId: `client-${seq}`,
    body: `message ${seq}`,
    createdAt: new Date(2026, 9, 31, 10, seq).toISOString(),
    editedAt: null,
    deletedAt: null,
    ...overrides,
  };
}

function conversation(overrides: Partial<Conversation> = {}): Conversation {
  return {
    id: "c1",
    kind: "Direct",
    title: "Mehmet",
    members: [
      { userId: me, displayName: "Ayşe", role: "Member", lastReadSeq: 0 },
      { userId: other, displayName: "Mehmet", role: "Member", lastReadSeq: 0 },
    ],
    lastSeq: 0,
    unreadCount: 0,
    lastActivityAt: new Date(2026, 9, 31, 9).toISOString(),
    lastMessage: null,
    ...overrides,
  };
}

function loaded() {
  let state = reducer(initialState(me), { type: "conversationsLoaded", conversations: [conversation()] });
  state = reducer(state, { type: "pageLoaded", conversationId: "c1", page: { messages: [], hasMore: false }, direction: "latest" });
  return state;
}

describe("merging messages", () => {
  it("keeps them in order without duplicates and takes the newer copy", () => {
    const merged = mergeMessages([message(1), message(3)], [message(2), message(3, { body: "edited" })]);
    expect(merged.map((m) => m.seq)).toEqual([1, 2, 3]);
    expect(merged[2].body).toBe("edited");
  });

  it("finds where the unbroken run of messages ends", () => {
    expect(contiguousTail(undefined)).toBe(0);
    expect(contiguousTail({ messages: [message(4), message(5), message(7)], hasMoreBefore: true, loaded: true })).toBe(5);
    expect(contiguousTail({ messages: [message(1), message(2)], hasMoreBefore: false, loaded: true })).toBe(2);
  });
});

describe("receiving messages", () => {
  it("adds the message, counts it as unread and updates the preview", () => {
    const state = reducer(loaded(), { type: "messageReceived", message: message(1) });
    expect(state.threads.c1.messages).toHaveLength(1);
    expect(state.conversations.c1.unreadCount).toBe(1);
    expect(state.conversations.c1.lastMessage?.seq).toBe(1);
  });

  it("is idempotent when the same message arrives twice", () => {
    let state = reducer(loaded(), { type: "messageReceived", message: message(1) });
    state = reducer(state, { type: "messageReceived", message: message(1) });
    expect(state.threads.c1.messages).toHaveLength(1);
    expect(state.conversations.c1.unreadCount).toBe(1);
  });

  it("replaces my pending copy and counts my own message as read", () => {
    let state = reducer(loaded(), {
      type: "pendingAdded",
      pending: { clientMessageId: "abc", conversationId: "c1", body: "hi", createdAt: "", failed: false },
    });
    state = reducer(state, { type: "messageReceived", message: message(1, { senderId: me, clientMessageId: "abc" }) });
    expect(state.pending).toHaveLength(0);
    expect(state.conversations.c1.unreadCount).toBe(0);
  });

  it("ends the sender's typing hint", () => {
    let state = reducer(loaded(), { type: "typing", event: { conversationId: "c1", userId: other }, now: 0 });
    state = reducer(state, { type: "messageReceived", message: message(1) });
    expect(state.typing.c1[other]).toBeUndefined();
  });
});

describe("read receipts", () => {
  it("clears my unread count when I read and shows when the other person has read mine", () => {
    let state = reducer(loaded(), { type: "messageReceived", message: message(1) });
    state = reducer(state, { type: "messageReceived", message: message(2, { senderId: me }) });
    expect(readState(state.conversations.c1, me, 2)).toEqual({ read: 0, of: 1 });

    state = reducer(state, { type: "readUpdated", receipt: { conversationId: "c1", userId: other, seq: 2 } });
    expect(readState(state.conversations.c1, me, 2)).toEqual({ read: 1, of: 1 });
    expect(state.conversations.c1.unreadCount).toBe(0);
  });

  it("never moves a read marker back", () => {
    let state = reducer(loaded(), { type: "readUpdated", receipt: { conversationId: "c1", userId: other, seq: 5 } });
    state = reducer(state, { type: "readUpdated", receipt: { conversationId: "c1", userId: other, seq: 2 } });
    expect(state.conversations.c1.members.find((m) => m.userId === other)?.lastReadSeq).toBe(5);
  });
});

describe("typing and presence", () => {
  it("shows typing for a few seconds", () => {
    let state = reducer(loaded(), { type: "typing", event: { conversationId: "c1", userId: other }, now: 1000 });
    expect(Object.keys(state.typing.c1)).toEqual([other]);
    state = reducer(state, { type: "typingExpired", now: 1000 + TYPING_VISIBLE_MS + 1 });
    expect(Object.keys(state.typing.c1)).toEqual([]);
  });

  it("tracks who is online", () => {
    let state = reducer(loaded(), { type: "presenceLoaded", online: [other] });
    expect(state.online[other]).toBe(true);
    state = reducer(state, { type: "presenceChanged", event: { userId: other, online: false } });
    expect(state.online[other]).toBeUndefined();
  });
});
