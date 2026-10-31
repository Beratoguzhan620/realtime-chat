import { HttpTransportType, HubConnection, HubConnectionBuilder, HubConnectionState, LogLevel } from "@microsoft/signalr";
import type { ConnectionState, Message, PresenceEvent, ReadReceipt, TypingEvent } from "./types";

export type ChatHandlers = {
  messageReceived: (message: Message) => void;
  messageUpdated: (message: Message) => void;
  readUpdated: (receipt: ReadReceipt) => void;
  typing: (event: TypingEvent) => void;
  presenceChanged: (event: PresenceEvent) => void;
  conversationChanged: (conversationId: string) => void;
  stateChanged: (state: ConnectionState) => void;
  /** Called after every (re)connect: whatever happened while offline has to be fetched. */
  connected: () => void;
};

const HEARTBEAT_MS = 20_000;

/**
 * The hub connection. WebSockets only and no negotiate request, so any API instance behind the load balancer
 * can take it. It reconnects forever with growing pauses, and keeps itself counted as online with a heartbeat.
 */
export class ChatConnection {
  private readonly hub: HubConnection;
  private heartbeat: number | undefined;

  constructor(token: () => string, private readonly handlers: ChatHandlers) {
    this.hub = new HubConnectionBuilder()
      .withUrl("/hubs/chat", { accessTokenFactory: token, transport: HttpTransportType.WebSockets, skipNegotiation: true })
      .withAutomaticReconnect({ nextRetryDelayInMilliseconds: (retry) => Math.min(30_000, 500 * 2 ** retry.previousRetryCount) })
      .configureLogging(LogLevel.Warning)
      .build();

    this.hub.on("MessageReceived", handlers.messageReceived);
    this.hub.on("MessageUpdated", handlers.messageUpdated);
    this.hub.on("ReadUpdated", handlers.readUpdated);
    this.hub.on("Typing", handlers.typing);
    this.hub.on("PresenceChanged", handlers.presenceChanged);
    this.hub.on("ConversationChanged", handlers.conversationChanged);
    this.hub.onreconnecting(() => handlers.stateChanged("reconnecting"));
    this.hub.onreconnected(() => {
      handlers.stateChanged("connected");
      handlers.connected();
    });
    this.hub.onclose(() => handlers.stateChanged("disconnected"));
  }

  async start() {
    this.handlers.stateChanged("connecting");
    for (let attempt = 0; ; attempt++) {
      try {
        await this.hub.start();
        break;
      } catch {
        if (this.hub.state !== HubConnectionState.Disconnected) return;
        this.handlers.stateChanged("reconnecting");
        await new Promise((resolve) => setTimeout(resolve, Math.min(30_000, 1000 * 2 ** attempt)));
      }
    }

    this.handlers.stateChanged("connected");
    this.handlers.connected();
    this.heartbeat = window.setInterval(() => {
      if (this.hub.state === HubConnectionState.Connected) {
        this.hub.invoke<boolean>("Heartbeat").then((alive) => {
          // The server forgot this connection (it expired): connect again so presence is right.
          if (!alive) void this.hub.stop().then(() => this.start());
        }).catch(() => {});
      }
    }, HEARTBEAT_MS);
  }

  async stop() {
    window.clearInterval(this.heartbeat);
    await this.hub.stop();
  }

  send(conversationId: string, clientMessageId: string, body: string) {
    return this.call<Message>("Send", conversationId, clientMessageId, body);
  }

  edit(messageId: string, body: string) {
    return this.call<Message>("Edit", messageId, body);
  }

  remove(messageId: string) {
    return this.call<Message>("Delete", messageId);
  }

  markRead(conversationId: string, seq: number) {
    return this.call<number | null>("MarkRead", conversationId, seq);
  }

  typing(conversationId: string) {
    if (this.hub.state === HubConnectionState.Connected) void this.hub.invoke("Typing", conversationId).catch(() => {});
  }

  private async call<T>(method: string, ...args: unknown[]): Promise<T> {
    try {
      return await this.hub.invoke<T>(method, ...args);
    } catch (error) {
      throw new Error(hubErrorMessage(error));
    }
  }
}

/** The server sends "code: message" inside SignalR's own wrapper; people should only see the message. */
export function hubErrorMessage(error: unknown): string {
  const text = error instanceof Error ? error.message : String(error);
  const inner = text.split("HubException: ").pop() ?? text;
  const match = /^[a-z_]+: (.*)$/s.exec(inner);
  return match ? match[1] : "The message could not be sent. Check your connection and try again.";
}
