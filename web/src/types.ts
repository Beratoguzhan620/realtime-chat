export type User = { id: string; displayName: string };

export type Session = { accessToken: string; expiresAt: string; user: User };

export type Member = { userId: string; displayName: string; role: "Owner" | "Member"; lastReadSeq: number };

export type Message = {
  id: string;
  conversationId: string;
  seq: number;
  senderId: string;
  clientMessageId: string;
  body: string;
  createdAt: string;
  editedAt: string | null;
  deletedAt: string | null;
};

export type Conversation = {
  id: string;
  kind: "Direct" | "Group";
  title: string;
  members: Member[];
  lastSeq: number;
  unreadCount: number;
  lastActivityAt: string;
  lastMessage: Message | null;
};

export type MessagePage = { messages: Message[]; hasMore: boolean };

export type ReadReceipt = { conversationId: string; userId: string; seq: number };

export type TypingEvent = { conversationId: string; userId: string };

export type PresenceEvent = { userId: string; online: boolean };

/** A message typed here that the server has not confirmed yet. */
export type Pending = {
  clientMessageId: string;
  conversationId: string;
  body: string;
  createdAt: string;
  failed: boolean;
};

export type ConnectionState = "connecting" | "connected" | "reconnecting" | "disconnected";
